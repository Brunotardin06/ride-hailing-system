using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AnalyticsService.Data;
using AnalyticsService.Messaging;
using AnalyticsService.Models;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;
using static AnalyticsService.Experiments.ExperimentAnalysis;

namespace AnalyticsService.Experiments;

/// <summary>
/// Experiment B — traditional message queuing (RabbitMQ) vs event streaming (Kafka) for distributing ride
/// lifecycle events. The same generated workload (ride.requested/assigned/started/completed/cancelled) is
/// published through one mechanism; simulated independent services subscribe to it.
///
///   RabbitMQ: fanout exchange "experiment.ride-lifecycle"; one queue per subscribing service (per run);
///             manual ack per message: an acknowledged message is removed from that queue.
///   Kafka:    topic "experiment.ride-lifecycle" (3 partitions, key = ride id); one consumer group per
///             subscribing service (per run); offset committed per record: records stay in the log.
///
/// Experiment resources are separate from the application's exchange/topic, and per-run queues/groups are
/// removed afterwards. Subscribers run inside AnalyticsService, but each has its own queue/group, so from the
/// broker's point of view they are independent consumers.
/// </summary>
public sealed class ExperimentBRunner(
    RabbitMqConnection rabbitConnection,
    RabbitMqInspector rabbit,
    IOptions<KafkaOptions> kafkaOptions,
    AnalyticsDbContext db,
    ILogger<ExperimentBRunner> logger)
{
    public const string Exchange = "experiment.ride-lifecycle";
    public const string Topic = "experiment.ride-lifecycle";

    public async Task RunAsync(Guid runId, string mechanism, ExperimentBRequest p, Action<string> phase, CancellationToken ct)
    {
        var run = await db.ExperimentRuns.FirstAsync(r => r.Id == runId, ct);
        var tracker = new EventTracker();
        var clock = Stopwatch.StartNew();
        var timeline = new List<string>();
        void Log(string text) { timeline.Add($"t+{clock.Elapsed.TotalSeconds:F1}s {text}"); phase(text); }

        var events = Generate(runId, p.Rides);
        var publishAcks = new List<double>();
        var results = new Dictionary<string, double?> { ["eventsPublished"] = 0 };
        var observations = new List<string>();
        var consumers = new List<ConsumerResult>();
        Side side = mechanism == Mechanisms.Kafka
            ? new KafkaSide(kafkaOptions.Value.BootstrapServers, runId, tracker)
            : new RabbitSide(await rabbitConnection.GetAsync(ct), rabbit, runId, tracker, durable: p.RabbitQueueMode != "temporary");
        long firstPublish = 0;

        async Task PublishAsync(IEnumerable<(MessageEnvelope Envelope, LifecycleEvent Event)> batch, int spacingMs = 0)
        {
            foreach (var (envelope, e) in batch)
            {
                if (firstPublish == 0) firstPublish = Stopwatch.GetTimestamp();
                // Start of the delivery interval: right before the publish call (a consumer can receive the
                // message before the broker's acknowledgement reaches the publisher).
                tracker.Published(envelope.EventId);
                var ms = await side.PublishAsync(envelope, e, ct);
                publishAcks.Add(ms);
                results["eventsPublished"] = publishAcks.Count;
                if (spacingMs > 0) await Task.Delay(spacingMs, ct);
            }
        }

        async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var until = clock.Elapsed + timeout;
            while (!condition())
            {
                if (clock.Elapsed > until) return false;
                await Task.Delay(50, ct);
            }
            return true;
        }

        ConsumerResult Summarize(ISubscriber s, int expected, string? note = null)
        {
            var receipts = tracker.ReceiptsOf(s.Name);
            var (received, unique, duplicates) = CountDeliveries(receipts.Select(r => r.EventId).ToList());
            var latencies = tracker.Latencies(s.Name);
            return new ConsumerResult(s.Name, s.Subscription, received, unique, duplicates,
                CountOutOfOrder(receipts.Select(r => (r.RideId, r.Seq))), Math.Max(0, expected - unique),
                P(latencies, 50), P(latencies, 95), Max(latencies), s.AcksOrCommits, note);
        }

        try
        {
            await side.PrepareAsync(ct);
            var n = events.Count;
            Log($"{mechanism}: prepared {side.Describe}; {n} events for {p.Rides} rides");

            switch (p.Scenario)
            {
                case ScenarioB.Single:
                {
                    var a = await side.SubscribeAsync("Consumer A", "a", resume: false, ct);
                    Log("Consumer A subscribed");
                    await PublishAsync(events);
                    Log($"Published {n} events");
                    await WaitUntilAsync(() => tracker.UniqueCount(a.Name) >= n, TimeSpan.FromSeconds(30));
                    await a.StopAsync();
                    consumers.Add(Summarize(a, n));
                    break;
                }
                case ScenarioB.Multiple:
                {
                    var keyB = p.SharedSubscription ? "a" : "b";
                    var a = await side.SubscribeAsync("Consumer A", "a", resume: false, ct);
                    var b = await side.SubscribeAsync("Consumer B", keyB, resume: false, ct);
                    Log(p.SharedSubscription
                        ? "Consumers A and B share ONE subscription (same queue / same consumer group)"
                        : "Consumers A and B have independent subscriptions (own queue / own consumer group)");
                    await PublishAsync(events);
                    Log($"Published {n} events");
                    await WaitUntilAsync(() => p.SharedSubscription
                        ? tracker.ReceiptsOf(a.Name).Concat(tracker.ReceiptsOf(b.Name)).Select(r => r.EventId).Distinct().Count() >= n
                        : tracker.UniqueCount(a.Name) >= n && tracker.UniqueCount(b.Name) >= n, TimeSpan.FromSeconds(30));
                    await Task.Delay(500, ct);
                    await a.StopAsync();
                    await b.StopAsync();
                    consumers.Add(Summarize(a, p.SharedSubscription ? 0 : n, p.SharedSubscription ? "shares the subscription with B" : null));
                    consumers.Add(Summarize(b, p.SharedSubscription ? 0 : n, p.SharedSubscription ? "shares the subscription with A" : null));
                    if (p.SharedSubscription)
                    {
                        results["eventsSeenByAnyConsumer"] = tracker.ReceiptsOf(a.Name).Concat(tracker.ReceiptsOf(b.Name)).Select(r => r.EventId).Distinct().Count();
                        // Ordering: were the events of one ride handled by one consumer, or split over both?
                        var ridesA = tracker.ReceiptsOf(a.Name).Select(r => r.RideId).ToHashSet();
                        var ridesB = tracker.ReceiptsOf(b.Name).Select(r => r.RideId).ToHashSet();
                        results["ridesSplitAcrossConsumers"] = ridesA.Intersect(ridesB).Count();
                        results["rides"] = p.Rides;
                    }
                    break;
                }
                case ScenarioB.Offline:
                {
                    var half = n / 2;
                    var a = await side.SubscribeAsync("Consumer A", "a", resume: false, ct);
                    var b = await side.SubscribeAsync("Consumer B", "b", resume: false, ct);
                    Log("Consumers A and B subscribed (independent)");
                    await PublishAsync(events.Take(half));
                    await WaitUntilAsync(() => tracker.UniqueCount(a.Name) >= half && tracker.UniqueCount(b.Name) >= half, TimeSpan.FromSeconds(20));
                    Log($"Published {half} events; both consumers processed them");

                    await b.StopAsync();
                    Log("Consumer B went offline");
                    await Task.Delay(500, ct); // let the broker finish removing a temporary queue before publishing on
                    var receivedBeforeOffline = tracker.UniqueCount(b.Name);
                    var spacing = Math.Max(0, p.OfflineSeconds * 1000 / Math.Max(1, n - half));
                    await PublishAsync(events.Skip(half), spacing);
                    Log($"Published {n - half} more events while B was offline (producer not affected)");
                    var backlog = await side.BacklogAsync("b", ct);
                    results["backlogWhileOffline"] = backlog;
                    Log(backlog is null ? "B's queue no longer exists (temporary queue was deleted)" : $"Backlog for B while offline: {backlog}");

                    // Recovery time: from the moment B restarts (reconnect / rejoin the group) until its backlog is processed.
                    var back = Stopwatch.GetTimestamp();
                    var b2 = await side.SubscribeAsync("Consumer B", "b", resume: true, ct);
                    Log("Consumer B is back online");
                    var caughtUp = await WaitUntilAsync(() => tracker.UniqueCount(b2.Name) >= n, TimeSpan.FromSeconds(20));
                    await Task.Delay(300, ct);
                    var last = tracker.LastReceiptTimestamp(b2.Name);
                    results["catchUpMs"] = caughtUp && last is { } l && l > back ? Math.Round(Stopwatch.GetElapsedTime(back, l).TotalMilliseconds, 1) : null;
                    results["eventsMissedByB"] = n - tracker.UniqueCount(b2.Name);
                    results["eventsReceivedByBBeforeOffline"] = receivedBeforeOffline;
                    await WaitUntilAsync(() => tracker.UniqueCount(a.Name) >= n, TimeSpan.FromSeconds(10));
                    await a.StopAsync();
                    await b2.StopAsync();
                    consumers.Add(Summarize(a, n));
                    consumers.Add(Summarize(b2, n, "offline for the second half of the events"));
                    break;
                }
                case ScenarioB.Replay:
                {
                    var a = await side.SubscribeAsync("Consumer A", "a", resume: false, ct);
                    await PublishAsync(events);
                    await WaitUntilAsync(() => tracker.UniqueCount(a.Name) >= n, TimeSpan.FromSeconds(30));
                    await Task.Delay(300, ct);
                    await a.StopAsync();
                    Log($"Published {n} events; Consumer A processed and acknowledged/committed them");
                    results["backlogOfAAfterProcessing"] = await side.BacklogAsync("a", ct);

                    var late = await side.SubscribeAsync("Late consumer C", "late", resume: false, ct);
                    Log("A new service (Consumer C) subscribes AFTER the events were published");
                    await WaitUntilAsync(() => tracker.UniqueCount(late.Name) >= n, TimeSpan.FromSeconds(6));
                    await late.StopAsync();
                    results["replayedEvents"] = tracker.UniqueCount(late.Name);
                    consumers.Add(Summarize(a, n));
                    consumers.Add(Summarize(late, n, mechanism == Mechanisms.Kafka
                        ? "new consumer group starting at the offsets where this run's events begin"
                        : "new queue bound to the exchange after publishing"));
                    break;
                }
                default:
                    throw new ArgumentException($"Unknown scenario {p.Scenario}");
            }

            var main = consumers[0];
            var lastA = tracker.LastReceiptTimestamp(main.Name);
            results["publishRejectedAndResent"] = side.PublishRejections;
            results["publishAckP50Ms"] = P(publishAcks, 50);
            results["publishAckP95Ms"] = P(publishAcks, 95);
            results["deliveryP50Ms"] = main.LatencyP50Ms;
            results["deliveryP95Ms"] = main.LatencyP95Ms;
            // Throughput only where events are published as fast as possible (B1/B2), not where publishing is spaced out.
            results["throughputEventsPerSec"] = p.Scenario is ScenarioB.Single or ScenarioB.Multiple && lastA is { } la && firstPublish > 0 && main.Unique > 0
                ? Math.Round(main.Unique / Stopwatch.GetElapsedTime(firstPublish, la).TotalSeconds, 1) : null;
            foreach (var c in consumers)
            {
                var k = c.Name switch { "Consumer A" => "consumerA", "Consumer B" => "consumerB", _ => "lateConsumer" };
                results[$"{k}Unique"] = c.Unique;
                results[$"{k}Duplicates"] = c.Duplicates;
                results[$"{k}OutOfOrder"] = c.OutOfOrder;
            }

            observations.AddRange(Observe(mechanism, p, n, consumers, results));
            run.Status = "Completed";
            run.Count = n;
            run.Errors = consumers.Sum(c => c.Missing);
            var latencies = tracker.Latencies(main.Name);
            run.AvgMs = latencies.Count == 0 ? null : Math.Round(latencies.Average(), 3);
            run.P50Ms = main.LatencyP50Ms;
            run.P95Ms = main.LatencyP95Ms;
            run.P99Ms = P(latencies, 99);
            run.MaxMs = main.LatencyMaxMs;
            run.ThroughputPerSecond = results["throughputEventsPerSec"];
            run.ParametersJson = JsonSerializer.Serialize(side.Configuration(p), Json.Options);

            var now = DateTimeOffset.UtcNow;
            db.Measurements.AddRange(latencies.Select(l => new Measurement { RunId = run.Id, Mechanism = mechanism, Kind = MetricKinds.Delivery,
                Operation = "publish call → Consumer A callback", DurationMs = Math.Round(l, 3), Success = true, RecordedAt = now, Source = "experiment-b" }));
            db.Measurements.AddRange(publishAcks.Select(l => new Measurement { RunId = run.Id, Mechanism = mechanism, Kind = "publish-ack",
                Operation = "publish → broker acknowledgement", DurationMs = Math.Round(l, 3), Success = true, RecordedAt = now, Source = "experiment-b" }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Experiment B run {RunId} failed", runId);
            run.Status = "Failed";
            run.Error = ex.Message;
        }
        finally
        {
            await side.CleanupAsync();
            run.ResultsJson = JsonSerializer.Serialize(results, Json.Options);
            run.ObservationsJson = JsonSerializer.Serialize(observations, Json.Options);
            run.DetailsJson = JsonSerializer.Serialize(new { consumers, timeline, implementation = side.ImplementationSteps }, Json.Options);
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Factual statements derived from the measured numbers (no conclusions about "better").</summary>
    private static IEnumerable<string> Observe(string mechanism, ExperimentBRequest p, int n, List<ConsumerResult> consumers, Dictionary<string, double?> r)
    {
        foreach (var c in consumers)
            yield return $"{c.Name} ({c.Subscription}): received {c.Received} messages, {c.Unique} unique, {c.Duplicates} duplicates, " +
                         $"{c.OutOfOrder} out of order per ride; delivery p50 {Ms(c.LatencyP50Ms)}, p95 {Ms(c.LatencyP95Ms)}.";
        yield return $"Publishing {n} events: broker acknowledgement p50 {Ms(r["publishAckP50Ms"])}, p95 {Ms(r["publishAckP95Ms"])}.";
        if (r.GetValueOrDefault("publishRejectedAndResent") is > 0)
            yield return $"{r["publishRejectedAndResent"]} publishes were rejected by the broker (negative confirm) and re-sent once.";

        switch (p.Scenario)
        {
            case ScenarioB.Multiple when p.SharedSubscription:
                yield return $"With a shared subscription the events were divided: A got {consumers[0].Unique}, B got {consumers[1].Unique} " +
                             $"({r.GetValueOrDefault("eventsSeenByAnyConsumer")} distinct in total). This is competing consumers, not independent services.";
                yield return $"Events of the same ride ended up at both consumers for {r.GetValueOrDefault("ridesSplitAcrossConsumers")} of {r.GetValueOrDefault("rides")} rides" +
                             (mechanism == Mechanisms.Kafka
                                 ? " (Kafka assigns whole partitions to a consumer and the ride id is the key, so a ride's events stay with one consumer)."
                                 : " (RabbitMQ distributes messages of one queue round-robin, so per-ride processing order across consumers is not guaranteed).");
                break;
            case ScenarioB.Multiple:
                yield return $"Both independent subscribers received the full stream (A: {consumers[0].Unique}/{n}, B: {consumers[1].Unique}/{n}).";
                break;
            case ScenarioB.Offline:
                var backlog = r.GetValueOrDefault("backlogWhileOffline");
                yield return backlog is null
                    ? "B's temporary queue was deleted when B disconnected, so events published while B was offline were not kept for B."
                    : $"While B was offline, {backlog} events waited for B ({(mechanism == Mechanisms.Kafka ? "consumer-group lag" : "messages in B's queue")}).";
                yield return $"After reconnecting, B was missing {r.GetValueOrDefault("eventsMissedByB")} of {n} events" +
                             (r.GetValueOrDefault("catchUpMs") is { } cu ? $"; it processed its backlog {Ms(cu)} after restarting (reconnect included)." : ".");
                break;
            case ScenarioB.Replay:
                yield return $"After Consumer A had processed everything, its backlog was {r.GetValueOrDefault("backlogOfAAfterProcessing")}.";
                yield return $"A consumer that subscribed after publishing received {r.GetValueOrDefault("replayedEvents")} of {n} earlier events" +
                             (mechanism == Mechanisms.Kafka
                                 ? " (records are retained in the log; the new group chose where to start reading)."
                                 : " (with this configuration, messages exist only in queues that were bound when they were published, and are deleted after acknowledgement).");
                break;
        }
    }

    private static List<(MessageEnvelope, LifecycleEvent)> Generate(Guid runId, int rides)
    {
        var rideIds = Enumerable.Range(0, Math.Clamp(rides, 1, 500)).Select(_ => Guid.NewGuid()).ToList();
        string[] normal = ["requested", "assigned", "started", "completed"];
        string[] cancelled = ["requested", "assigned", "cancelled"];
        var list = new List<(MessageEnvelope, LifecycleEvent)>();
        // Stage by stage across all rides, like real traffic where many rides are in progress at once.
        for (var stage = 0; stage < 4; stage++)
            for (var i = 0; i < rideIds.Count; i++)
            {
                var stages = i % 5 == 4 ? cancelled : normal; // every 5th ride is cancelled
                if (stage >= stages.Length) continue;
                var e = new LifecycleEvent(runId, rideIds[i], stage + 1, stages[stage]);
                list.Add((MessageEnvelope.Create($"ride.{stages[stage]}", rideIds[i], "experiment-b", e), e));
            }
        return list;
    }

    // ---------- Mechanism-specific parts ----------

    private interface ISubscriber
    {
        string Name { get; }
        string Subscription { get; }
        int AcksOrCommits { get; }
        Task StopAsync();
    }

    private abstract class Side
    {
        public abstract string Describe { get; }
        public abstract string[] ImplementationSteps { get; }
        public abstract Task PrepareAsync(CancellationToken ct);
        public abstract Task<ISubscriber> SubscribeAsync(string name, string key, bool resume, CancellationToken ct);
        public abstract Task<double> PublishAsync(MessageEnvelope envelope, LifecycleEvent e, CancellationToken ct);
        public abstract Task<double?> BacklogAsync(string key, CancellationToken ct);
        public abstract Dictionary<string, string> Configuration(ExperimentBRequest p);
        public abstract Task CleanupAsync();

        /// <summary>Publishes the broker rejected (nack) and that were re-sent.</summary>
        public int PublishRejections;
    }

    private sealed class RabbitSide(IConnection connection, RabbitMqInspector inspector, Guid runId, EventTracker tracker, bool durable) : Side
    {
        private readonly string _prefix = $"expb.{runId.ToString("N")[..8]}";
        private readonly HashSet<string> _queues = [];
        private IChannel? _channel;

        public override string Describe => $"fanout exchange {Exchange}, {(durable ? "durable" : "temporary (auto-delete)")} queue per subscriber";
        public override string[] ImplementationSteps =>
        [
            "Declare a fanout exchange (copies every message to every bound queue).",
            "Declare one queue per subscribing service and bind it to the exchange (a binding must exist BEFORE a message is published, otherwise the message is not stored for that service).",
            "Choose queue durability (durable vs auto-delete) and message persistence (delivery_mode 2).",
            "Consume with manual acknowledgements; an acknowledged message is deleted from that queue.",
            "Two consumers on the SAME queue compete for messages (work distribution); separate queues give independent copies.",
        ];

        private string Queue(string key) => $"{_prefix}.{key}";

        public override async Task PrepareAsync(CancellationToken ct)
        {
            _channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
            await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        }

        public override async Task<ISubscriber> SubscribeAsync(string name, string key, bool resume, CancellationToken ct)
        {
            var queue = Queue(key);
            // Temporary = exclusive + auto-delete: the queue disappears when its consumer goes away (RabbitMQ 4 no
            // longer allows transient non-exclusive queues).
            await _channel!.QueueDeclareAsync(queue, durable: durable, exclusive: !durable, autoDelete: !durable, cancellationToken: ct);
            await _channel.QueueBindAsync(queue, Exchange, "", cancellationToken: ct);
            _queues.Add(queue);
            var consumer = new RabbitExperimentConsumer(name, queue, connection, tracker, runId);
            await consumer.StartAsync(ct);
            return new Wrapper(name, $"queue {queue}", () => consumer.Acks, consumer.StopAsync);
        }

        public override async Task<double> PublishAsync(MessageEnvelope envelope, LifecycleEvent e, CancellationToken ct)
        {
            envelope.PublishedAt = DateTimeOffset.UtcNow;
            var body = JsonSerializer.SerializeToUtf8Bytes(envelope, Json.Options);
            var props = new BasicProperties { MessageId = envelope.EventId.ToString(), Type = envelope.EventType, DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _channel!.BasicPublishAsync(Exchange, "", false, props, body, ct); // returns after the broker's confirm
            }
            catch (RabbitMQ.Client.Exceptions.PublishException)
            {
                // Negative confirm (nack): observed when a temporary queue was being deleted while the message was
                // routed to it. Re-send once and count it, so the effect stays visible in the results.
                PublishRejections++;
                await _channel!.BasicPublishAsync(Exchange, "", false, props, body, ct);
            }
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        public override async Task<double?> BacklogAsync(string key, CancellationToken ct) =>
            await inspector.GetQueueStateAsync(Queue(key), ct) is { } state ? state.Messages : null;

        public override Dictionary<string, string> Configuration(ExperimentBRequest p) => new()
        {
            ["exchange"] = $"{Exchange} (fanout, durable)",
            ["queues"] = durable ? "durable, one per subscriber, deleted after the run" : "temporary: exclusive + auto-delete (removed when the consumer unsubscribes)",
            ["messages"] = "persistent (delivery_mode 2), publisher confirms",
            ["consumption"] = "manual ack per message, prefetch 50",
            ["rides"] = p.Rides.ToString(),
        };

        public override async Task CleanupAsync()
        {
            if (_channel is null) return;
            foreach (var q in _queues)
            {
                try { await _channel.QueueDeleteAsync(q, ifUnused: false, ifEmpty: false); } catch { /* auto-deleted already */ }
            }
            await _channel.CloseAsync();
        }
    }

    private sealed class KafkaSide(string bootstrapServers, Guid runId, EventTracker tracker) : Side
    {
        private readonly string _prefix = $"expb-{runId.ToString("N")[..8]}";
        private readonly HashSet<string> _groups = [];
        private Dictionary<int, long> _startOffsets = [];
        private IProducer<string, string>? _producer;

        public override string Describe => $"topic {Topic} (3 partitions, key = ride id), one consumer group per subscriber";
        public override string[] ImplementationSteps =>
        [
            "Create a topic with partitions (ordering is guaranteed per partition, so the ride id is used as key).",
            "Give every subscribing service its own consumer group; each group tracks its own offsets.",
            "Commit offsets after processing (here: store the offset per record, commit in the background); records are NOT deleted when consumed (retention is time/size based).",
            "Choose where a new group starts reading (earliest, latest or a specific offset/time): this enables replay.",
            "Two consumers in the SAME group share the partitions (work distribution); separate groups each read everything.",
        ];

        private string Group(string key) => $"{_prefix}-{key}";

        public override async Task PrepareAsync(CancellationToken ct)
        {
            using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build())
            {
                try { await admin.CreateTopicsAsync([new TopicSpecification { Name = Topic, NumPartitions = 3, ReplicationFactor = 1 }]); }
                catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists)) { }
            }
            // Where this run's events will begin in each partition (end of the log now).
            using var probe = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = bootstrapServers, GroupId = $"{_prefix}-probe" }).Build();
            _startOffsets = Enumerable.Range(0, 3).ToDictionary(i => i,
                i => probe.QueryWatermarkOffsets(new TopicPartition(Topic, i), TimeSpan.FromSeconds(10)).High.Value);
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true,
                LingerMs = 0, // send immediately (no batching delay), so per-event timings are comparable
            }).Build();
        }

        public override async Task<ISubscriber> SubscribeAsync(string name, string key, bool resume, CancellationToken ct)
        {
            var group = Group(key);
            _groups.Add(group);
            var consumer = new KafkaExperimentConsumer(name, bootstrapServers, Topic, group, _startOffsets, tracker, runId);
            await consumer.StartAsync();
            return new Wrapper(name, $"consumer group {group}", () => consumer.Commits, consumer.StopAsync);
        }

        public override async Task<double> PublishAsync(MessageEnvelope envelope, LifecycleEvent e, CancellationToken ct)
        {
            envelope.PublishedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            await _producer!.ProduceAsync(Topic, new Message<string, string>
            {
                Key = e.RideId.ToString(),
                Value = JsonSerializer.Serialize(envelope, Json.Options),
                Headers = new Confluent.Kafka.Headers { { "event-type", Encoding.UTF8.GetBytes(envelope.EventType) } },
            }, ct);
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Consumer-group lag: records in the log that the group has not committed yet.</summary>
        public override Task<double?> BacklogAsync(string key, CancellationToken ct)
        {
            using var c = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = bootstrapServers, GroupId = Group(key) }).Build();
            var partitions = Enumerable.Range(0, 3).Select(i => new TopicPartition(Topic, i)).ToList();
            var committed = c.Committed(partitions, TimeSpan.FromSeconds(10)).ToDictionary(x => x.Partition.Value, x => x.Offset);
            double lag = 0;
            foreach (var tp in partitions)
            {
                var high = c.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(10)).High.Value;
                var position = committed.TryGetValue(tp.Partition.Value, out var o) && o != Offset.Unset ? o.Value : _startOffsets[tp.Partition.Value];
                lag += Math.Max(0, high - position);
            }
            return Task.FromResult<double?>(lag);
        }

        public override Dictionary<string, string> Configuration(ExperimentBRequest p) => new()
        {
            ["topic"] = $"{Topic} (3 partitions, replication 1, key = ride id)",
            ["producer"] = "acks=all, idempotent, linger.ms=0",
            ["consumption"] = "one consumer group per subscriber; offset stored after processing each record, committed in the background every 200 ms (at-least-once)",
            ["newGroupStart"] = "offsets where this run's events begin (equivalent to 'earliest' for this run)",
            ["rides"] = p.Rides.ToString(),
        };

        public override async Task CleanupAsync()
        {
            _producer?.Dispose();
            try
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
                await admin.DeleteGroupsAsync([.. _groups, $"{_prefix}-probe"]);
            }
            catch { /* groups expire anyway */ }
        }
    }

    private sealed class Wrapper(string name, string subscription, Func<int> acks, Func<Task> stop) : ISubscriber
    {
        public string Name => name;
        public string Subscription => subscription;
        public int AcksOrCommits => acks();
        public Task StopAsync() => stop();
    }
}
