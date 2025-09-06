using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using OT.Assessment.Infrastructure.Options;

namespace OT.Assessment.Infrastructure.Messaging
{
    public class RabbitMqPublisher : IDisposable
    {
        private readonly RabbitMqOptions _opts;
        private readonly ILogger<RabbitMqPublisher> _logger;
        private readonly object _syncRoot = new();
        private readonly AsyncRetryPolicy _retryPolicy;

        private IConnection? _connection;
        private IModel? _channel;

        private const string DeadLetterExchange = "dlx.direct";
        private const string DeadLetterQueue = "poison.queue";

        public RabbitMqPublisher(IOptions<RabbitMqOptions> opts, ILogger<RabbitMqPublisher> logger)
        {
            _opts = opts.Value;
            _logger = logger;

            // Retry policy: 3 attempts, exponential backoff
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (ex, ts, attempt, ctx) =>
                    {
                        _logger.LogWarning(ex, "Retry {Attempt} after {Delay} due to publish failure", attempt, ts);
                    });

            InitializeConnection();
        }

        private void InitializeConnection()
        {
            lock (_syncRoot)
            {
                try
                {
                    if (_connection != null && _connection.IsOpen) return;

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

                    _connection?.Dispose();
                    _connection = factory.CreateConnection();
                    _logger.LogInformation("RabbitMQ connection established");

                    _channel?.Dispose();
                    _channel = CreateChannel();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize RabbitMQ connection/channel");
                    throw;
                }
            }
        }

        private IModel CreateChannel()
        {
            var channel = _connection!.CreateModel();

            // Dead letter setup
            channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true);
            channel.QueueDeclare(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(DeadLetterQueue, DeadLetterExchange, "poison");

            // Main queue/exchange with DLQ
            channel.ExchangeDeclare(_opts.Exchange, ExchangeType.Direct, durable: true, autoDelete: false);
            channel.QueueDeclare(
                queue: _opts.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", DeadLetterExchange },
                    { "x-dead-letter-routing-key", "poison" }
                });
            channel.QueueBind(_opts.Queue, _opts.Exchange, _opts.RoutingKey);

            // Fair dispatch
            channel.BasicQos(0, 1, false);

            _logger.LogInformation("RabbitMQ channel created. Exchange={Exchange}, Queue={Queue}, DLQ={DeadLetterQueue}",
                _opts.Exchange, _opts.Queue, DeadLetterQueue);

            return channel;
        }

        private IModel GetOrCreateChannel()
        {
            lock (_syncRoot)
            {
                if (_connection == null || !_connection.IsOpen)
                {
                    _logger.LogWarning("RabbitMQ connection is closed. Reinitializing...");
                    InitializeConnection();
                }

                if (_channel == null || !_channel.IsOpen)
                {
                    _logger.LogWarning("RabbitMQ channel is closed. Recreating...");
                    _channel?.Dispose();
                    _channel = CreateChannel();
                }

                return _channel;
            }
        }

        public async Task PublishAsync(string routingKey, object message)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                var channel = GetOrCreateChannel();

                var payload = JsonSerializer.SerializeToUtf8Bytes(message);
                var props = channel.CreateBasicProperties();
                props.Persistent = true;

                channel.BasicPublish(
                    exchange: _opts.Exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: props,
                    body: payload
                );

                _logger.LogInformation("Message published to {Exchange} with routingKey={RoutingKey}", _opts.Exchange, routingKey);

                await Task.CompletedTask;
            });
        }

        public void Dispose()
        {
            try
            {
                _channel?.Close();
                _channel?.Dispose();
                _connection?.Close();
                _connection?.Dispose();
                _logger.LogInformation("RabbitMQ publisher disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RabbitMQ publisher");
            }
        }
    }
}
