using System.Collections.Concurrent;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Kafka;

namespace MatchingService.Services;

/// <summary>
/// Last known position of every driver, filled from Kafka "driver-locations" (consumer group "matching-service").
/// In memory: after a restart, the consumer group continues from its committed offset and the cache refills
/// within seconds (positions arrive every ~2 s).
/// </summary>
public sealed class DriverLocationCache
{
    /// <summary>Older positions are ignored: the driver probably closed the app.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, DriverLocation> _latest = new();

    public void Update(DriverLocation location) =>
        // Keep the newest: ordering per driver is guaranteed by the partition key, this is just a safety net.
        _latest.AddOrUpdate(location.DriverId, location, (_, old) => location.RecordedAt >= old.RecordedAt ? location : old);

    public bool TryGet(Guid driverId, out GeoPoint point)
    {
        if (_latest.TryGetValue(driverId, out var l) && DateTimeOffset.UtcNow - l.RecordedAt < MaxAge)
        {
            point = new GeoPoint(l.Lat, l.Lng);
            return true;
        }
        point = default;
        return false;
    }
}

/// <summary>Kafka → cache.</summary>
public sealed class MatchingLocationHandler(DriverLocationCache cache) : IDriverLocationHandler
{
    public Task HandleAsync(DriverLocation location, CancellationToken ct)
    {
        cache.Update(location);
        return Task.CompletedTask;
    }
}
