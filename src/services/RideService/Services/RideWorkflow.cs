using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;
using RideService.Data;
using RideService.Models;

namespace RideService.Services;

/// <summary>
/// Application logic of RideService. Used by the REST controller and by the RabbitMQ message handler.
/// Every change and the events it causes are saved in one transaction (via the outbox), then published
/// to RabbitMQ (workflow) and Kafka (event stream for analytics).
/// </summary>
public class RideWorkflow(
    RideDbContext db,
    OutboxSignal outboxSignal,
    MatchingRestClient matchingClient,
    IOptions<MatchingClientOptions> matchingOptions,
    ILogger<RideWorkflow> logger)
{
    public const string Source = "ride-service";

    // ---------- Commands from the frontend (REST) ----------

    /// <summary>
    /// Creates the ride and starts matching via one of two communication paths (Experiment A):
    ///   RabbitMq (asynchronous): ride.requested is stored in the outbox and published; the passenger gets 201
    ///            immediately, matching happens whenever MatchingService consumes the message.
    ///   Rest (synchronous):      MatchingService is called directly and RideService waits for the outcome
    ///            (with a timeout). If MatchingService does not answer, the request fails (503).
    /// Exactly one path is used per ride, so a ride is never matched twice.
    /// </summary>
    public async Task<Ride> RequestRideAsync(CreateRideRequest request, Guid correlationId, CancellationToken ct)
    {
        var mode = request.MatchingMode ?? matchingOptions.Value.Mode;
        var experiment = request.ExperimentRunId is not null;
        if (!experiment && (request.SimulatedMatchingDelayMs > 0 || request.MatchingTimeoutMs is not null))
            throw new ArgumentException("SimulatedMatchingDelayMs and MatchingTimeoutMs are only allowed for experiment rides.");

        if (await db.Rides.AnyAsync(r => r.PassengerId == request.PassengerId && Ride.ActiveStatuses.Contains(r.Status), ct))
            throw new ConflictException("You already have an active ride.");

        var ride = Ride.Request(request.PassengerId, request.PassengerName.Trim(), request.Pickup.Trim(), request.Destination.Trim(),
            correlationId, mode, request.ExperimentRunId);
        db.Rides.Add(ride);

        var payload = new RideRequestedPayload(ride.Id, ride.PassengerId, ride.PassengerName, ride.Pickup, ride.Destination,
            ride.RequestedAt, request.ExperimentRunId, request.SimulatedMatchingDelayMs);
        // Business event stream (Kafka): experiment rides are kept out of the business analytics.
        if (!experiment)
            db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideRequested, ride.CorrelationId, Source, payload), OutboxDestinations.Kafka);
        // Asynchronous matching path: hand the work to RabbitMQ (experiment rides go to the experiment instance).
        if (mode == MatchingMode.RabbitMq)
            db.AddToOutbox(MessageEnvelope.Create(experiment ? EventTypes.ExperimentRideRequested : EventTypes.RideRequested,
                ride.CorrelationId, Source, payload), OutboxDestinations.RabbitMq);

        try
        {
            await SaveAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("You already have an active ride.");
        }
        logger.LogInformation("Ride {RideId} requested by {Passenger} via {Mode} (correlation {CorrelationId})", ride.Id, ride.PassengerName, mode, correlationId);

        if (mode == MatchingMode.Rest)
            await MatchSynchronouslyAsync(ride, request, CancellationToken.None);
        return ride;
    }

    /// <summary>Synchronous matching path: call MatchingService and wait for the outcome.</summary>
    private async Task MatchSynchronouslyAsync(Ride ride, CreateRideRequest request, CancellationToken ct)
    {
        var matchingRequest = new MatchingRequest(ride.Id, ride.CorrelationId, ride.PassengerId, ride.PassengerName, ride.Pickup,
            ride.Destination, ride.ExperimentRunId, request.SimulatedMatchingDelayMs);

        MatchingResponse result;
        try
        {
            result = await matchingClient.RequestMatchingAsync(matchingRequest, ride.ExperimentRunId is not null, request.MatchingTimeoutMs, ct);
        }
        catch (MatchingCallFailedException ex)
        {
            // Temporal coupling: without an answer from MatchingService this request cannot be completed.
            // The ride is cancelled (recorded with the reason) and the passenger gets 503 and must try again.
            // After a timeout MatchingService may still finish the work; the cancellation makes DriverService
            // refuse or withdraw such a late offer.
            logger.LogWarning("Synchronous matching for ride {RideId} failed ({Kind}): {Message}", ride.Id, ex.Kind, ex.Message);
            await UpdateRideAsync(ride.Id, r =>
            {
                if (r.Status != RideStatus.Requested) return;
                var previous = r.Status;
                r.Cancel($"Matching unavailable ({ex.Kind}): {ex.Message}");
                AddStatusChangedEvent(r, EventTypes.RideCancelled, previous, causationId: null);
            });
            throw new ServiceUnavailableException("The matching service did not answer, so the ride could not be requested. Please try again.",
                new Dictionary<string, object?> { ["rideId"] = ride.Id, ["reason"] = ex.Kind });
        }

        await UpdateRideAsync(ride.Id, r =>
        {
            switch (result.Outcome)
            {
                case MatchingOutcome.OfferCreated:
                    r.RecordOfferCreated(result.DriverName, result.OfferCreatedAt ?? DateTimeOffset.UtcNow);
                    break;
                case MatchingOutcome.NoDriverAvailable when r.Status == RideStatus.Requested:
                    r.MarkNoDriverAvailable(result.Reason ?? "No driver available.");
                    AddStatusChangedEvent(r, EventTypes.RideNoDriverAvailable, RideStatus.Requested, causationId: null);
                    break;
            }
        });
    }

    /// <summary>Applies a change and saves; reloads and retries if a message handler updated the ride at the same time.</summary>
    private async Task UpdateRideAsync(Guid rideId, Action<Ride> change)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var ride = await db.Rides.FindAsync([rideId]) ?? throw new NotFoundException($"Ride {rideId} not found.");
                change(ride);
                await SaveAsync(CancellationToken.None);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public Task<Ride> StartAsync(Guid rideId, Guid driverId, CancellationToken ct) =>
        ChangeStatusAsync(rideId, ride => ride.Start(driverId), EventTypes.RideStarted, ct);

    public Task<Ride> CompleteAsync(Guid rideId, Guid driverId, CancellationToken ct) =>
        ChangeStatusAsync(rideId, ride => ride.Complete(driverId), EventTypes.RideCompleted, ct);

    public Task<Ride> CancelAsync(Guid rideId, Guid? passengerId, string? reason, CancellationToken ct) =>
        ChangeStatusAsync(rideId, ride =>
        {
            if (passengerId is not null && passengerId != ride.PassengerId)
                throw new ForbiddenException("Only the passenger of this ride can cancel it.");
            ride.Cancel(string.IsNullOrWhiteSpace(reason) ? "Cancelled by passenger" : reason.Trim());
        }, EventTypes.RideCancelled, ct);

    private async Task<Ride> ChangeStatusAsync(Guid rideId, Action<Ride> change, string eventType, CancellationToken ct)
    {
        var ride = await db.Rides.FindAsync([rideId], ct) ?? throw new NotFoundException($"Ride {rideId} not found.");
        var previous = ride.Status;
        change(ride);
        AddStatusChangedEvent(ride, eventType, previous, causationId: null);
        await SaveAsync(ct);
        logger.LogInformation("Ride {RideId}: {Previous} → {Status}", ride.Id, previous, ride.Status);
        return ride;
    }

    // ---------- Events from other services (RabbitMQ) ----------

    public async Task HandleDriverAcceptedAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return; // duplicate delivery

        var accepted = message.PayloadAs<DriverAcceptedRidePayload>();
        var ride = await db.Rides.FindAsync([accepted.RideId], ct);

        if (ride is null)
        {
            logger.LogWarning("DriverAcceptedRide for unknown ride {RideId}", accepted.RideId);
        }
        else if (ride.Status == RideStatus.Requested)
        {
            ride.AssignDriver(accepted.DriverId, accepted.DriverName, accepted.Vehicle, accepted.LicensePlate);
            AddStatusChangedEvent(ride, EventTypes.RideAssigned, RideStatus.Requested, message.EventId);
        }
        else if (ride.DriverId != accepted.DriverId)
        {
            // The ride was cancelled (or already handled) while the driver was accepting. Tell DriverService
            // to release this driver again by sending a cancellation that names them.
            logger.LogWarning("Driver {DriverId} accepted ride {RideId}, but it is {Status}; releasing the driver",
                accepted.DriverId, ride.Id, ride.Status);
            var payload = new RideStatusChangedPayload(ride.Id, ride.PassengerId, accepted.DriverId, accepted.DriverName,
                ride.Status.ToString(), ride.Status.ToString(), "Ride no longer available", DateTimeOffset.UtcNow);
            db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideCancelled, ride.CorrelationId, Source, payload, message.EventId),
                OutboxDestinations.RabbitMq);
        }

        db.MarkProcessed(message);
        await SaveAsync(ct);
    }

    /// <summary>An offer was created (by either matching path): record when, so pending matching becomes visible.</summary>
    public async Task HandleOfferCreatedAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return;
        var offer = message.PayloadAs<RideOfferCreatedPayload>();
        var ride = await db.Rides.FindAsync([offer.RideId], ct);
        // OccurredAt = when DriverService created the offer (same transaction). Recorded even for cancelled
        // rides, which reveals "late" offers created after a synchronous timeout.
        ride?.RecordOfferCreated(offer.DriverName, message.OccurredAt);
        db.MarkProcessed(message);
        await SaveAsync(ct);
    }

    public async Task HandleMatchingFailedAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return;

        var failed = message.PayloadAs<RideMatchingFailedPayload>();
        var ride = await db.Rides.FindAsync([failed.RideId], ct);
        if (ride is { Status: RideStatus.Requested })
        {
            ride.MarkNoDriverAvailable(failed.Reason);
            AddStatusChangedEvent(ride, EventTypes.RideNoDriverAvailable, RideStatus.Requested, message.EventId);
        }

        db.MarkProcessed(message);
        await SaveAsync(ct);
    }

    // ---------- Helpers ----------

    private void AddStatusChangedEvent(Ride ride, string eventType, RideStatus previous, Guid? causationId)
    {
        var payload = new RideStatusChangedPayload(ride.Id, ride.PassengerId, ride.DriverId, ride.DriverName,
            ride.Status.ToString(), previous.ToString(), ride.StatusReason, DateTimeOffset.UtcNow);
        // Workflow (RabbitMQ) always; business event stream (Kafka) only for non-experiment rides.
        string[] destinations = ride.ExperimentRunId is null
            ? [OutboxDestinations.RabbitMq, OutboxDestinations.Kafka]
            : [OutboxDestinations.RabbitMq];
        db.AddToOutbox(MessageEnvelope.Create(eventType, ride.CorrelationId, Source, payload, causationId), destinations);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        outboxSignal.Notify();
    }
}
