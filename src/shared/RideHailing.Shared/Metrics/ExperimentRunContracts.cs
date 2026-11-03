namespace RideHailing.Shared.Metrics;

/// <summary>One sample of an experiment run.</summary>
public sealed record RunSample(double DurationMs, bool Success, string? Error = null);

/// <summary>
/// A complete experiment run submitted to AnalyticsService (POST /api/experiments/runs).
/// All samples of a run measure the same interval (Mechanism + Kind + Operation).
/// </summary>
public sealed record CreateRunRequest(
    string Name,
    string Mechanism,
    string Kind,
    string Operation,
    string Scenario,
    string Tool,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    /// <summary>Time spent actually executing the samples; used for throughput (successful samples / second).</summary>
    double WallTimeMs,
    Dictionary<string, string>? Parameters,
    List<RunSample> Samples,
    string? Notes = null);

/// <summary>Aggregated results of a run, as returned by AnalyticsService.</summary>
public sealed record RunSummaryDto(
    Guid Id,
    string Name,
    string Mechanism,
    string Kind,
    string Operation,
    string Scenario,
    string Tool,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Count,
    int Errors,
    double ErrorRate,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    double? MaxMs,
    double? ThroughputPerSecond,
    Dictionary<string, string>? Parameters,
    string? Notes,
    // Scenario-based experiments (A/B). Older tool runs (benchmark, k6) use the defaults.
    string ExperimentType = "tool",
    string? ScenarioKey = null,
    string Status = "Completed",
    Guid? GroupId = null,
    Dictionary<string, double?>? Results = null,
    List<string>? Observations = null,
    System.Text.Json.JsonElement? Details = null,
    string? QualitativeNotes = null,
    string? Error = null);
