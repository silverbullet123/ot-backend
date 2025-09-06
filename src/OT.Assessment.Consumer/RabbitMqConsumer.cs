using System;
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

            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                    (ex, ts, attempt, ctx) => _logger.LogWarning(ex, "Retry {Attempt} after {Delay} ms", attempt, ts.TotalMilliseconds));
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
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

            // DLX & poison queue
            _channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(DeadLetterQueue, DeadLetterExchange, "poison");

            // Main exchange/queue with DLX
            _channel.ExchangeDeclare(_opts.Exchange, ExchangeType.Direct, durable: true, autoDelete: false);
            _channel.QueueDeclare(
                queue: _opts.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new System.Collections.Generic.Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", DeadLetterExchange },
                    { "x-dead-letter-routing-key", "poison" }
                });
            _channel.QueueBind(_opts.Queue, _opts.Exchange, _opts.RoutingKey);

            _channel.BasicQos(0, 1, false);

            _logger.LogInformation("RabbitMQ consumer started. Queue={Queue}, DLQ={DeadLetterQueue}", _opts.Queue, DeadLetterQueue);

            return base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_channel is null) throw new InvalidOperationException("Channel not initialized");

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                await _semaphore.WaitAsync(stoppingToken);
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<WagerService>();

                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var dto = JsonSerializer.Deserialize<CasinoWagerDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

                    await _retryPolicy.ExecuteAsync(async () => await svc.InsertWagerAsync(dto, stoppingToken));

                    _channel.BasicAck(ea.DeliveryTag, false);
                    _logger.LogInformation("Message processed successfully and ACKed. WagerId={WagerId}", dto.WagerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message. Sending to DLQ...");
                    _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
                }
                finally
                {
                    _semaphore.Release();
                }
            };

            _channel.BasicConsume(queue: _opts.Queue, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            try
            {
                _channel?.Close();
                _connection?.Close();
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
