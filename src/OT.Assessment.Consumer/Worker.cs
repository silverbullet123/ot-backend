using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Application.Services;
using OT.Assessment.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace OT.Assessment.Consumer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _sp;
        private readonly RabbitMqOptions _opts;
        private IConnection? _connection;
        private IModel? _channel;

        public Worker(ILogger<Worker> logger, IServiceProvider sp, IOptions<RabbitMqOptions> opts)
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
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_opts.Exchange, ExchangeType.Direct, durable: true, autoDelete: false);
            _channel.QueueDeclare(_opts.Queue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(_opts.Queue, _opts.Exchange, _opts.RoutingKey);
            _channel.BasicQos(0, 100, false);

            return base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_channel is null) throw new InvalidOperationException("Channel not initialized");

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<WagerService>();

                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var dto = JsonSerializer.Deserialize<CasinoWagerDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    await svc.InsertWagerAsync(dto, stoppingToken); // application-level call
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                    _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
                }
            };

            _channel.BasicConsume(queue: _opts.Queue, autoAck: false, consumer: consumer);
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
