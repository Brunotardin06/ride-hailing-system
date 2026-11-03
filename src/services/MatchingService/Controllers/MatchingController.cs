using MatchingService.Services;
using Microsoft.AspNetCore.Mvc;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Messaging;

namespace MatchingService.Controllers;

/// <summary>
/// Synchronous matching (Experiment A, approach "REST"): RideService calls this endpoint and waits for the result.
/// It runs exactly the same MatchingWorkflow as the RabbitMQ consumer.
/// </summary>
[ApiController]
[Route("api/matching")]
public class MatchingController(MatchingWorkflow workflow, SimulatedOutage outage, MatchingStats stats, ILogger<MatchingController> logger) : ControllerBase
{
    /// <summary>
    /// Match a ride now. 200 = outcome (OfferCreated / NoDriverAvailable / RideClosed);
    /// 503 = this instance is (simulated) unavailable.
    /// </summary>
    [HttpPost("requests")]
    public async Task<ActionResult<MatchingResponse>> Match(MatchingRequest request, CancellationToken ct)
    {
        if (outage.Enabled)
        {
            logger.LogWarning("Rejecting matching request for ride {RideId}: simulated outage", request.RideId);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Matching service unavailable",
                detail: "Simulated outage (Experiment A2).");
        }

        var ride = new RideToMatch(request.RideId, request.CorrelationId, request.PassengerId, request.PassengerName,
            request.Pickup, request.Destination, ExperimentPool: request.ExperimentRunId is not null, request.SimulatedMatchingDelayMs);

        // The request key is derived from the ride: if RideService (or a proxy) repeats the call, DriverService
        // returns the existing offer instead of creating a second one.
        try
        {
            return await workflow.MatchAsync(ride, attempt: 1, [], requestKey: $"rest:{request.RideId}", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ct = HttpContext.RequestAborted: the caller gave up (timeout) and closed the connection, and
            // ASP.NET Core cancelled this work. Without that propagation the work would continue unseen.
            stats.RecordAborted();
            logger.LogWarning("Matching for ride {RideId} aborted: the caller disconnected", request.RideId);
            throw;
        }
    }
}

/// <summary>
/// Simulated unavailability of the experiment instance (Experiment A2): REST requests get 503 and the
/// RabbitMQ consumer is cancelled, so RabbitMQ sees no consumer and keeps messages in the queue.
/// For a real process stop use: docker compose stop matching-experiment.
/// </summary>
public sealed class SimulatedOutage(ConsumerPauseSwitch consumers)
{
    public bool Enabled { get; private set; }
    public DateTimeOffset? Since { get; private set; }

    public void Set(bool enabled)
    {
        Enabled = enabled;
        Since = enabled ? DateTimeOffset.UtcNow : null;
        consumers.Set(enabled);
    }
}
