using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using OT.Assessment.Infrastructure.Options;

namespace OT.Assessment.Infrastructure.Messaging
{
     public class RabbitMqPublisher : IDisposable
    {
        private readonly IConnection _connection;
        //private readonly IModel _channel;
        private readonly IModel _channel;
        private readonly RabbitMqOptions _opts;

        public RabbitMqPublisher(IOptions<RabbitMqOptions> opts)
        {
            _opts = opts.Value;
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
        }

        public Task PublishAsync(string routingKey, object message)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            _channel.BasicPublish(exchange: _opts.Exchange, routingKey: routingKey, basicProperties: props, body: payload);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
