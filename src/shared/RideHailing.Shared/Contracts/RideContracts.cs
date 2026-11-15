namespace RideHailing.Shared.Contracts;

// Payloads of the RabbitMQ events (the "contract" between publisher and consumers).
// Rule: each payload carries ALL the data its consumers need, so they never have to call back the publisher.
// Changing a field here changes the contract for every consumer, so add fields, don't rename/remove them.

/// <summary>ride.requested — RideService → MatchingService.</summary>
public sealed record RideRequestedPayload(
    Guid RideId,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    DateTimeOffset RequestedAt,
    // Coordinates chosen on the map (added in v2; optional so older messages still deserialize).
    double? PickupLat = null,
    double? PickupLng = null,
    double? DestinationLat = null,
    double? DestinationLng = null);

/// <summary>ride.offer-created — DriverService → RealtimeService (show the offer on the driver's screen).</summary>
public sealed record RideOfferCreatedPayload(
    Guid OfferId,
    Guid RideId,
    Guid DriverId,
    string DriverName,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    int Attempt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// driver.rejected-ride — DriverService → MatchingService (+ RealtimeService). Sent when a driver rejected an
/// offer or let it expire. Carries the ride data and the drivers already tried, so MatchingService can try the
/// next driver without keeping state and without calling anyone back.
/// </summary>
public sealed record DriverRejectedRidePayload(
    Guid OfferId,
    Guid RideId,
    Guid DriverId,
    string DriverName,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    int Attempt,
    string Reason,
    IReadOnlyList<Guid> ExcludedDriverIds,
    double? PickupLat = null,
    double? PickupLng = null,
    double? DestinationLat = null,
    double? DestinationLng = null);

/// <summary>ride.offer-withdrawn — DriverService → RealtimeService (the ride was cancelled while the offer was pending).</summary>
public sealed record RideOfferWithdrawnPayload(Guid OfferId, Guid RideId, Guid DriverId, string Reason);

/// <summary>ride.matching-failed — MatchingService → RideService (no available driver accepted).</summary>
public sealed record RideMatchingFailedPayload(Guid RideId, string Reason, int Attempts);

/// <summary>
/// ride.assigned / started / completed / cancelled / no-driver-available — RideService → DriverService, RealtimeService.
/// One payload for every status change.
/// </summary>
public sealed record RideStatusChangedPayload(
    Guid RideId,
    Guid PassengerId,
    Guid? DriverId,
    string? DriverName,
    string Status,
    string? Reason,
    DateTimeOffset ChangedAt);
