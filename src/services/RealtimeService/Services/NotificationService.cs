using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RealtimeService.Hubs;
using RealtimeService.Models;

namespace RealtimeService.Services;

/// <summary>Sends notifications to the personal group of a passenger or driver and tracks their delivery.</summary>
public sealed class NotificationService(
    IHubContext<RideHub, IRideClient> hub,
    ConnectionRegistry registry,
    DeliveryTracker deliveryTracker,
    ILogger<NotificationService> logger)
{
    public Task ToPassengerAsync(Guid passengerId, string type, Guid rideId, string title, string message, JsonElement? data, Guid correlationId) =>
        SendAsync(HubGroups.Passenger(passengerId), type, rideId, title, message, data, correlationId);

    public Task ToDriverAsync(Guid driverId, string type, Guid rideId, string title, string message, JsonElement? data, Guid correlationId) =>
        SendAsync(HubGroups.Driver(driverId), type, rideId, title, message, data, correlationId);

    private async Task SendAsync(string group, string type, Guid rideId, string title, string message, JsonElement? data, Guid correlationId)
    {
        var listeners = registry.CountInGroup(group);
        var notification = new NotificationDto(Guid.NewGuid(), type, rideId, title, message, data, DateTimeOffset.UtcNow);

        if (listeners > 0) deliveryTracker.Track(notification.Id, $"notification:{type}", correlationId);
        else logger.LogInformation("No client connected for {Group}; {Type} is not delivered live", group, type);

        await hub.Clients.Group(group).Notification(notification);
    }
}
