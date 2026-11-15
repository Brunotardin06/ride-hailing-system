using Microsoft.AspNetCore.SignalR;
using RealtimeService.Hubs;
using RealtimeService.Models;
using RealtimeService.Services;
using RideHailing.Shared.Kafka;

namespace RealtimeService.Messaging;

/// <summary>
/// Kafka "driver-locations" (consumer group "realtime-service") → SignalR.
/// If the driver is on a ride, the position is pushed to that ride's passenger; otherwise it is ignored here
/// (MatchingService still uses it, through its own consumer group).
/// </summary>
public sealed class RealtimeLocationHandler(ActiveRideTracker rides, IHubContext<RideHub, IRideClient> hub) : IDriverLocationHandler
{
    public Task HandleAsync(DriverLocation location, CancellationToken ct)
    {
        if (!rides.TryGet(location.DriverId, out var ride)) return Task.CompletedTask;

        return hub.Clients.Group(HubGroups.Passenger(ride.PassengerId)).DriverLocation(
            new DriverLocationDto(ride.RideId, location.DriverId, location.Lat, location.Lng, location.RecordedAt));
    }
}
