// RealtimeService — pushes notifications and chat to browsers via SignalR (WebSocket).
// RabbitMQ in (ride workflow events), SignalR out/in. Stateless apart from in-memory chat history. See README.md.

using RealtimeService.Hubs;
using RealtimeService.Messaging;
using RealtimeService.Services;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddSignalR();

builder.Services.AddServiceIdentity("realtime-service");
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<ChatHistory>();
builder.Services.AddSingleton<DeliveryTracker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeliveryTracker>());
builder.Services.AddSingleton<NotificationService>();

builder.Services.AddSingleton<ProcessedMessageCache>();
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddRabbitMqConsumer<RealtimeMessageHandler>(RabbitMqTopology.Queues.RealtimeService);
builder.Services.AddMetricsReporter(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document: /openapi/v1.json
    app.MapOpenApi();
}

app.UseCors();

app.MapHub<RideHub>("/hubs/rides");
app.MapGet("/api/realtime/status", (ConnectionRegistry registry) => new { connections = registry.ConnectionCount });
app.MapHealthChecks("/health");

app.Run();
