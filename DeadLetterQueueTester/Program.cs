using OT.Assessment.Core.Dtos;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();

var testDto = new CasinoWagerDto
{
    WagerId = Guid.NewGuid(),
    TransactionId = Guid.NewGuid(),
    AccountId = Guid.NewGuid(),
    BrandId = Guid.NewGuid(),
    Username = "TestUser",
    CountryCode = "ZA",
    GameName = "Poker",
    Theme = "Classic",
    Provider = "ProviderX",
    Amount = 50.0m,
    NumberOfBets = 2,
    Duration = 1200,
    SessionData = "{}",
    CreatedDateTime = DateTime.UtcNow
};

var json = JsonSerializer.Serialize(testDto);
var body = Encoding.UTF8.GetBytes(json);

channel.BasicPublish(
    exchange: "dlx.direct",
    routingKey: "poison",
    basicProperties: null,
    body: body
);

Console.WriteLine("Test message sent to DLQ");
