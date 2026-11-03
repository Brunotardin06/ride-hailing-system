namespace RideHailing.Shared.Contracts;

// Payloads of the messages exchanged between services (the "contract").
// Each payload carries enough data for its consumers so they do not need to call back.

/// <summary>
/// Published by RideService when a passenger requests a ride (RabbitMQ matching path).
/// ExperimentRunId / SimulatedMatchingDelayMs are only set for Experiment A rides.
/// </summary>
public sealed record RideRequestedPayload(
    Guid RideId,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    DateTimeOffset RequestedAt,
    Guid? ExperimentRunId = null,
    int SimulatedMatchingDelayMs = 0);

/// <summary>
/// Synchronous matching request: RideService → MatchingService (REST matching path, Experiment A).
/// Carries the same data as <see cref="RideRequestedPayload"/>.
/// </summary>
public sealed record MatchingRequest(
    Guid RideId,
    Guid CorrelationId,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    Guid? ExperimentRunId = null,
    int SimulatedMatchingDelayMs = 0);

public enum MatchingOutcome { OfferCreated, NoDriverAvailable, RideClosed }

/// <summary>Result of a synchronous matching request.</summary>
public sealed record MatchingResponse(
    MatchingOutcome Outcome,
    Guid? OfferId,
    Guid? DriverId,
    string? DriverName,
    DateTimeOffset? OfferCreatedAt,
    string? Reason);

/// <summary>Published by DriverService after MatchingService created an offer for a driver.</summary>
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

/// <summary>Published by DriverService when a driver accepted an offer.</summary>
public sealed record DriverAcceptedRidePayload(
    Guid OfferId,
    Guid RideId,
    Guid DriverId,
    string DriverName,
    string Vehicle,
    string LicensePlate,
    Guid PassengerId);

/// <summary>
/// Published by DriverService when a driver rejected an offer or let it expire.
/// Contains the ride details and the drivers already tried, so MatchingService can re-match statelessly.
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
    IReadOnlyList<Guid> ExcludedDriverIds);

/// <summary>Published by DriverService when a pending offer is withdrawn because the ride was cancelled.</summary>
public sealed record RideOfferWithdrawnPayload(Guid OfferId, Guid RideId, Guid DriverId, string Reason);

/// <summary>Published by MatchingService when no driver could be found.</summary>
public sealed record RideMatchingFailedPayload(
    Guid RideId,
    Guid PassengerId,
    string Reason,
    int Attempts);

/// <summary>
/// Published by RideService on every status change after the request
/// (assigned, started, completed, cancelled, no driver available).
/// </summary>
public sealed record RideStatusChangedPayload(
    Guid RideId,
    Guid PassengerId,
    Guid? DriverId,
    string? DriverName,
    string Status,
    string? PreviousStatus,
    string? Reason,
    DateTimeOffset ChangedAt);
