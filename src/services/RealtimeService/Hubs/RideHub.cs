using Microsoft.AspNetCore.SignalR;
using RealtimeService.Models;
using RealtimeService.Services;

namespace RealtimeService.Hubs;

/// <summary>Methods the server can call on a connected browser.</summary>
public interface IRideClient
{
    Task Notification(NotificationDto notification);
    Task ChatMessage(ChatMessageDto message);
}

/// <summary>
/// SignalR hub at /hubs/rides. The browser keeps one WebSocket connection open and:
///   - Register(role, userId)      → joins its personal group to receive notifications
///   - JoinRideChat(rideId)        → joins the chat group of a ride, returns the history
///   - SendChatMessage(...)        → message is pushed to the other participant(s)
///   - Acknowledge(messageId)      → confirms a notification/chat message was received (for delivery metrics)
/// Business actions (request, accept, start...) are NOT done here; they go via REST to the owning service.
/// </summary>
public class RideHub(ConnectionRegistry registry, ChatHistory chatHistory, DeliveryTracker deliveryTracker, ILogger<RideHub> logger)
    : Hub<IRideClient>
{
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

    public async Task Unregister(string role, Guid userId)
    {
        var group = role == "driver" ? HubGroups.Driver(userId) : HubGroups.Passenger(userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        registry.Remove(Context.ConnectionId, group);
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

        // Only measure delivery if someone else is listening (the sender is in the group too).
        if (registry.CountInGroup(group) > 1) deliveryTracker.Track(message.Id, "chat-message", request.RideId);
        await Clients.OthersInGroup(group).ChatMessage(message);
        return message; // the sender gets its own message back as the return value
    }

    /// <summary>Client confirms it received and displayed a notification or chat message.</summary>
    public void Acknowledge(Guid messageId) => deliveryTracker.Acknowledge(messageId);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
