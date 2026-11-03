using System.ComponentModel.DataAnnotations;

namespace RideService.Models;

/// <summary>
/// Request a ride. The last four fields are optional and only meant for Experiment A:
/// MatchingMode overrides the configured path; experiment rides use the isolated experiment resources.
/// </summary>
public sealed record CreateRideRequest(
    [Required] Guid PassengerId,
    [Required, StringLength(80, MinimumLength = 1)] string PassengerName,
    [Required, StringLength(200, MinimumLength = 2)] string Pickup,
    [Required, StringLength(200, MinimumLength = 2)] string Destination,
    MatchingMode? MatchingMode = null,
    Guid? ExperimentRunId = null,
    [Range(0, 60000)] int SimulatedMatchingDelayMs = 0,
    [Range(100, 60000)] int? MatchingTimeoutMs = null);

public sealed record DriverActionRequest([Required] Guid DriverId);

public sealed record CancelRideRequest(Guid? PassengerId, string? Reason);

public sealed record RideDto(
    Guid Id,
    Guid CorrelationId,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    RideStatus Status,
    Guid? DriverId,
    string? DriverName,
    string? Vehicle,
    string? LicensePlate,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? StatusReason,
    MatchingMode MatchingMode,
    Guid? ExperimentRunId,
    DateTimeOffset? OfferCreatedAt,
    string? OfferedDriverName)
{
    public static RideDto From(Ride r) => new(r.Id, r.CorrelationId, r.PassengerId, r.PassengerName, r.Pickup, r.Destination,
        r.Status, r.DriverId, r.DriverName, r.Vehicle, r.LicensePlate, r.RequestedAt, r.AssignedAt, r.StartedAt,
        r.CompletedAt, r.CancelledAt, r.StatusReason, r.MatchingMode, r.ExperimentRunId, r.OfferCreatedAt, r.OfferedDriverName);
}
