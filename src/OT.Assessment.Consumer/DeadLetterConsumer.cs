using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OT.Assessment.Application.Services;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Infrastructure.Options;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace OT.Assessment.Consumer
{
    public class DeadLetterConsumer : BackgroundService
    {
        private readonly ILogger<DeadLetterConsumer> _logger;
        private readonly IServiceProvider _sp;
        private readonly RabbitMqOptions _opts;
        private IConnection? _connection;
        private IModel? _channel;

        private const string DeadLetterExchange = "dlx.direct";
        private const string DeadLetterQueue = "poison.queue";

        public DeadLetterConsumer(ILogger<DeadLetterConsumer> logger, IServiceProvider sp, IOptions<RabbitMqOptions> opts)
        {
            _logger = logger;
            _sp = sp;
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
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(DeadLetterQueue, DeadLetterExchange, "poison");

            _channel.BasicQos(0, 10, false); // max 10 unacked messages
            _logger.LogInformation("DLQ Consumer started. Queue={Queue}", DeadLetterQueue);

            return base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_channel is null) throw new InvalidOperationException("Channel not initialized");

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                using var scope = _sp.CreateScope();
                var failedSvc = scope.ServiceProvider.GetRequiredService<FailedWagerService>();

                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var dto = JsonSerializer.Deserialize<CasinoWagerDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    await failedSvc.SaveFailedWagerAsync(dto);

                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing DLQ message");
                    _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
                }
            };

            _channel.BasicConsume(queue: DeadLetterQueue, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            base.Dispose();
        }
    }
}
