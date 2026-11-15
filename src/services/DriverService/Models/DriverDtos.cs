namespace DriverService.Models;

// DTOs = the JSON shapes of the REST API. They are separate from the entities (Driver, RideOffer), so:
//  - the database model can change without breaking the frontend;
//  - internal fields (Version) are never exposed.
// Field names match frontend/src/api/types.ts (Driver, Offer).

public sealed record DriverDto(
    Guid Id, string Name, string Vehicle, string LicensePlate, double Rating,
    DriverStatus Status, DateTimeOffset? AvailableSince, Guid? CurrentRideId, int CompletedRides)
{
    public static DriverDto From(Driver d) =>
        new(d.Id, d.Name, d.Vehicle, d.LicensePlate, d.Rating, d.Status, d.AvailableSince, d.CurrentRideId, d.CompletedRides);
}

public sealed record OfferDto(
    Guid Id, Guid RideId, Guid DriverId, Guid PassengerId, string PassengerName, string Pickup, string Destination,
    int Attempt, OfferStatus Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RespondedAt,
    double? PickupLat, double? PickupLng, double? DestinationLat, double? DestinationLng)
{
    public static OfferDto From(RideOffer o) => new(o.Id, o.RideId, o.DriverId, o.PassengerId, o.PassengerName,
        o.Pickup, o.Destination, o.Attempt, o.Status, o.CreatedAt, o.ExpiresAt, o.RespondedAt,
        o.PickupLat, o.PickupLng, o.DestinationLat, o.DestinationLng);
}

/// <summary>Body of PUT /api/drivers/{id}/availability.</summary>
public sealed record SetAvailabilityRequest(bool IsAvailable);

/// <summary>Body of POST /api/offers/{id}/accept and /reject: who is answering.</summary>
public sealed record OfferResponseRequest(Guid DriverId);

/// <summary>Data needed to create an offer (mapped from the gRPC CreateOffer request).</summary>
public sealed record CreateOfferCommand(
    string RequestKey, Guid RideId, Guid CorrelationId, Guid DriverId, Guid PassengerId, string PassengerName,
    string Pickup, string Destination, int Attempt,
    double? PickupLat = null, double? PickupLng = null, double? DestinationLat = null, double? DestinationLng = null);

/// <summary>An available driver, as returned to MatchingService.</summary>
public sealed record AvailableDriverDto(Guid Id, string Name, string Vehicle, string LicensePlate, double Rating);
