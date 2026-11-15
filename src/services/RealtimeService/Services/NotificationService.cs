using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RealtimeService.Hubs;
using RealtimeService.Models;

namespace RealtimeService.Services;

/// <summary>
/// Pushes a notification to the personal group of a passenger or driver.
/// IHubContext = "send to clients from OUTSIDE a hub" (here: from the RabbitMQ handler).
/// </summary>
public sealed class NotificationService(IHubContext<RideHub, IRideClient> hub, ConnectionRegistry registry, ILogger<NotificationService> logger)
{
    public Task ToPassengerAsync(Guid passengerId, string type, Guid rideId, string title, string message, JsonElement? data) =>
        SendAsync(HubGroups.Passenger(passengerId), type, rideId, title, message, data);

    public Task ToDriverAsync(Guid driverId, string type, Guid rideId, string title, string message, JsonElement? data) =>
        SendAsync(HubGroups.Driver(driverId), type, rideId, title, message, data);

    private async Task SendAsync(string group, string type, Guid rideId, string title, string message, JsonElement? data)
    {
        // Nobody connected → the push is simply lost. That is fine: the state lives in Ride/Driver, and the
        // frontend loads it via REST when the page opens. Realtime is only the "something changed" signal.
        if (registry.CountInGroup(group) == 0)
            logger.LogInformation("No client connected for {Group}; {Type} not delivered live", group, type);

        await hub.Clients.Group(group).Notification(
            new NotificationDto(Guid.NewGuid(), type, rideId, title, message, data, DateTimeOffset.UtcNow));
    }
}
