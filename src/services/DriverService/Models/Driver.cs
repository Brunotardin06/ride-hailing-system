using RideHailing.Shared.Hosting;

namespace DriverService.Models;

public enum DriverStatus
{
    Offline,    // not accepting rides
    Available,  // can receive an offer
    Offered,    // reserved: an offer is waiting for the driver's answer
    Busy,       // driving an accepted ride
}

/// <summary>
/// A driver and their availability (state machine):
///
///   Offline ⇄ Available ──offer──► Offered ──accept──► Busy ──ride completed/cancelled──► Available
///                 ▲                  │
///                 └──reject/expire───┘
///
/// The setters are private: the state can only change through the methods below, which check the rules.
/// </summary>
public class Driver
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string LicensePlate { get; set; } = "";
    public double Rating { get; set; }
    public DriverStatus Status { get; private set; }

    /// <summary>Since when the driver is available; the longest-waiting driver gets the next offer.</summary>
    public DateTimeOffset? AvailableSince { get; private set; }

    /// <summary>Ride the driver is reserved for (Offered) or driving (Busy).</summary>
    public Guid? CurrentRideId { get; private set; }

    public int CompletedRides { get; private set; }

    /// <summary>
    /// Optimistic concurrency token (PostgreSQL xmin). If two requests change the same driver at the same time,
    /// the second SaveChanges fails (→ 409) instead of silently overwriting the first.
    /// </summary>
    public uint Version { get; private set; }

    public static Driver Seed(Guid id, string name, string vehicle, string plate, double rating,
        DriverStatus status, DateTimeOffset? availableSince) => new()
    {
        Id = id, Name = name, Vehicle = vehicle, LicensePlate = plate, Rating = rating,
        Status = status, AvailableSince = availableSince,
    };

    public void SetAvailability(bool available)
    {
        if (Status is DriverStatus.Offered or DriverStatus.Busy)
            throw new ConflictException($"Cannot change availability while {Status}.");

        if (available && Status == DriverStatus.Offline)
        {
            Status = DriverStatus.Available;
            AvailableSince = DateTimeOffset.UtcNow;
        }
        else if (!available)
        {
            Status = DriverStatus.Offline;
            AvailableSince = null;
        }
    }

    public void ReserveFor(Guid rideId)
    {
        if (Status != DriverStatus.Available)
            throw new ConflictException($"Driver {Name} is not available ({Status}).");
        Status = DriverStatus.Offered;
        CurrentRideId = rideId;
    }

    public void ConfirmRide(Guid rideId)
    {
        if (Status != DriverStatus.Offered || CurrentRideId != rideId)
            throw new ConflictException($"Driver {Name} has no pending offer for this ride.");
        Status = DriverStatus.Busy;
    }

    /// <summary>Back to Available after a rejected/expired offer or a cancelled ride.</summary>
    public void Release(Guid rideId)
    {
        if (CurrentRideId != rideId) return;
        Status = DriverStatus.Available;
        CurrentRideId = null;
        AvailableSince = DateTimeOffset.UtcNow;
    }

    public void FinishRide(Guid rideId)
    {
        if (Status != DriverStatus.Busy || CurrentRideId != rideId) return;
        CompletedRides++;
        Release(rideId);
    }

    public void ResetForDemo(DriverStatus status, DateTimeOffset? availableSince)
    {
        Status = status;
        AvailableSince = availableSince;
        CurrentRideId = null;
    }
}
