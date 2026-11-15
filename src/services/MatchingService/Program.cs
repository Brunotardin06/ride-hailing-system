// MatchingService — Port 5003 (health only). Stateless: no database, no REST API for the frontend.
// Input:  RabbitMQ queue "matching-service" (ride.requested, driver.rejected-ride), Kafka "driver-locations"
// Output: gRPC calls to DriverService (lookup + create offer) and ride.matching-failed on RabbitMQ.

using MatchingService.Messaging;
using MatchingService.Services;
using RideHailing.Grpc.Drivers;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Kafka;
using RideHailing.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddScoped<MatchingWorkflow>();
builder.Services.AddSingleton<ProcessedMessageCache>(); // singleton: the memory must outlive one message

// gRPC client → DriverService:5012. Generated from DriverService/Protos/drivers.proto (see MatchingService.csproj).
builder.Services.AddGrpcClient<Drivers.DriversClient>(o => o.Address = new Uri(builder.Configuration["ServiceUrls:DriverServiceGrpc"]!));
builder.Services.AddTransient<DriverDirectory>();
builder.Services.AddTransient<OfferClient>();

// Kafka: read driver positions (own consumer group) → last known position per driver → nearest driver.
builder.Services.AddSingleton<DriverLocationCache>();
builder.Services.AddDriverLocationConsumer<MatchingLocationHandler>(builder.Configuration, groupId: "matching-service");

builder.Services.AddRabbitMq(builder.Configuration, MatchingWorkflow.Source);
builder.Services.AddRabbitMqConsumer<MatchingMessageHandler>(RabbitMqTopology.Queues.MatchingService);

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
