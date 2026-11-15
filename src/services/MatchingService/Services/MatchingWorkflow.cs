using RideHailing.Shared.Contracts;
using RideHailing.Shared.Messaging;

namespace MatchingService.Services;

/// <summary>
/// Decides which driver gets each ride. Stateless: everything it needs arrives in the messages
/// (ride data + drivers already tried), so any instance can handle any message.
///
///   ride.requested        → attempt 1: find available drivers (gRPC), pick the NEAREST (positions from Kafka),
///                           create an offer (gRPC)
///   driver.rejected-ride  → next attempt with the next driver, or give up after MaxAttempts
///   no driver left        → publish ride.matching-failed (RideService marks the ride NoDriverAvailable)
///
/// Async in (RabbitMQ: the ride request may wait in the queue if Matching is down),
/// sync out (gRPC: Matching needs the answer now to decide the next step).
/// </summary>
public class MatchingWorkflow(
    DriverDirectory drivers,
    OfferClient offers,
    DriverLocationCache locations,
    IMessagePublisher publisher,
    IConfiguration configuration,
    ILogger<MatchingWorkflow> logger)
{
    public const string Source = "matching-service";

    private int MaxAttempts => configuration.GetValue("Matching:MaxAttempts", 3);
    private int CandidatesPerLookup => configuration.GetValue("Matching:CandidatesPerLookup", 5);

    public Task HandleRideRequestedAsync(MessageEnvelope message, CancellationToken ct)
    {
        var ride = message.PayloadAs<RideRequestedPayload>();
        return MatchAsync(message, ride, attempt: 1, exclude: [], ct);
    }

    public async Task HandleDriverRejectedAsync(MessageEnvelope message, CancellationToken ct)
    {
        var rejected = message.PayloadAs<DriverRejectedRidePayload>();
        logger.LogInformation("{Driver} declined ride {RideId} ({Reason})", rejected.DriverName, rejected.RideId, rejected.Reason);

        var nextAttempt = rejected.Attempt + 1;
        if (nextAttempt > MaxAttempts)
        {
            await FailAsync(message, rejected.RideId, $"No driver accepted after {rejected.Attempt} attempts.", rejected.Attempt, ct);
            return;
        }

        // The rejection carries the ride data, so we can rebuild the request without asking RideService.
        var ride = new RideRequestedPayload(rejected.RideId, rejected.PassengerId, rejected.PassengerName,
            rejected.Pickup, rejected.Destination, message.OccurredAt,
            rejected.PickupLat, rejected.PickupLng, rejected.DestinationLat, rejected.DestinationLng);
        await MatchAsync(message, ride, nextAttempt, rejected.ExcludedDriverIds, ct);
    }

    private async Task MatchAsync(MessageEnvelope cause, RideRequestedPayload ride, int attempt, IReadOnlyList<Guid> exclude, CancellationToken ct)
    {
        var available = await drivers.FindAvailableAsync(exclude, CandidatesPerLookup, ct); // longest-waiting first
        var candidates = NearestFirst(available, ride);
        logger.LogInformation("Ride {RideId}, attempt {Attempt}: {Count} available driver(s)", ride.RideId, attempt, candidates.Count);

        // If the first one was taken in the meantime, try the next.
        foreach (var driver in candidates)
        {
            if (await offers.TryCreateOfferAsync(ride, driver, attempt, cause.CorrelationId, ct))
            {
                logger.LogInformation("Offered ride {RideId} to {Driver} (attempt {Attempt})", ride.RideId, driver.Name, attempt);
                return;
            }
        }

        await FailAsync(cause, ride.RideId, "No driver available right now.", attempt, ct);
    }

    /// <summary>
    /// Drivers with a known position (Kafka) sorted by distance to the pickup; drivers without one keep the
    /// longest-waiting order and come after them. Free-text pickups (no coordinates) keep the original order.
    /// </summary>
    private List<DriverCandidate> NearestFirst(IReadOnlyList<DriverCandidate> available, RideRequestedPayload ride)
    {
        var pickupName = ride.Pickup;
        GeoPoint pickup;
        if (ride.PickupLat is { } lat && ride.PickupLng is { } lng) pickup = new GeoPoint(lat, lng); // picked on the map
        else if (!Places.TryGet(pickupName, out pickup)) return available.ToList();                // no location known

        var withDistance = available
            .Select((d, index) => (Driver: d, Index: index,
                Meters: locations.TryGet(d.Id, out var at) ? Geo.DistanceMeters(at, pickup) : (double?)null))
            .ToList();

        foreach (var c in withDistance)
            logger.LogInformation("  candidate {Driver}: {Distance}", c.Driver.Name,
                c.Meters is { } m ? $"{m / 1000:F1} km from {pickupName}" : "position unknown");

        return withDistance
            .OrderBy(c => c.Meters is null)          // known positions first
            .ThenBy(c => c.Meters ?? 0)              // nearest first
            .ThenBy(c => c.Index)                    // tie / unknown: longest waiting first
            .Select(c => c.Driver)
            .ToList();
    }

    /// <summary>
    /// No database → no outbox: publish directly. If this throws, the consumer does NOT ack the incoming message,
    /// so it is retried and the failure event is published again (at-least-once).
    /// </summary>
    private async Task FailAsync(MessageEnvelope cause, Guid rideId, string reason, int attempts, CancellationToken ct)
    {
        logger.LogWarning("Matching failed for ride {RideId}: {Reason}", rideId, reason);
        await publisher.PublishAsync(MessageEnvelope.Create(EventTypes.RideMatchingFailed, cause.CorrelationId, Source,
            new RideMatchingFailedPayload(rideId, reason, attempts), causationId: cause.EventId), ct);
    }
}
