using System.Text.Json;

namespace RealtimeService.Models;

// Shapes match frontend/src/api/types.ts (Notification, ChatMessage).

/// <summary>Push notification for a passenger or driver. The frontend shows it as a toast and reloads its data via REST.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Type,        // e.g. "ride-offer", "ride-assigned", "no-driver"
    Guid RideId,
    string Title,
    string Message,
    JsonElement? Data,  // the original event payload
    DateTimeOffset SentAt);

public sealed record ChatMessageDto(Guid Id, Guid RideId, string SenderRole, string SenderName, string Text, DateTimeOffset SentAt);

public sealed record SendChatMessageRequest(Guid RideId, string SenderRole, string SenderName, string Text);

/// <summary>Position of the passenger's driver, pushed every ~2 s while the ride is Assigned / InProgress.</summary>
public sealed record DriverLocationDto(Guid RideId, Guid DriverId, double Lat, double Lng, DateTimeOffset RecordedAt);

/// <summary>
/// SignalR group names. A group = a set of connections that receive the same messages.
/// One person can have several tabs open: all their connections join the same personal group.
/// </summary>
public static class HubGroups
{
    public static string Passenger(Guid passengerId) => $"passenger:{passengerId}";
    public static string Driver(Guid driverId) => $"driver:{driverId}";
    public static string RideChat(Guid rideId) => $"ride:{rideId}";
}
