// DriverService — drivers, availability and ride offers.
// REST in (frontend + MatchingService), gRPC in (MatchingService), RabbitMQ out/in. See README.md.
//
// Two Kestrel endpoints (appsettings.json → Kestrel:Endpoints):
//   Http (HTTP/1.1) for REST, Grpc (HTTP/2 only) for gRPC. Without TLS one port cannot serve both.

using System.Text.Json.Serialization;
using DriverService.Data;
using DriverService.Messaging;
using DriverService.Services;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGrpc();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiExceptionHandling();

builder.Services.AddServiceIdentity(OfferService.Source);
builder.Services.AddDbContext<DriverDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.Configure<OfferOptions>(builder.Configuration.GetSection("Offers"));
builder.Services.AddScoped<DriverAvailabilityQuery>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddHostedService<OfferExpiryService>();

builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddRabbitMqConsumer<DriverMessageHandler>(RabbitMqTopology.Queues.DriverService);
builder.Services.AddOutbox<DriverDbContext>();
builder.Services.AddMetricsReporter(builder.Configuration);

var app = builder.Build();

await app.Services.RetryOnStartupAsync("Apply database migrations",
    sp => sp.GetRequiredService<DriverDbContext>().Database.MigrateAsync());

if (app.Environment.IsDevelopment())
{
    // OpenAPI document: /openapi/v1.json
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseCors();

app.MapControllers();
app.MapGrpcService<DriverAvailabilityGrpcService>();
app.MapHealthChecks("/health");

app.Run();
