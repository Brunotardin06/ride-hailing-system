using MatchingService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace MatchingService.Controllers;

/// <summary>
/// Experiment controls (not part of the ride workflow): REST/gRPC benchmark, transport switch and
/// RabbitMQ failure injection.
/// </summary>
[ApiController]
[Route("api/experiments")]
public class ExperimentsController(
    DriverLookup lookup, DriverLookupBenchmark benchmark, FailureInjection failureInjection,
    SimulatedOutage outage, MatchingStats stats, IOptions<MatchingOptions> matchingOptions) : ControllerBase
{
    /// <summary>Experiment A: finished matching operations per outcome since the last reset.</summary>
    [HttpGet("stats")]
    public Dictionary<string, int> GetStats() => stats.Snapshot();

    [HttpDelete("stats")]
    public IActionResult ResetStats()
    {
        stats.Reset();
        return NoContent();
    }

    public sealed record OutageDto(string Instance, bool Enabled, DateTimeOffset? Since);
    public sealed record SetOutageRequest(bool Enabled);

    /// <summary>Experiment A2: is this instance simulating an outage?</summary>
    [HttpGet("simulated-outage")]
    public OutageDto GetOutage() => new(matchingOptions.Value.Instance, outage.Enabled, outage.Since);

    /// <summary>
    /// Experiment A2: start/stop a simulated outage. Only allowed on the dedicated experiment instance,
    /// so demo rides are never affected.
    /// </summary>
    [HttpPut("simulated-outage")]
    public ActionResult<OutageDto> SetOutage(SetOutageRequest request)
    {
        if (!matchingOptions.Value.IsExperimentInstance)
            return Conflict("Simulated outages are only available on the experiment instance (matching-experiment).");
        outage.Set(request.Enabled);
        return GetOutage();
    }

    public sealed record ConfigDto(DriverLookupMode ConfiguredMode, DriverLookupMode ActiveMode, int PendingInjectedFailures);
    public sealed record SetModeRequest(DriverLookupMode? Mode);
    public sealed record FailNextRequest(int Count);

    [HttpGet("config")]
    public ConfigDto GetConfig() => new(lookup.ConfiguredMode, lookup.CurrentMode, failureInjection.Remaining);

    /// <summary>Override the configured transport (DriverLookup:Mode) at runtime; null resets to the configured value.</summary>
    [HttpPut("lookup-mode")]
    public ConfigDto SetMode(SetModeRequest request)
    {
        DriverLookup.OverrideMode(request.Mode);
        return GetConfig();
    }

    /// <summary>Run the REST vs gRPC driver-lookup benchmark and store the results in AnalyticsService.</summary>
    [HttpPost("driver-lookup")]
    public Task<List<RunSummaryDto>> RunBenchmark(BenchmarkRequest request, CancellationToken ct) => benchmark.RunAsync(request, ct);

    /// <summary>
    /// Make the next N RabbitMQ messages consumed by MatchingService fail, to demonstrate retries and the
    /// dead-letter queue (with N &gt; RabbitMQ:MaxRetries the message ends up in matching-service.dlq).
    /// </summary>
    [HttpPost("fail-next")]
    public ConfigDto FailNext(FailNextRequest request)
    {
        failureInjection.FailNext(request.Count);
        return GetConfig();
    }
}
