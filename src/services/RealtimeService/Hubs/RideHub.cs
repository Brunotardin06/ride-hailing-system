using Microsoft.AspNetCore.SignalR;
using RealtimeService.Models;
using RealtimeService.Services;
using RideHailing.Shared.Kafka;

namespace RealtimeService.Hubs;

/// <summary>
/// Methods the SERVER calls on the browser (strongly typed instead of magic strings).
/// The JS client listens with connection.on('notification', ...) / on('chatMessage', ...).
/// </summary>
public interface IRideClient
{
    Task Notification(NotificationDto notification);
    Task ChatMessage(ChatMessageDto message);
    Task DriverLocation(DriverLocationDto location);
}

/// <summary>
/// SignalR hub at /hubs/rides: each browser tab keeps ONE WebSocket open. Methods the BROWSER calls:
///   Register(role, userId)  → join the personal group "passenger:&lt;id&gt;" / "driver:&lt;id&gt;"
///   JoinRideChat(rideId)    → join the chat group of a ride, get the history back
///   SendChatMessage(...)    → pushed to the other participant
///   Acknowledge(id)         → client confirms it received a message (no-op)
///   UpdateLocation(...)     → driver's simulated GPS → Kafka "driver-locations"
///
/// Business actions (request, accept, start, …) are NOT done here: they go via REST to the owning service.
/// A hub instance lives for ONE call only, so state is kept in singletons (ConnectionRegistry, ChatHistory).
/// </summary>
public class RideHub(ConnectionRegistry registry, ChatHistory chatHistory, DriverLocationProducer locations, ILogger<RideHub> logger)
    : Hub<IRideClient>
{
    /// <summary>
    /// The driver's tab (simulated GPS) sends its position every ~2 s. We only validate it and write it to Kafka;
    /// the consumers (Realtime itself, Matching) decide what to do with it.
    /// </summary>
    public async Task UpdateLocation(Guid driverId, double lat, double lng)
    {
        // Only the driver's own connection may report their position.
        if (!registry.IsInGroup(Context.ConnectionId, HubGroups.Driver(driverId)))
            throw new HubException("Register as this driver first.");
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
            throw new HubException("Invalid coordinates.");

        await locations.ProduceAsync(new DriverLocation(driverId, lat, lng, DateTimeOffset.UtcNow), Context.ConnectionAborted);
    }

    public async Task Register(string role, Guid userId)
    {
        var group = role switch
        {
            "passenger" => HubGroups.Passenger(userId),
            "driver" => HubGroups.Driver(userId),
            _ => throw new HubException("Role must be 'passenger' or 'driver'."),
        };
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        registry.Add(Context.ConnectionId, group);
        logger.LogInformation("Connection {ConnectionId} registered as {Group}", Context.ConnectionId, group);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> JoinRideChat(Guid rideId)
    {
        var group = HubGroups.RideChat(rideId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        registry.Add(Context.ConnectionId, group);
        return chatHistory.Get(rideId);
    }

    public async Task LeaveRideChat(Guid rideId)
    {
        var group = HubGroups.RideChat(rideId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        registry.Remove(Context.ConnectionId, group);
    }

    public async Task<ChatMessageDto> SendChatMessage(SendChatMessageRequest request)
    {
        var text = request.Text?.Trim() ?? "";
        if (text.Length is 0 or > 500) throw new HubException("A message must be 1–500 characters.");
        if (request.SenderRole is not ("passenger" or "driver")) throw new HubException("Invalid sender role.");

        var group = HubGroups.RideChat(request.RideId);
        if (!registry.IsInGroup(Context.ConnectionId, group)) throw new HubException("Join the ride chat first.");

        var message = new ChatMessageDto(Guid.NewGuid(), request.RideId, request.SenderRole, request.SenderName.Trim(), text, DateTimeOffset.UtcNow);
        chatHistory.Add(message);
        await Clients.OthersInGroup(group).ChatMessage(message); // everyone in the ride chat except the sender
        return message;                                         // the sender gets it back as the return value
    }

    /// <summary>The frontend acknowledges every message. Kept so the call succeeds; nothing is done with it.</summary>
    public void Acknowledge(Guid messageId) { }

    // SignalR removes the connection from its groups automatically; we clean our own registry.
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
