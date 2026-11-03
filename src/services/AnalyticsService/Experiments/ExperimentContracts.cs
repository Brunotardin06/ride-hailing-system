namespace AnalyticsService.Experiments;

// ---------- Experiment A: synchronous (REST) vs asynchronous (RabbitMQ) ride matching ----------

public static class ScenarioA
{
    public const string Normal = "A1-normal";
    public const string Unavailable = "A2-unavailable";
    public const string Slow = "A3-slow";
    public static readonly string[] All = [Normal, Unavailable, Slow];
}

/// <summary>Start Experiment A for one scenario and one or both approaches (REST, RabbitMQ).</summary>
public sealed record ExperimentARequest(
    string Scenario,
    string[]? Approaches = null,
    int Rides = 5,
    int IntervalMs = 300,
    int SlowDelayMs = 4000,
    int RestTimeoutMs = 3000,
    int OutageSeconds = 10,
    int CompletionTimeoutSeconds = 60,
    /// <summary>
    /// simulated (default): the runner switches the experiment instance's simulated outage on/off.
    /// external: the instance is really stopped/started from outside (experiments/experiment-a-real-outage.mjs);
    /// the runner only observes and detects recovery via the instance's /health endpoint.
    /// </summary>
    string OutageMode = "simulated");

/// <summary>Result of one experiment ride (stored in DetailsJson).</summary>
public sealed record ExperimentARide(
    int Index,
    double SentAtMs,
    double HttpMs,
    int HttpStatus,
    bool Accepted,
    Guid? RideId,
    Guid? CorrelationId,
    string? FinalStatus,
    string Outcome,
    double? TimeToOfferMs,
    double? OfferAtMs,
    double? PublishConfirmMs,
    int Offers,
    string? Reason);

public sealed record QueueSample(double AtMs, uint Messages, uint Consumers);

// ---------- Experiment B: RabbitMQ (messaging) vs Kafka (event streaming) ----------

public static class ScenarioB
{
    public const string Single = "B1-single";
    public const string Multiple = "B2-multiple";
    public const string Offline = "B3-offline";
    public const string Replay = "B4-replay";
    public static readonly string[] All = [Single, Multiple, Offline, Replay];
}

/// <summary>Start Experiment B for one scenario and one or both mechanisms.</summary>
public sealed record ExperimentBRequest(
    string Scenario,
    string[]? Mechanisms = null,
    int Rides = 50,
    int OfflineSeconds = 5,
    string RabbitQueueMode = "durable",
    bool SharedSubscription = false);

/// <summary>Per-consumer outcome (stored in DetailsJson).</summary>
public sealed record ConsumerResult(
    string Name,
    string Subscription,
    int Received,
    int Unique,
    int Duplicates,
    int OutOfOrder,
    int Missing,
    double? LatencyP50Ms,
    double? LatencyP95Ms,
    double? LatencyMaxMs,
    int AcksOrCommits,
    string? Note);

public sealed record ExperimentStarted(Guid GroupId, List<Guid> RunIds);

public sealed record ActiveExperimentDto(string Experiment, string Scenario, Guid GroupId, DateTimeOffset StartedAt, string Phase);
