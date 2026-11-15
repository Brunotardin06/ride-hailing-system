using RideHailing.Shared.Contracts;
using RideHailing.Shared.Hosting;

namespace RideService.Models;

public enum RideStatus
{
    Requested,          // waiting for a driver (matching in progress)
    Assigned,           // a driver accepted and is on the way
    InProgress,         // passenger picked up
    Completed,
    Cancelled,
    NoDriverAvailable,
}

/// <summary>
/// A ride and its lifecycle. RideService is the source of truth for ride status.
/// Every status change goes through a method below, which checks the transition is allowed:
///
///   Requested ──► Assigned ──► InProgress ──► Completed
///      │             │
///      ├──► Cancelled ◄┘
///      └──► NoDriverAvailable
/// </summary>
public class Ride
{
    // State machine as data: "from this status, you may go to these statuses".
    private static readonly Dictionary<RideStatus, RideStatus[]> AllowedTransitions = new()
    {
        [RideStatus.Requested] = [RideStatus.Assigned, RideStatus.Cancelled, RideStatus.NoDriverAvailable],
        [RideStatus.Assigned] = [RideStatus.InProgress, RideStatus.Cancelled],
        [RideStatus.InProgress] = [RideStatus.Completed],
        [RideStatus.Completed] = [],
        [RideStatus.Cancelled] = [],
        [RideStatus.NoDriverAvailable] = [],
    };

    public static readonly RideStatus[] ActiveStatuses = [RideStatus.Requested, RideStatus.Assigned, RideStatus.InProgress];

    public Guid Id { get; private set; }

    /// <summary>Id that follows this ride through every message and log line (used from the RabbitMQ phase on).</summary>
    public Guid CorrelationId { get; private set; }

    public Guid PassengerId { get; private set; }
    public string PassengerName { get; private set; } = "";
    public string Pickup { get; private set; } = "";
    public string Destination { get; private set; } = "";

    // Coordinates picked on the map (or of a known place). Null for older rides / free text without a location.
    public double? PickupLat { get; private set; }
    public double? PickupLng { get; private set; }
    public double? DestinationLat { get; private set; }
    public double? DestinationLng { get; private set; }

    public RideStatus Status { get; private set; }

    // Driver data is COPIED here when the driver is assigned, so reading a ride never needs DriverService.
    public Guid? DriverId { get; private set; }
    public string? DriverName { get; private set; }
    public string? Vehicle { get; private set; }
    public string? LicensePlate { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? StatusReason { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    private Ride() { } // EF Core needs a parameterless constructor; our code uses Request(...)

    /// <summary>Factory: the only way to create a ride, so every new ride starts valid (status Requested).</summary>
    public static Ride Request(Guid passengerId, string passengerName, string pickup, string destination,
        GeoPoint? pickupAt = null, GeoPoint? destinationAt = null) => new()
    {
        Id = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        PassengerId = passengerId,
        PassengerName = passengerName,
        Pickup = pickup,
        Destination = destination,
        PickupLat = pickupAt?.Lat,
        PickupLng = pickupAt?.Lng,
        DestinationLat = destinationAt?.Lat,
        DestinationLng = destinationAt?.Lng,
        Status = RideStatus.Requested,
        RequestedAt = DateTimeOffset.UtcNow,
    };

    public bool IsActive => ActiveStatuses.Contains(Status);

    public void AssignDriver(Guid driverId, string driverName, string vehicle, string licensePlate)
    {
        TransitionTo(RideStatus.Assigned);
        DriverId = driverId;
        DriverName = driverName;
        Vehicle = vehicle;
        LicensePlate = licensePlate;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public void Start(Guid driverId)
    {
        EnsureAssignedDriver(driverId);
        TransitionTo(RideStatus.InProgress);
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(Guid driverId)
    {
        EnsureAssignedDriver(driverId);
        TransitionTo(RideStatus.Completed);
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(Guid passengerId, string reason)
    {
        if (passengerId != PassengerId)
            throw new ForbiddenException("Only the passenger of this ride can cancel it.");
        TransitionTo(RideStatus.Cancelled);
        CancelledAt = DateTimeOffset.UtcNow;
        StatusReason = reason;
    }

    public void MarkNoDriverAvailable(string reason)
    {
        TransitionTo(RideStatus.NoDriverAvailable);
        StatusReason = reason;
    }

    private void EnsureAssignedDriver(Guid driverId)
    {
        if (DriverId != driverId)
            throw new ForbiddenException("Only the driver assigned to this ride can change it.");
    }

    private void TransitionTo(RideStatus next)
    {
        if (!AllowedTransitions[Status].Contains(next))
            throw new ConflictException($"Ride cannot go from {Status} to {next}.");
        Status = next;
    }
}
