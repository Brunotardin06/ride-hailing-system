using System.ComponentModel.DataAnnotations;

namespace RideService.Models;

// JSON shapes of the REST API. Field names match frontend/src/api/types.ts (Ride).
// [Required] / [StringLength]: [ApiController] validates these automatically and returns 400 if they fail.

/// <summary>Body of POST /api/rides.</summary>
public sealed record RequestRideRequest(
    [Required] Guid PassengerId,
    [Required, StringLength(80, MinimumLength = 1)] string PassengerName,
    [Required, StringLength(200, MinimumLength = 2)] string Pickup,
    [Required, StringLength(200, MinimumLength = 2)] string Destination,
    // Optional: point picked on the map. Without it, a known place name is resolved to coordinates.
    [Range(-90, 90)] double? PickupLat = null,
    [Range(-180, 180)] double? PickupLng = null,
    [Range(-90, 90)] double? DestinationLat = null,
    [Range(-180, 180)] double? DestinationLng = null);

/// <summary>Body of POST /api/rides/{id}/start and /complete: which driver is acting.</summary>
public sealed record DriverActionRequest([Required] Guid DriverId);

/// <summary>Body of POST /api/rides/{id}/cancel.</summary>
public sealed record CancelRideRequest([Required] Guid PassengerId, string? Reason);

public sealed record RideDto(
    Guid Id,
    Guid CorrelationId,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    double? PickupLat,
    double? PickupLng,
    double? DestinationLat,
    double? DestinationLng,
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
    string? StatusReason)
{
    public static RideDto From(Ride r) => new(r.Id, r.CorrelationId, r.PassengerId, r.PassengerName, r.Pickup, r.Destination,
        r.PickupLat, r.PickupLng, r.DestinationLat, r.DestinationLng, r.Status, r.DriverId, r.DriverName, r.Vehicle, r.LicensePlate, r.RequestedAt, r.AssignedAt, r.StartedAt,
        r.CompletedAt, r.CancelledAt, r.StatusReason);
}
