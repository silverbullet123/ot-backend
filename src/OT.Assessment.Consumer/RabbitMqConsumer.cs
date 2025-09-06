using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Application.Services;
using OT.Assessment.Infrastructure.Options;

namespace OT.Assessment.Consumer
{
    public class RabbitMqConsumer : BackgroundService
    {
        private readonly ILogger<RabbitMqConsumer> _logger;
        private readonly IServiceProvider _sp;
        private readonly RabbitMqOptions _opts;

        private IConnection? _connection;
        private IModel? _channel;

        private readonly SemaphoreSlim _semaphore = new(50); // limit concurrent message processing
        private readonly AsyncRetryPolicy _retryPolicy;

        private const string DeadLetterExchange = "dlx.direct";
        private const string DeadLetterQueue = "poison.queue";

        public RabbitMqConsumer(ILogger<RabbitMqConsumer> logger, IServiceProvider sp, IOptions<RabbitMqOptions> opts)
        {
            _logger = logger;
            _sp = sp;
            _opts = opts.Value;

            // Retry policy for transient errors
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3,
                    attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                    (ex, ts, attempt, ctx) =>
                        _logger.LogWarning(ex, "Retry {Attempt} after {Delay} ms", attempt, ts.TotalMilliseconds));
        }

        // Keeps the consumer running & tries to reconnect on errors
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    EnsureConnected();

                    var consumer = new AsyncEventingBasicConsumer(_channel!);
                    consumer.Received += async (sender, ea) =>
                    {
                        await _semaphore.WaitAsync(stoppingToken);
                        using var scope = _sp.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<WagerService>();

                        try
                        {
                            var json = Encoding.UTF8.GetString(ea.Body.Span);
                            var dto = JsonSerializer.Deserialize<CasinoWagerDto>(
                                json,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            )!;

                            // Process message with retry
                            await _retryPolicy.ExecuteAsync(async () =>
                                await svc.InsertWagerAsync(dto, stoppingToken));

                            // Acknowledge with retry
                            await _retryPolicy.ExecuteAsync(() =>
                            {
                                _channel!.BasicAck(ea.DeliveryTag, false);
                                return Task.CompletedTask;
                            });

                            _logger.LogInformation("ACKed WagerId={WagerId}", dto.WagerId);
                        }
                        catch (Exception ex)
                        {
                            var json = Encoding.UTF8.GetString(ea.Body.Span);
                            _logger.LogError(ex, "Processing failed, sending to DLQ. Payload={Payload}", json);

                            // Save poison payload (optional: to DB, blob, or file)
                            SavePoisonMessage(json, ex);

                            await _retryPolicy.ExecuteAsync(() =>
                            {
                                _channel!.BasicNack(ea.DeliveryTag, false, requeue: false);
                                return Task.CompletedTask;
                            });
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    };

                    _channel!.BasicConsume(queue: _opts.Queue, autoAck: false, consumer: consumer);

                    // Keep consumer alive
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RabbitMQ consumer crashed. Retrying in 5s...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private void EnsureConnected()
        {
            if (_connection != null && _connection.IsOpen && _channel != null && _channel.IsOpen)
                return;

            var factory = new ConnectionFactory
            {
                HostName = _opts.HostName,
                Port = _opts.Port,
                UserName = _opts.UserName,
                Password = _opts.Password,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                TopologyRecoveryEnabled = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _connection.ConnectionShutdown += (s, ea) =>
            {
                _logger.LogWarning("RabbitMQ connection shutdown: {Reason}", ea.ReplyText);
                _connection = null;
                _channel = null;
            };
            _channel.ModelShutdown += (s, ea) =>
            {
                _logger.LogWarning("RabbitMQ channel shutdown: {Reason}", ea.ReplyText);
                _channel = null;
            };

            // Declare queues & DLQ
            _channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(DeadLetterQueue, DeadLetterExchange, "poison");

            _channel.ExchangeDeclare(_opts.Exchange, ExchangeType.Direct, durable: true, autoDelete: false);
            _channel.QueueDeclare(
                queue: _opts.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", DeadLetterExchange },
                    { "x-dead-letter-routing-key", "poison" }
                });
            _channel.QueueBind(_opts.Queue, _opts.Exchange, _opts.RoutingKey);

            _channel.BasicQos(0, 1, false);

            _logger.LogInformation("RabbitMQ consumer connected. Queue={Queue}", _opts.Queue);
        }

        private void SavePoisonMessage(string payload, Exception ex)
        {
            try
            {
                // For now, just log. Replace with DB insert or file write
                _logger.LogError("Poison message logged. Payload={Payload}, Error={Error}", payload, ex.Message);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Failed to log poison message");
            }
        }

        public override void Dispose()
        {
            try
            {
                _channel?.Close();
                _channel?.Dispose();
                _connection?.Close();
                _connection?.Dispose();
                _logger.LogInformation("RabbitMQ consumer disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RabbitMQ consumer");
            }

            base.Dispose();
        }
    }
}
