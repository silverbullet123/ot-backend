using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using OT.Assessment.Application.Services;
using OT.Assessment.Application.Interfaces;
using OT.Assessment.Infrastructure.Data;
using OT.Assessment.Infrastructure.Repositories;
using OT.Assessment.Infrastructure.Messaging;
using OT.Assessment.Infrastructure.Options;
using OT.Assessment.Consumer;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
    })
    .ConfigureServices((context, services) =>
    {
        //configure services
        services.Configure<RabbitMqOptions>(context.Configuration.GetSection("RabbitMq"));
        services.AddSingleton<DapperConnectionFactory>();
        services.AddScoped<IWagerRepository, WagerRepository>();
        services.AddScoped<WagerService>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddHostedService<RabbitMqConsumer>();
        services.AddHostedService<DeadLetterConsumer>();
        services.AddScoped<FailedWagerService>();
        services.AddScoped<IFailedWagerRepository, FailedWagerRepository>();

    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application started {time:yyyy-MM-dd HH:mm:ss}", DateTime.Now);

await host.RunAsync();

logger.LogInformation("Application ended {time:yyyy-MM-dd HH:mm:ss}", DateTime.Now);