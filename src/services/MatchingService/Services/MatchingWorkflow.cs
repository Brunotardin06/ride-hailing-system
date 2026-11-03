using Microsoft.Extensions.Options;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Messaging;

namespace MatchingService.Services;

public sealed class MatchingOptions
{
    /// <summary>How many drivers may be offered the same ride before giving up.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>How many candidates to ask DriverService for per lookup.</summary>
    public int CandidatesPerLookup { get; set; } = 5;

    /// <summary>
    /// Main = consumes the "matching-service" queue (demo rides).
    /// Experiment = dedicated instance for Experiment A: consumes "matching-experiment" and can simulate being unavailable.
    /// </summary>
    public string Instance { get; set; } = "Main";

    public bool IsExperimentInstance => Instance.Equals("Experiment", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Everything needed to match a ride; built from a RabbitMQ message or from a REST request.</summary>
public sealed record RideToMatch(
    Guid RideId, Guid CorrelationId, Guid PassengerId, string PassengerName, string Pickup, string Destination,
    bool ExperimentPool, int SimulatedDelayMs);

/// <summary>
/// The matching business logic. It is transport-independent: the same method is used by
///   - the RabbitMQ consumer (asynchronous path: ride.requested / driver.rejected-ride), and
///   - the REST endpoint POST /api/matching/requests (synchronous path, Experiment A).
/// Steps: (optional simulated delay) → find drivers → create offer at DriverService → outcome.
/// </summary>
public sealed class MatchingWorkflow(
    DriverLookup driverLookup,
    OfferClient offerClient,
    IMessagePublisher publisher,
    IOptions<MatchingOptions> options,
    MatchingStats stats,
    ILogger<MatchingWorkflow> logger)
{
    public const string Source = "matching-service";

    // ---------- Asynchronous path (RabbitMQ) ----------

    public async Task HandleRideRequestedAsync(MessageEnvelope message, CancellationToken ct)
    {
        var p = message.PayloadAs<RideRequestedPayload>();
        var ride = new RideToMatch(p.RideId, message.CorrelationId, p.PassengerId, p.PassengerName, p.Pickup, p.Destination,
            ExperimentPool: p.ExperimentRunId is not null, p.SimulatedMatchingDelayMs);

        // RequestKey = id of the triggering message: a redelivered message never creates a second offer.
        var result = await MatchAsync(ride, attempt: 1, [], requestKey: message.EventId.ToString(), ct);
        await PublishIfFailedAsync(ride, result, message, ct);
    }

    public async Task HandleDriverRejectedAsync(MessageEnvelope message, CancellationToken ct)
    {
        var p = message.PayloadAs<DriverRejectedRidePayload>();
        logger.LogInformation("Driver {Driver} did not take ride {RideId} ({Reason}); re-matching", p.DriverName, p.RideId, p.Reason);
        var ride = new RideToMatch(p.RideId, message.CorrelationId, p.PassengerId, p.PassengerName, p.Pickup, p.Destination, false, 0);

        var result = p.Attempt + 1 > options.Value.MaxAttempts
            ? new MatchingResponse(MatchingOutcome.NoDriverAvailable, null, null, null, null,
                $"No driver accepted the ride after {options.Value.MaxAttempts} offers.")
            : await MatchAsync(ride, p.Attempt + 1, p.ExcludedDriverIds, message.EventId.ToString(), ct);
        await PublishIfFailedAsync(ride, result, message, ct);
    }

    private async Task PublishIfFailedAsync(RideToMatch ride, MatchingResponse result, MessageEnvelope cause, CancellationToken ct)
    {
        if (result.Outcome != MatchingOutcome.NoDriverAvailable) return;
        var payload = new RideMatchingFailedPayload(ride.RideId, ride.PassengerId, result.Reason ?? "No driver available.", 0);
        await publisher.PublishAsync(MessageEnvelope.Create(EventTypes.RideMatchingFailed, cause.CorrelationId, Source, payload, cause.EventId), ct);
    }

    // ---------- Shared business logic ----------

    public async Task<MatchingResponse> MatchAsync(RideToMatch ride, int attempt, IReadOnlyList<Guid> excludedDrivers, string requestKey, CancellationToken ct)
    {
        var result = await MatchCoreAsync(ride, attempt, excludedDrivers, requestKey, ct);
        stats.Record(result.Outcome);
        return result;
    }

    private async Task<MatchingResponse> MatchCoreAsync(RideToMatch ride, int attempt, IReadOnlyList<Guid> excludedDrivers, string requestKey, CancellationToken ct)
    {
        if (ride.SimulatedDelayMs > 0)
        {
            // Experiment A3 "slow processing": scoped to this one experiment ride.
            logger.LogInformation("Simulating {Delay} ms of slow processing for experiment ride {RideId}", ride.SimulatedDelayMs, ride.RideId);
            await Task.Delay(ride.SimulatedDelayMs, ct);
        }

        var candidates = await driverLookup.GetAvailableDriversAsync(excludedDrivers, options.Value.CandidatesPerLookup, ride.CorrelationId, ct, ride.ExperimentPool);

        foreach (var driver in candidates)
        {
            var request = new CreateOfferRequest(requestKey, ride.RideId, driver.Id, ride.PassengerId,
                ride.PassengerName, ride.Pickup, ride.Destination, attempt);

            var (result, offer) = await offerClient.CreateOfferAsync(request, ride.CorrelationId, ct);
            switch (result)
            {
                case OfferResult.Created:
                case OfferResult.AlreadyExists:
                    logger.LogInformation("Ride {RideId} offered to {Driver} (attempt {Attempt}{Duplicate})", ride.RideId, driver.Name, attempt,
                        result == OfferResult.AlreadyExists ? ", existing offer – duplicate request" : "");
                    return new MatchingResponse(MatchingOutcome.OfferCreated, offer!.Id, offer.DriverId,
                        result == OfferResult.Created ? driver.Name : null, offer.CreatedAt, null);
                case OfferResult.RideClosed:
                    logger.LogInformation("Ride {RideId} was cancelled; stop matching", ride.RideId);
                    return new MatchingResponse(MatchingOutcome.RideClosed, null, null, null, null, "The ride was cancelled.");
                case OfferResult.DriverUnavailable:
                    logger.LogInformation("{Driver} became unavailable; trying next candidate", driver.Name);
                    continue;
            }
        }

        var reason = excludedDrivers.Count == 0 ? "No driver is available right now." : "No other driver is available.";
        logger.LogWarning("Matching failed for ride {RideId}: {Reason}", ride.RideId, reason);
        return new MatchingResponse(MatchingOutcome.NoDriverAvailable, null, null, null, null, reason);
    }
}
