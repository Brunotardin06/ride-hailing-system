namespace AnalyticsService.Models;

// ---------- Responsibility A: business event analytics (from Kafka) ----------

/// <summary>A ride lifecycle event consumed from the Kafka topic "ride-events".</summary>
public class RideEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; }          // unique: duplicates from Kafka redelivery are ignored
    public string EventType { get; set; } = "";
    public Guid RideId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset ConsumedAt { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
    public string Payload { get; set; } = "";
}

// ---------- Responsibility B: experiment metrics ----------

/// <summary>
/// One measured interval. Live measurements (RunId = null) come from the running services;
/// samples of an experiment run have a RunId.
/// </summary>
public class Measurement
{
    public long Id { get; set; }
    public Guid? RunId { get; set; }
    public string Mechanism { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Operation { get; set; } = "";
    public double DurationMs { get; set; }
    public bool Success { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Source { get; set; } = "";
    public Guid? CorrelationId { get; set; }
    public string? Error { get; set; }
}

/// <summary>A repeatable experiment (e.g. 500 driver lookups via gRPC) with its aggregated results.</summary>
public class ExperimentRun
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Mechanism { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string Tool { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public int Count { get; set; }
    public int Errors { get; set; }
    public double? AvgMs { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public double? P99Ms { get; set; }
    public double? MaxMs { get; set; }
    public double? ThroughputPerSecond { get; set; }

    public string? ParametersJson { get; set; }
    public string? Notes { get; set; }

    /// <summary>Original file for imported runs (e.g. k6 summary JSON), kept for traceability.</summary>
    public string? RawImport { get; set; }

    // ---------- Scenario-based experiments (A: sync vs async, B: messaging vs streaming) ----------

    /// <summary>A = Experiment A, B = Experiment B, tool = supplementary benchmark/k6/recovery run.</summary>
    public string ExperimentType { get; set; } = "tool";

    /// <summary>e.g. A1-normal, A2-unavailable, A3-slow, B1-single, B2-multiple, B3-offline, B4-replay.</summary>
    public string? ScenarioKey { get; set; }

    /// <summary>Running, Completed, Incomplete (some work unfinished at the end) or Failed.</summary>
    public string Status { get; set; } = "Completed";

    /// <summary>Runs started together (e.g. REST and RabbitMQ for the same scenario) share a group id.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Named quantitative results (JSON object of numbers).</summary>
    public string? ResultsJson { get; set; }

    /// <summary>Factual observations derived from the measurements (JSON array of strings).</summary>
    public string? ObservationsJson { get; set; }

    /// <summary>Per-ride / per-consumer details and timeline (JSON).</summary>
    public string? DetailsJson { get; set; }

    /// <summary>Observations written by the researcher (editable on the dashboard).</summary>
    public string? QualitativeNotes { get; set; }

    public string? Error { get; set; }
}

// ---------- Experiment C: architectural analysis ----------

/// <summary>
/// Structured analysis of one scenario (A1..A3, B1..B4). Requirements and hypothesis are seeded; all other
/// fields are written by the researcher after looking at the measured evidence.
/// </summary>
public class ScenarioFinding
{
    public string Key { get; set; } = "";
    public string Experiment { get; set; } = "";
    public string Title { get; set; } = "";
    public string ResearchQuestion { get; set; } = "";
    public string Requirements { get; set; } = "";
    public string Hypothesis { get; set; } = "";
    public string ExpectedProperties { get; set; } = "";
    public string Observations { get; set; } = "";
    public string Advantages { get; set; } = "";
    public string Disadvantages { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string Limitations { get; set; } = "";

    /// <summary>NotTested, Hypothesis, Supported, PartiallySupported, Contradicted.</summary>
    public string EvidenceStatus { get; set; } = "NotTested";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One cell of a trade-off matrix: how an approach scores on a decision criterion, and on what evidence.</summary>
public class TradeOffNote
{
    public string Experiment { get; set; } = "";
    public string Criterion { get; set; } = "";
    public string Approach { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>Measured (from runs), Expected (architectural property, not measured here) or NotTested.</summary>
    public string EvidenceType { get; set; } = "NotTested";
    public DateTimeOffset UpdatedAt { get; set; }
}
