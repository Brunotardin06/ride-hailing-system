using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;
using RideService.Data;
using RideService.Models;

namespace RideService.Services;

/// <summary>
/// Business logic of RideService. Entry points that call it:
///   - REST     (RidesController)                → request, list, get, start, complete, cancel
///   - gRPC     (RidesGrpcService)               → assign driver when DriverService reports an accepted offer
///   - RabbitMQ (RideMessageHandler)             → matching failed → NoDriverAvailable
///
/// Every change writes its event to the OUTBOX in the same SaveChanges as the ride itself:
/// either both are stored, or neither (no "ride saved but event lost").
/// </summary>
public class RideWorkflow(RideDbContext db, ILogger<RideWorkflow> logger)
{
    public const string Source = "ride-service";

    public async Task<Ride> RequestAsync(RequestRideRequest request, CancellationToken ct)
    {
        // Rule: one active ride per passenger.
        var hasActive = await db.Rides.AnyAsync(r => r.PassengerId == request.PassengerId
                                                     && Ride.ActiveStatuses.Contains(r.Status), ct);
        if (hasActive) throw new ConflictException("You already have an active ride.");

        var ride = Ride.Request(request.PassengerId, request.PassengerName, request.Pickup, request.Destination,
            Locate(request.PickupLat, request.PickupLng, request.Pickup),
            Locate(request.DestinationLat, request.DestinationLng, request.Destination));
        db.Rides.Add(ride);

        db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideRequested, ride.CorrelationId, Source,
            new RideRequestedPayload(ride.Id, ride.PassengerId, ride.PassengerName, ride.Pickup, ride.Destination, ride.RequestedAt,
                ride.PickupLat, ride.PickupLng, ride.DestinationLat, ride.DestinationLng)));

        await db.SaveChangesAsync(ct); // ride row + outbox row in ONE transaction
        return ride;
    }

    /// <summary>Point from the map if given (both lat and lng), otherwise the coordinates of a known place name, otherwise none.</summary>
    private static GeoPoint? Locate(double? lat, double? lng, string name)
    {
        if (lat is not null && lng is not null) return new GeoPoint(lat.Value, lng.Value);
        if (lat is not null || lng is not null) throw new ArgumentException($"Give both latitude and longitude for '{name}'.");
        return Places.TryGet(name, out var known) ? known : null;
    }

    public async Task<Ride> GetAsync(Guid rideId, CancellationToken ct) =>
        await db.Rides.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rideId, ct)
        ?? throw new NotFoundException("Ride not found.");

    /// <summary>Newest first; filter by passenger (history) or driver.</summary>
    public async Task<List<Ride>> ListAsync(Guid? passengerId, Guid? driverId, int limit, CancellationToken ct)
    {
        var query = db.Rides.AsNoTracking();
        if (passengerId is not null) query = query.Where(r => r.PassengerId == passengerId);
        if (driverId is not null) query = query.Where(r => r.DriverId == driverId);
        return await query.OrderByDescending(r => r.RequestedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
    }

    /// <summary>The current (Requested / Assigned / InProgress) ride of a passenger or driver, or null.</summary>
    public async Task<Ride?> GetActiveAsync(Guid? passengerId, Guid? driverId, CancellationToken ct)
    {
        if (passengerId is null && driverId is null)
            throw new ArgumentException("Give passengerId or driverId.");

        var query = db.Rides.AsNoTracking().Where(r => Ride.ActiveStatuses.Contains(r.Status));
        if (passengerId is not null) query = query.Where(r => r.PassengerId == passengerId);
        if (driverId is not null) query = query.Where(r => r.DriverId == driverId);
        return await query.OrderByDescending(r => r.RequestedAt).FirstOrDefaultAsync(ct);
    }

    /// <summary>Called via gRPC by DriverService when a driver accepts. Throws 409-style ConflictException if the ride is no longer Requested.</summary>
    public Task<Ride> AssignDriverAsync(Guid rideId, Guid driverId, string driverName, string vehicle, string licensePlate, CancellationToken ct) =>
        ChangeAsync(rideId, EventTypes.RideAssigned, ride => ride.AssignDriver(driverId, driverName, vehicle, licensePlate), ct);

    public Task<Ride> StartAsync(Guid rideId, Guid driverId, CancellationToken ct) =>
        ChangeAsync(rideId, EventTypes.RideStarted, ride => ride.Start(driverId), ct);

    public Task<Ride> CompleteAsync(Guid rideId, Guid driverId, CancellationToken ct) =>
        ChangeAsync(rideId, EventTypes.RideCompleted, ride => ride.Complete(driverId), ct);

    public Task<Ride> CancelAsync(Guid rideId, Guid passengerId, string? reason, CancellationToken ct) =>
        ChangeAsync(rideId, EventTypes.RideCancelled,
            ride => ride.Cancel(passengerId, string.IsNullOrWhiteSpace(reason) ? "Cancelled by passenger" : reason), ct);

    /// <summary>
    /// ride.matching-failed from MatchingService. Idempotent: the inbox skips a duplicate message, and a ride
    /// that is no longer Requested (e.g. cancelled meanwhile) is left as it is.
    /// </summary>
    public async Task HandleMatchingFailedAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return; // duplicate delivery

        var payload = message.PayloadAs<RideMatchingFailedPayload>();
        var ride = await db.Rides.FindAsync([payload.RideId], ct);
        if (ride is { Status: RideStatus.Requested })
        {
            ride.MarkNoDriverAvailable(payload.Reason);
            AddStatusEvent(ride, EventTypes.RideNoDriverAvailable, causationId: message.EventId);
        }
        else
        {
            logger.LogInformation("Ignoring matching-failed for ride {RideId} (status {Status})", payload.RideId, ride?.Status);
        }

        db.MarkProcessed(message);     // inbox row …
        await db.SaveChangesAsync(ct); // … saved together with the effect
    }

    /// <summary>Load → apply one state change (the entity checks the rules) → add the event → save.</summary>
    private async Task<Ride> ChangeAsync(Guid rideId, string eventType, Action<Ride> change, CancellationToken ct)
    {
        var ride = await db.Rides.FindAsync([rideId], ct) ?? throw new NotFoundException("Ride not found.");
        change(ride);
        AddStatusEvent(ride, eventType);
        await db.SaveChangesAsync(ct);
        return ride;
    }

    private void AddStatusEvent(Ride ride, string eventType, Guid? causationId = null) =>
        db.AddToOutbox(MessageEnvelope.Create(eventType, ride.CorrelationId, Source,
            new RideStatusChangedPayload(ride.Id, ride.PassengerId, ride.DriverId, ride.DriverName,
                ride.Status.ToString(), ride.StatusReason, DateTimeOffset.UtcNow),
            causationId));
}
