using System.Collections.Concurrent;
using RealtimeService.Models;

namespace RealtimeService.Services;

// In-memory state (no database): lost on restart, which is acceptable for live connections and a demo chat.
// Registered as singletons, shared by all hub calls and message handlers.

/// <summary>
/// Which connection is in which group. SignalR itself does not expose group membership,
/// but we need it to check "did you join this chat?" and to log when nobody is listening.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groupsByConnection = new();

    public void Add(string connectionId, string group) =>
        _groupsByConnection.GetOrAdd(connectionId, _ => new()).TryAdd(group, 0);

    public void Remove(string connectionId, string group)
    {
        if (_groupsByConnection.TryGetValue(connectionId, out var groups)) groups.TryRemove(group, out _);
    }

    public void RemoveConnection(string connectionId) => _groupsByConnection.TryRemove(connectionId, out _);

    public bool IsInGroup(string connectionId, string group) =>
        _groupsByConnection.TryGetValue(connectionId, out var groups) && groups.ContainsKey(group);

    public int CountInGroup(string group) => _groupsByConnection.Values.Count(g => g.ContainsKey(group));
}

/// <summary>Recent chat messages per ride, so a reloaded page can show the conversation again.</summary>
public sealed class ChatHistory
{
    private const int MaxMessagesPerRide = 100;
    private readonly ConcurrentDictionary<Guid, List<ChatMessageDto>> _messages = new();

    public void Add(ChatMessageDto message)
    {
        var list = _messages.GetOrAdd(message.RideId, _ => []);
        lock (list)
        {
            list.Add(message);
            if (list.Count > MaxMessagesPerRide) list.RemoveAt(0);
        }
    }

    public IReadOnlyList<ChatMessageDto> Get(Guid rideId)
    {
        if (!_messages.TryGetValue(rideId, out var list)) return [];
        lock (list) return list.ToList();
    }
}
