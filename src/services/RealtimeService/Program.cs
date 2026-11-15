// RealtimeService — Port 5004. No database, no business logic: the bridge between backend events and browsers.
// Input:  RabbitMQ queue "realtime-service" (ride / offer events), SignalR from browsers (chat, driver GPS),
//         Kafka "driver-locations"
// Output: SignalR pushes (notifications, chat, driver position), Kafka "driver-locations" (producer)

using RealtimeService.Hubs;
using RealtimeService.Messaging;
using RealtimeService.Services;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Kafka;
using RideHailing.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(); // part of ASP.NET Core, no extra package
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration); // SignalR needs CORS with AllowCredentials

// State shared by all connections → singletons.
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<ChatHistory>();
builder.Services.AddSingleton<ProcessedMessageCache>();
builder.Services.AddSingleton<NotificationService>();

builder.Services.AddSingleton<ActiveRideTracker>();

// RabbitMQ: ride/offer events → notifications (and which passenger follows which driver).
builder.Services.AddRabbitMq(builder.Configuration, "realtime-service");
builder.Services.AddRabbitMqConsumer<RealtimeMessageHandler>(RabbitMqTopology.Queues.RealtimeService);

// Kafka: the hub PRODUCES the driver's GPS to "driver-locations"; this service also CONSUMES it (own group)
// to push the position to the passenger. MatchingService reads the same topic with its own group.
// (With several Realtime instances, each would need its own group id so every instance sees every position.)
builder.Services.AddDriverLocationProducer(builder.Configuration);
builder.Services.AddDriverLocationConsumer<RealtimeLocationHandler>(builder.Configuration, groupId: "realtime-service");

var app = builder.Build();

app.UseCors();

app.MapHub<RideHub>("/hubs/rides"); // the frontend connects to http://localhost:5004/hubs/rides
app.MapHealthChecks("/health");

app.Run();
