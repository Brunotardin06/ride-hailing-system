using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RideHailing.Shared.Messaging;

namespace AnalyticsService.Experiments;

/// <summary>Payload of the synthetic ride lifecycle events used in Experiment B.</summary>
public sealed record LifecycleEvent(Guid RunId, Guid RideId, int Seq, string Status);

/// <summary>
/// Records when each event was published and when each consumer received it, using one monotonic clock
/// (Stopwatch timestamps in this process). Producer and consumers run in the same process, so delivery latency
/// has no clock-synchronisation error.
/// </summary>
public sealed class EventTracker
{
    public sealed record Receipt(Guid EventId, Guid RideId, int Seq, long Timestamp);

    private readonly ConcurrentDictionary<Guid, long> _published = new();
    private readonly ConcurrentDictionary<string, List<Receipt>> _received = new();

    public void Published(Guid eventId) => _published[eventId] = Stopwatch.GetTimestamp();

    public void Received(string consumer, Guid eventId, Guid rideId, int seq)
    {
        var list = _received.GetOrAdd(consumer, _ => []);
        lock (list) list.Add(new Receipt(eventId, rideId, seq, Stopwatch.GetTimestamp()));
    }

    public List<Receipt> ReceiptsOf(string consumer)
    {
        if (!_received.TryGetValue(consumer, out var list)) return [];
        lock (list) return [.. list];
    }

    public int UniqueCount(string consumer) => ReceiptsOf(consumer).Select(r => r.EventId).Distinct().Count();

    /// <summary>Delivery latency (ms) of the first receipt of each event: publish call started → consumer callback.</summary>
    public List<double> Latencies(string consumer) => ReceiptsOf(consumer)
        .GroupBy(r => r.EventId).Select(g => g.First())
        .Where(r => _published.ContainsKey(r.EventId))
        .Select(r => Stopwatch.GetElapsedTime(_published[r.EventId], r.Timestamp).TotalMilliseconds)
        .ToList();

    public long? LastReceiptTimestamp(string consumer) => ReceiptsOf(consumer).Select(r => (long?)r.Timestamp).Max();
}

/// <summary>A RabbitMQ subscriber: one queue, manual ack per message.</summary>
public sealed class RabbitExperimentConsumer(string name, string queue, IConnection connection, EventTracker tracker, Guid runId) : IAsyncDisposable
{
    private IChannel? _channel;
    private string? _tag;
    public int Acks;
    public string Name => name;
    public string Queue => queue;

    public async Task StartAsync(CancellationToken ct)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await _channel.BasicQosAsync(0, 50, false, ct);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(ea.Body.ToArray(), Json.Options);
            var e = envelope?.PayloadAs<LifecycleEvent>();
            if (envelope is not null && e is not null && e.RunId == runId) tracker.Received(name, envelope.EventId, e.RideId, e.Seq);
            await _channel.BasicAckAsync(ea.DeliveryTag, false); // the broker deletes the message from this queue now
            Interlocked.Increment(ref Acks);
        };
        _tag = await _channel.BasicConsumeAsync(queue, autoAck: false, consumer, ct);
    }

    public async Task StopAsync()
    {
        if (_channel is null) return;
        if (_tag is not null && _channel.IsOpen) await _channel.BasicCancelAsync(_tag);
        await _channel.CloseAsync();
        _channel = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

/// <summary>
/// A Kafka subscriber: one consumer group. At-least-once: the offset of a record is stored only after it was
/// processed, and stored offsets are committed in the background (every 200 ms) and on close.
/// </summary>
public sealed class KafkaExperimentConsumer(
    string name, string bootstrapServers, string topic, string groupId,
    IReadOnlyDictionary<int, long> startOffsetsForNewGroup, EventTracker tracker, Guid runId)
    : IAsyncDisposable
{
    private readonly TaskCompletionSource _assigned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    public int Commits;
    public string Name => name;
    public string GroupId => groupId;

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Loop(_cts.Token));
        return _assigned.Task.WaitAsync(TimeSpan.FromSeconds(30)); // ready once partitions are assigned
    }

    private void Loop(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = true,          // background commit of stored offsets…
            AutoCommitIntervalMs = 200,
            EnableAutoOffsetStore = false,    // …but an offset is only stored after processing
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SessionTimeoutMs = 10_000,
        })
        .SetPartitionsAssignedHandler((c, partitions) =>
        {
            // Per partition: resume from the group's committed offset if there is one (a returning consumer,
            // or a partition handed over in a rebalance); otherwise (brand-new group) start exactly where this
            // run's events begin.
            List<TopicPartitionOffset> committed;
            try { committed = c.Committed(partitions, TimeSpan.FromSeconds(5)); }
            catch (KafkaException) { committed = []; }
            var result = partitions.Select(p =>
            {
                var offset = committed.FirstOrDefault(x => x.TopicPartition == p)?.Offset ?? Offset.Unset;
                return offset != Offset.Unset && offset.Value >= 0
                    ? new TopicPartitionOffset(p, offset)
                    : new TopicPartitionOffset(p, new Offset(startOffsetsForNewGroup.GetValueOrDefault(p.Partition.Value, 0)));
            }).ToList();
            _assigned.TrySetResult();
            return result;
        })
        .Build();

        consumer.Subscribe(topic);
        while (!ct.IsCancellationRequested)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(100));
            if (result?.Message is null) continue;
            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(result.Message.Value, Json.Options);
            var e = envelope?.PayloadAs<LifecycleEvent>();
            if (envelope is not null && e is not null && e.RunId == runId) tracker.Received(name, envelope.EventId, e.RideId, e.Seq);
            consumer.StoreOffset(result); // the record stays in the log; only this group's position moves
            Commits++;
        }
        consumer.Close(); // leave the group cleanly
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        if (_loop is not null) await _loop;
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
