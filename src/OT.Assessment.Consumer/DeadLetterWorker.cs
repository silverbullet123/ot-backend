using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OT.Assessment.Infrastructure.Options;

namespace OT.Assessment.Consumer
{
    public class DeadLetterWorker : BackgroundService
    {
        private readonly ILogger<DeadLetterWorker> _logger;
        private readonly RabbitMqOptions _opts;
        private IConnection? _connection;
        private IModel? _channel;

        private const string DeadLetterExchange = "dlx.direct";
        private const string DeadLetterQueue = "poison.queue";

        public DeadLetterWorker(ILogger<DeadLetterWorker> logger, IOptions<RabbitMqOptions> opts)
        {
            _logger = logger;
            _opts = opts.Value;
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

            // Make sure DLX exists
            _channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(DeadLetterQueue, DeadLetterExchange, "poison");

            _logger.LogInformation("DeadLetterWorker started. Listening to {DeadLetterQueue}", DeadLetterQueue);

            return base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_channel is null) throw new InvalidOperationException("Channel not initialized");

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                try
                {
                    // Just log it for now
                    _logger.LogWarning("DLQ message received: {Message}", message);

                    // Optionally deserialize for further inspection
                    // var obj = JsonSerializer.Deserialize<CasinoWagerDto>(message);

                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process DLQ message. Requeueing...");
                    _channel.BasicNack(ea.DeliveryTag, false, requeue: true);
                }

                await Task.Yield();
            };

            _channel.BasicConsume(queue: DeadLetterQueue, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            try
            {
                _channel?.Close();
                _connection?.Close();
                _logger.LogInformation("DeadLetterWorker disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing DeadLetterWorker");
            }

            base.Dispose();
        }
    }
}
