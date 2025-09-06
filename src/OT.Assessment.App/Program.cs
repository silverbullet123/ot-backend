using System.Reflection;
using Microsoft.Extensions.Options;
using OT.Assessment.Application.Services;
using OT.Assessment.Application.Interfaces;
using OT.Assessment.Infrastructure.Data;
using OT.Assessment.Infrastructure.Repositories;
using OT.Assessment.Infrastructure.Messaging;
using OT.Assessment.Infrastructure.Options;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Application.Dtos;
using OT.Assessment.Application.Helpers;
using Microsoft.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// configs
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));

// register infrastructure / application
builder.Services.AddSingleton<DapperConnectionFactory>();
builder.Services.AddScoped<IWagerRepository, WagerRepository>();
builder.Services.AddScoped<WagerService>();

builder.Services.AddScoped<IFailedWagerRepository, FailedWagerRepository>();
builder.Services.AddScoped<FailedWagerService>();


// Rabbit publisher
builder.Services.AddSingleton<RabbitMqPublisher>();

builder.Services.AddSingleton<HttpClient>(); 

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckl
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(opts =>
    {
        opts.EnableTryItOutByDefault();
        opts.DocumentTitle = "OT Assessment App";
        opts.DisplayRequestDuration();
    });
}

//// POST -> publish to queue
//app.MapPost("/api/player/casinowager", async (CasinoWagerDto dto, RabbitMqPublisher publisher, WagerService svc) =>
//{
//    // perform light validation via service (or before publishing)
//    await svc.InsertWagerAsync(dto); // optional: we might not want to insert here; instead validate only
//    // NOTE: if you want API-only publish (consumer writes DB), remove the InsertWagerAsync call above.
//    // For the assessment we only publish and let consumer write, so better to validate and publish only:
//    await publisher.PublishAsync("ot.casino.wager", dto);
//    return Results.Accepted();
//}).WithOpenApi();

//// GET paged wagers for player (reads DB directly via service)
//app.MapGet("/api/player/{playerId:guid}/casino", async (Guid playerId, int page, int pageSize, WagerService svc) =>
//{
//    var res = await svc.GetPlayerWagersAsync(playerId, page, pageSize);
//    return Results.Ok(res);
//}).WithOpenApi();

//app.MapGet("/api/player/topSpenders", async (int count, WagerService svc) =>
//{
//    var data = await svc.GetTopSpendersAsync(count);
//    return Results.Ok(data);
//}).WithOpenApi();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
