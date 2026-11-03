using System.ComponentModel.DataAnnotations;

namespace DriverService.Models;

public sealed record DriverDto(
    Guid Id, string Name, string Vehicle, string LicensePlate, double Rating,
    DriverStatus Status, DateTimeOffset? AvailableSince, Guid? CurrentRideId, int CompletedRides)
{
    public static DriverDto From(Driver d) =>
        new(d.Id, d.Name, d.Vehicle, d.LicensePlate, d.Rating, d.Status, d.AvailableSince, d.CurrentRideId, d.CompletedRides);
}

/// <summary>Response of the driver-availability lookup. Same fields as the gRPC AvailableDriver message.</summary>
public sealed record AvailableDriverDto(Guid Id, string Name, string Vehicle, string LicensePlate, double Rating, DateTimeOffset? AvailableSince);

public sealed record SetAvailabilityRequest(bool IsAvailable);

/// <summary>Sent by MatchingService to offer a ride to a specific driver.</summary>
public sealed record CreateOfferRequest(
    [Required, StringLength(200, MinimumLength = 1)] string RequestKey,
    Guid RideId,
    Guid DriverId,
    Guid PassengerId,
    string PassengerName,
    string Pickup,
    string Destination,
    int Attempt);

public sealed record OfferResponseRequest(Guid DriverId);

public sealed record OfferDto(
    Guid Id, Guid RideId, Guid DriverId, Guid PassengerId, string PassengerName, string Pickup, string Destination,
    int Attempt, OfferStatus Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RespondedAt)
{
    public static OfferDto From(RideOffer o) => new(o.Id, o.RideId, o.DriverId, o.PassengerId, o.PassengerName, o.Pickup,
        o.Destination, o.Attempt, o.Status, o.CreatedAt, o.ExpiresAt, o.RespondedAt);
}
