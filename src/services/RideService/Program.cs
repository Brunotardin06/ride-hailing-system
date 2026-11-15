// RideService — Port 5001 (REST, HTTP/1.1) and 5011 (gRPC, HTTP/2). Owns rides (source of truth for ride status).
// Two ports because without TLS one port cannot serve both HTTP/1.1 and HTTP/2 (see Kestrel in appsettings.json).
//   1. builder.Services... → register services in the DI container
//   2. app.Use/Map...      → the HTTP pipeline (order matters)

using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;
using RideService.Data;
using RideService.Grpc;
using RideService.Messaging;
using RideService.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Services ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())); // "Requested", not 0
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiExceptionHandling();

// The interceptor wakes the outbox dispatcher after every SaveChanges.
builder.Services.AddDbContext<RideDbContext>((sp, o) => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
    .AddInterceptors(sp.GetRequiredService<OutboxNotifyInterceptor>()));
builder.Services.AddScoped<RideWorkflow>();

// RabbitMQ: publish via the outbox, consume queue "ride-service".
builder.Services.AddRabbitMq(builder.Configuration, RideWorkflow.Source);
builder.Services.AddOutbox<RideDbContext>();
builder.Services.AddRabbitMqConsumer<RideMessageHandler>(RabbitMqTopology.Queues.RideService);

// gRPC server (port 5011, HTTP/2): AssignDriver, called by DriverService when a driver accepts an offer.
builder.Services.AddGrpc();

var app = builder.Build();

await app.Services.RetryOnStartupAsync("Apply database migrations",
    sp => sp.GetRequiredService<RideDbContext>().Database.MigrateAsync());

// ---------- 2. HTTP pipeline ----------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

app.UseExceptionHandler();
app.UseCors();

app.MapControllers();                    // REST  → port 5001
app.MapGrpcService<RidesGrpcService>();  // gRPC  → port 5011
app.MapHealthChecks("/health");

app.Run();
