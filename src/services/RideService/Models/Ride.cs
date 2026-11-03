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
/// How RideService hands the ride to MatchingService (Experiment A):
/// RabbitMq = asynchronous (publish ride.requested, respond immediately);
/// Rest = synchronous (call POST /api/matching/requests and wait for the outcome).
/// </summary>
public enum MatchingMode { RabbitMq, Rest }

/// <summary>
/// A ride and its lifecycle. All status changes go through the methods below, which enforce the
/// allowed transitions, so the status can never become inconsistent:
///
///   Requested ──► Assigned ──► InProgress ──► Completed
///      │             │
///      ├──► Cancelled ◄┘
///      └──► NoDriverAvailable
/// </summary>
public class Ride
{
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
    public Guid CorrelationId { get; private set; }
    public Guid PassengerId { get; private set; }
    public string PassengerName { get; private set; } = "";
    public string Pickup { get; private set; } = "";
    public string Destination { get; private set; } = "";
    public RideStatus Status { get; private set; }

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

    /// <summary>Communication path used to request matching for this ride.</summary>
    public MatchingMode MatchingMode { get; private set; }

    /// <summary>Set for rides created by Experiment A (isolated from demo rides and business analytics).</summary>
    public Guid? ExperimentRunId { get; private set; }

    /// <summary>
    /// When the first offer for this ride was created (DriverService time). While a ride is Requested and this
    /// is null, matching is still pending, e.g. waiting in the RabbitMQ queue.
    /// </summary>
    public DateTimeOffset? OfferCreatedAt { get; private set; }
    public string? OfferedDriverName { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin): concurrent updates of the same ride fail instead of overwriting each other.</summary>
    public uint Version { get; private set; }

    private Ride() { }

    public static Ride Request(Guid passengerId, string passengerName, string pickup, string destination, Guid correlationId,
        MatchingMode matchingMode = MatchingMode.RabbitMq, Guid? experimentRunId = null) => new()
    {
        MatchingMode = matchingMode,
        ExperimentRunId = experimentRunId,
        Id = Guid.NewGuid(),
        CorrelationId = correlationId,
        PassengerId = passengerId,
        PassengerName = passengerName,
        Pickup = pickup,
        Destination = destination,
        Status = RideStatus.Requested,
        RequestedAt = DateTimeOffset.UtcNow,
    };

    public bool IsActive => ActiveStatuses.Contains(Status);

    public static bool CanTransition(RideStatus from, RideStatus to) => AllowedTransitions[from].Contains(to);

    /// <summary>Records that matching produced an offer (keeps the first offer time; later offers update the name).</summary>
    public void RecordOfferCreated(string? driverName, DateTimeOffset createdAt)
    {
        OfferCreatedAt ??= createdAt;
        if (driverName is not null) OfferedDriverName = driverName;
    }

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

    public void Cancel(string reason)
    {
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
        if (!CanTransition(Status, next))
            throw new ConflictException($"Ride {Id} cannot go from {Status} to {next}.");
        Status = next;
    }
}
