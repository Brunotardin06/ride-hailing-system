// DriverService — Port 5002 (REST, HTTP/1.1) and 5012 (gRPC, HTTP/2).
//   1. builder.Services... → register services in the DI container ("what exists")
//   2. app.Use/Map...      → the HTTP pipeline ("what happens to each request, in order")

using System.Text.Json.Serialization;
using DriverService.Data;
using DriverService.Grpc;
using DriverService.Messaging;
using DriverService.Services;
using Microsoft.EntityFrameworkCore;
using RideHailing.Grpc.Rides;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Services (dependency injection) ----------
builder.Services.AddControllers()
    // Enums as text in JSON: "Available" instead of 1 (the frontend expects strings).
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiExceptionHandling();

// DbContext → PostgreSQL database "drivers". The interceptor wakes the outbox dispatcher after every SaveChanges.
builder.Services.AddDbContext<DriverDbContext>((sp, o) => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
    .AddInterceptors(sp.GetRequiredService<OutboxNotifyInterceptor>()));

// Scoped = one instance per HTTP request / per message (same lifetime as the DbContext they use).
builder.Services.AddScoped<DriverManager>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddHostedService<OfferExpiryService>();

// RabbitMQ: publish via the outbox, consume queue "driver-service".
builder.Services.AddRabbitMq(builder.Configuration, OfferService.Source);
builder.Services.AddOutbox<DriverDbContext>();
builder.Services.AddRabbitMqConsumer<DriverMessageHandler>(RabbitMqTopology.Queues.DriverService);

// gRPC server (port 5012): driver lookup + create offer, called by MatchingService.
builder.Services.AddGrpc();
builder.Services.AddScoped<DriverAvailabilityQuery>();

// gRPC client → RideService (AssignDriver on accept). The factory manages the HTTP/2 connection (reused, not per call).
builder.Services.AddGrpcClient<Rides.RidesClient>(o => o.Address = new Uri(builder.Configuration["ServiceUrls:RideServiceGrpc"]!));

var app = builder.Build();

// Create/update the tables on startup (retries while Postgres is still starting).
await app.Services.RetryOnStartupAsync("Apply database migrations",
    sp => sp.GetRequiredService<DriverDbContext>().Database.MigrateAsync());

// ---------- 2. HTTP pipeline (order matters) ----------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

app.UseExceptionHandler(); // NotFound/Conflict/Forbidden exceptions → 404/409/403 JSON
app.UseCors();             // allow the frontend origin

app.MapControllers();                      // REST → port 5002
app.MapGrpcService<DriversGrpcService>();  // gRPC → port 5012
app.MapHealthChecks("/health");

app.Run();
