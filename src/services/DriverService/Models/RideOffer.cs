using RideHailing.Shared.Hosting;

namespace DriverService.Models;

public enum OfferStatus { Pending, Accepted, Rejected, Expired, Cancelled }

/// <summary>
/// An offer of a ride to one driver. Created by MatchingService (gRPC, later phase), answered by the driver (REST).
/// Ride data (passenger, pickup, destination) is copied here, so the driver screen does not need to call RideService.
/// </summary>
public class RideOffer
{
    public Guid Id { get; set; }

    /// <summary>
    /// Idempotency key from MatchingService ("rideId:attempt"). Unique in the DB: if Matching retries the same
    /// message, CreateOffer returns the existing offer instead of creating a second one.
    /// </summary>
    public string RequestKey { get; set; } = "";

    public Guid RideId { get; set; }

    /// <summary>The ride's correlation id, copied into every event about this offer.</summary>
    public Guid CorrelationId { get; set; }

    public Guid DriverId { get; set; }
    public Guid PassengerId { get; set; }
    public string PassengerName { get; set; } = "";
    public string Pickup { get; set; } = "";
    public string Destination { get; set; } = "";

    // Trip coordinates, copied from the ride: needed to re-match the ride (nearest driver) after a rejection.
    public double? PickupLat { get; set; }
    public double? PickupLng { get; set; }
    public double? DestinationLat { get; set; }
    public double? DestinationLng { get; set; }

    /// <summary>1 = first driver tried, 2 = second driver after a rejection, …</summary>
    public int Attempt { get; set; }

    public OfferStatus Status { get; private set; } = OfferStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>An offer can be answered only once.</summary>
    public void Respond(OfferStatus outcome)
    {
        if (Status != OfferStatus.Pending)
            throw new ConflictException($"Offer is already {Status}.");
        Status = outcome;
        RespondedAt = DateTimeOffset.UtcNow;
    }
}
