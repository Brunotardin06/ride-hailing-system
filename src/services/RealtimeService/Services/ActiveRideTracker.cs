using System.Collections.Concurrent;

namespace RealtimeService.Services;

/// <summary>
/// "Which passenger is following which driver right now?" Built from the ride events Realtime already receives:
/// ride.assigned → start forwarding this driver's positions to the passenger; ride.completed / cancelled → stop.
/// In memory: after a Realtime restart, forwarding resumes with the next ride.
/// </summary>
public sealed class ActiveRideTracker
{
    public sealed record ActiveRide(Guid RideId, Guid PassengerId);

    private readonly ConcurrentDictionary<Guid, ActiveRide> _byDriver = new();

    public void Start(Guid driverId, Guid rideId, Guid passengerId) => _byDriver[driverId] = new ActiveRide(rideId, passengerId);

    public void Stop(Guid driverId, Guid rideId)
    {
        if (_byDriver.TryGetValue(driverId, out var ride) && ride.RideId == rideId) _byDriver.TryRemove(driverId, out _);
    }

    public bool TryGet(Guid driverId, out ActiveRide ride) => _byDriver.TryGetValue(driverId, out ride!);
}
