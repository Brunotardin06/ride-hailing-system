using RideHailing.Shared.Hosting;

namespace DriverService.Models;

public enum OfferStatus { Pending, Accepted, Rejected, Expired, Cancelled }

/// <summary>An offer of a ride to one driver. Created by MatchingService, answered by the driver.</summary>
public class RideOffer
{
    public Guid Id { get; set; }

    /// <summary>Idempotency key from MatchingService: the same request never creates two offers.</summary>
    public string RequestKey { get; set; } = "";

    public Guid RideId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid DriverId { get; set; }
    public Guid PassengerId { get; set; }
    public string PassengerName { get; set; } = "";
    public string Pickup { get; set; } = "";
    public string Destination { get; set; } = "";
    public int Attempt { get; set; }

    /// <summary>Offer to an experiment-pool driver. Never expires (experiments clean up themselves).</summary>
    public bool IsExperiment { get; set; }

    public OfferStatus Status { get; private set; } = OfferStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    public uint Version { get; private set; }

    public void Respond(OfferStatus outcome)
    {
        if (Status != OfferStatus.Pending)
            throw new ConflictException($"Offer is already {Status}.");
        Status = outcome;
        RespondedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>A ride that was cancelled; no new offers may be created for it.</summary>
public class ClosedRide
{
    public Guid RideId { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
}
