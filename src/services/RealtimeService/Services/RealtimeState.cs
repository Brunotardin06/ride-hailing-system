using System.Collections.Concurrent;
using System.Diagnostics;
using RealtimeService.Models;
using RideHailing.Shared.Metrics;

namespace RealtimeService.Services;

/// <summary>
/// Which connection is in which group. SignalR does not expose group membership, but we need it to know
/// whether anybody is listening (a notification to an empty group cannot be acknowledged).
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

    public int ConnectionCount => _groupsByConnection.Count;
}

/// <summary>
/// Measures SignalR delivery: the time from the server sending a message until the client's
/// acknowledgement arrives back at the server (round trip, both timestamps on the server clock).
/// Messages without an acknowledgement within the timeout are recorded as failed deliveries.
/// </summary>
public sealed class DeliveryTracker(IMetricsReporter metrics, ILogger<DeliveryTracker> logger) : BackgroundService
{
    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(15);

    private sealed record Pending(string Operation, long StartTimestamp, Guid? CorrelationId);

    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    /// <summary>Call immediately before sending.</summary>
    public void Track(Guid messageId, string operation, Guid? correlationId) =>
        _pending[messageId] = new Pending(operation, Stopwatch.GetTimestamp(), correlationId);

    public bool Acknowledge(Guid messageId)
    {
        if (!_pending.TryRemove(messageId, out var pending)) return false; // unknown, late or second ack
        metrics.Record(Mechanisms.SignalR, MetricKinds.AckRoundTrip, pending.Operation,
            Stopwatch.GetElapsedTime(pending.StartTimestamp).TotalMilliseconds, success: true, pending.CorrelationId);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var (id, pending) in _pending)
            {
                var elapsed = Stopwatch.GetElapsedTime(pending.StartTimestamp);
                if (elapsed < AckTimeout || !_pending.TryRemove(id, out _)) continue;
                logger.LogWarning("No acknowledgement for {Operation} {MessageId}", pending.Operation, id);
                metrics.Record(Mechanisms.SignalR, MetricKinds.AckRoundTrip, pending.Operation, elapsed.TotalMilliseconds,
                    success: false, pending.CorrelationId, $"No acknowledgement within {AckTimeout.TotalSeconds:F0} s");
            }
        }
    }
}

/// <summary>Recent chat messages per ride, in memory, so a reloaded page can show the conversation again.</summary>
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
