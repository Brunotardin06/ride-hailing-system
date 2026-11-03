using AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;
using RideHailing.Shared.Metrics;

namespace AnalyticsService.Controllers;

/// <summary>Responsibility B — live communication measurements reported by the services.</summary>
[ApiController]
[Route("api/metrics")]
public class MetricsController(MetricsService metrics) : ControllerBase
{
    /// <summary>Batch of measurements (sent by each service's MetricsReporter).</summary>
    [HttpPost("measurements")]
    public async Task<IActionResult> Submit(List<MeasurementDto> batch, CancellationToken ct)
    {
        var stored = await metrics.AddAsync(batch, ct);
        return Accepted(new { stored });
    }

    /// <summary>Count, error rate, avg/p50/p95/p99/max per mechanism + kind + operation. Optional time window.</summary>
    [HttpGet("summary")]
    public Task<List<MetricSummaryDto>> GetSummary(int? minutes, CancellationToken ct) => metrics.GetLiveSummaryAsync(minutes, ct);

    /// <summary>Individual measurements of one metric, oldest first (for latency-over-time charts).</summary>
    [HttpGet("series")]
    public Task<List<MeasurementPointDto>> GetSeries(string mechanism, string kind, string operation, int limit = 300, CancellationToken ct = default) =>
        metrics.GetSeriesAsync(mechanism, kind, operation, limit, ct);

    /// <summary>Start and end of the interval each metric measures.</summary>
    [HttpGet("definitions")]
    public MetricDefinitionDto[] GetDefinitions() => MetricsService.Definitions;

    /// <summary>Delete all live measurements (experiment runs are kept). Useful before a new measurement session.</summary>
    [HttpDelete("measurements")]
    public async Task<IActionResult> DeleteLive(CancellationToken ct) => Ok(new { deleted = await metrics.DeleteLiveAsync(ct) });
}
