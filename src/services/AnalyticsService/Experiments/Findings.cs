using AnalyticsService.Data;
using AnalyticsService.Models;
using Microsoft.EntityFrameworkCore;

namespace AnalyticsService.Experiments;

/// <summary>
/// Experiment C — architectural analysis. Seeds the scenario descriptions (requirements, hypothesis, expected
/// properties). Everything that is a conclusion (observations, advantages, disadvantages, recommendation) is
/// left empty on purpose: it must be written from the measured evidence, not assumed.
/// </summary>
public static class Findings
{
    public sealed record Criterion(string Key, string Title, string Question);

    /// <summary>Decision criteria used in the trade-off matrices (from the Personal Development Plan).</summary>
    public static readonly Criterion[] Criteria =
    [
        new("immediate-response", "Immediate response required", "Does the caller need the result right away, or only confirmation that the work was accepted?"),
        new("temporal-coupling", "Temporal coupling", "Must both services be available at the same moment?"),
        new("unavailability", "Behaviour when a service is unavailable", "What happens to requests/events while the other side is down, and after it returns?"),
        new("guarantees", "Processing and delivery guarantees", "Acknowledgements, retries, duplicates, ordering."),
        new("persistence-replay", "Persistence and replay", "Is data kept after processing? Can a (new) consumer re-read history?"),
        new("complexity", "Impact on system complexity", "Extra infrastructure, configuration and code paths (e.g. pending states)."),
        new("performance", "Performance (when it matters)", "Response time, time to completion, throughput — only where the scenario needs it."),
        new("consistency", "Consistency and delayed processing", "How long until all services agree; what the user sees meanwhile."),
    ];

    public static readonly Dictionary<string, string[]> Approaches = new()
    {
        ["A"] = ["REST", "RabbitMQ"],
        ["B"] = ["RabbitMQ", "Kafka"],
    };

    private static ScenarioFinding F(string key, string experiment, string title, string question, string requirements, string hypothesis, string expected, string limitations) => new()
    {
        Key = key, Experiment = experiment, Title = title, ResearchQuestion = question, Requirements = requirements,
        Hypothesis = hypothesis, ExpectedProperties = expected, Limitations = limitations, EvidenceStatus = "NotTested",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private const string SharedLimitsA =
        "Single Docker host (no real network latency); small number of rides; one matching instance with one consumer that processes one message at a time; " +
        "synchronous calls have no automatic retry; the dashboard outage is simulated (REST 503 + RabbitMQ consumer cancelled) — a real process stop can be tested with experiments/experiment-a-real-outage.mjs.";

    private const string SharedLimitsB =
        "Single broker each (no replication); consumers are simulated as separate subscriptions inside AnalyticsService; small workloads; " +
        "only classic RabbitMQ queues are tested (not RabbitMQ Streams); Kafka retention is the broker default.";

    public static ScenarioFinding[] Seed =>
    [
        F("A1-normal", "A", "A1 · Normal operation", "SQ1 / SQ2: when is synchronous vs asynchronous communication appropriate?",
            "The passenger needs fast feedback that the request was received; a ride offer should be created as soon as possible; both services are available.",
            "Hypothesis: with REST the passenger's response includes the matching outcome, so it takes as long as matching itself. With RabbitMQ the response only confirms that the request was stored, so it is faster, but the offer is created a little later (outbox + queue + consumer).",
            "Expected (from theory, to be checked): REST = simple request/response flow and an immediate outcome; RabbitMQ = extra infrastructure, an intermediate 'pending' state that the UI must handle, notifications needed for the result.",
            SharedLimitsA),
        F("A2-unavailable", "A", "A2 · Matching Service unavailable", "SQ2 / SQ4: what happens to requests when the receiver is down?",
            "Ride requests should not be lost while Matching Service is temporarily unavailable, and the passenger should get clear feedback.",
            "Hypothesis: REST requests fail (immediately or after a timeout) and nothing is matched during the outage; the client must retry. With RabbitMQ the requests are accepted, kept in the queue and matched after recovery — but only if the queue is durable and messages are persistent.",
            "Expected: asynchronous messaging removes temporal coupling; durability is a configuration choice, not automatic.",
            SharedLimitsA),
        F("A3-slow", "A", "A3 · Slow processing", "SQ1 / SQ4: what are the consequences of slow processing for each approach?",
            "Matching may be slow (e.g. under load). The passenger must not wait indefinitely; work should not be lost or duplicated.",
            "Hypothesis: REST callers wait for the slow processing and hit their timeout; the work may still finish after the caller gave up. With RabbitMQ the requests are accepted quickly, a backlog builds up, and the time until an offer grows for later requests.",
            "Expected: timeouts make synchronous outcomes uncertain (did it happen or not?); queues turn slowness into delay instead of errors.",
            SharedLimitsA),
        F("B1-single", "B", "B1 · Single consumer", "SQ3: practical differences between RabbitMQ and Kafka.",
            "One service must reliably process every ride lifecycle event.",
            "Hypothesis: both deliver all events with low latency. RabbitMQ removes a message after the consumer acknowledges it; Kafka keeps the record and only moves the consumer group's offset.",
            "Expected: similar implementation effort for this simple case; different underlying models (queue vs log).",
            SharedLimitsB),
        F("B2-multiple", "B", "B2 · Multiple independent consumers", "SQ3: how do both support several independent services?",
            "Several independent services (e.g. billing and analytics) each need every ride event.",
            "Hypothesis: both can deliver every event to each service — RabbitMQ with a fanout exchange and one queue per service, Kafka with one consumer group per service. Sharing one queue/group instead divides the events between consumers.",
            "Expected: RabbitMQ needs the routing (exchange + bindings) set up per service; Kafka needs only a new group id.",
            SharedLimitsB),
        F("B3-offline", "B", "B3 · Consumer temporarily offline", "SQ3 / SQ4: what happens to events while a consumer is down?",
            "A consuming service may be down for a while and must not miss events.",
            "Hypothesis: with a durable queue RabbitMQ keeps the events for the offline service and delivers them when it returns; with a temporary (auto-delete) queue they are lost. Kafka keeps the events in the log; the group resumes from its committed offset and the backlog is visible as lag.",
            "Expected: both can cope with consumer downtime when configured for it; the configuration determines the guarantee.",
            SharedLimitsB),
        F("B4-replay", "B", "B4 · Replay of historical events", "SQ3: when is event streaming more appropriate?",
            "A new service (or a fixed bug) needs to process events that were published before it started.",
            "Hypothesis: Kafka can deliver retained events to a new consumer group. With the tested RabbitMQ configuration (queues bound only when a service subscribes, messages deleted after acknowledgement) a new consumer cannot receive earlier events.",
            "Expected: replay is a property of retained logs. RabbitMQ Streams (not tested here) or storing events elsewhere could provide it too.",
            SharedLimitsB),
    ];

    /// <summary>Adds missing scenario rows; never overwrites what the researcher wrote.</summary>
    public static async Task EnsureSeededAsync(AnalyticsDbContext db)
    {
        var existing = await db.ScenarioFindings.Select(f => f.Key).ToListAsync();
        foreach (var finding in Seed.Where(f => !existing.Contains(f.Key))) db.ScenarioFindings.Add(finding);
        await db.SaveChangesAsync();
    }
}
