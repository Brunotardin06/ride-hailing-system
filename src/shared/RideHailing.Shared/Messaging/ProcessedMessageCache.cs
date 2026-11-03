namespace RideHailing.Shared.Messaging;

/// <summary>
/// In-memory duplicate detection for the stateless services (MatchingService, RealtimeService).
/// Remembers the most recent event ids; services with a database store processed ids in a table instead.
/// Limitation: the memory is lost on restart.
/// </summary>
public sealed class ProcessedMessageCache(int capacity = 10_000)
{
    private readonly HashSet<Guid> _ids = [];
    private readonly Queue<Guid> _order = new();
    private readonly Lock _lock = new();

    /// <summary>Returns false if the id was already seen (= duplicate).</summary>
    public bool TryMarkProcessed(Guid eventId)
    {
        lock (_lock)
        {
            if (!_ids.Add(eventId)) return false;
            _order.Enqueue(eventId);
            if (_order.Count > capacity) _ids.Remove(_order.Dequeue());
            return true;
        }
    }

    /// <summary>Forget an id again, e.g. when handling failed and the message will be retried.</summary>
    public void Remove(Guid eventId)
    {
        lock (_lock) _ids.Remove(eventId);
    }
}
