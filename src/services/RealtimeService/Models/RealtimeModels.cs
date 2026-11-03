using System.Text.Json;

namespace RealtimeService.Models;

/// <summary>Push notification sent to a passenger or driver.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Type,
    Guid RideId,
    string Title,
    string Message,
    JsonElement? Data,
    DateTimeOffset SentAt);

public sealed record ChatMessageDto(
    Guid Id,
    Guid RideId,
    string SenderRole,
    string SenderName,
    string Text,
    DateTimeOffset SentAt);

public sealed record SendChatMessageRequest(Guid RideId, string SenderRole, string SenderName, string Text);

/// <summary>SignalR group names. A group is a set of connections that receive the same messages.</summary>
public static class HubGroups
{
    public static string Passenger(Guid passengerId) => $"passenger:{passengerId}";
    public static string Driver(Guid driverId) => $"driver:{driverId}";
    public static string RideChat(Guid rideId) => $"ride:{rideId}";
}
