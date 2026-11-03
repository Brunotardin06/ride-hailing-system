using System.Text;
using AnalyticsService.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace AnalyticsService.Services;

public sealed record StreamMessageDto(int Partition, long Offset, DateTimeOffset Timestamp, string Key, string? EventType, string Value);

public sealed record StreamReadDto(string Topic, string ConsumerGroup, int Count, List<StreamMessageDto> Messages);

public sealed record PartitionStatusDto(int Partition, long LowWatermark, long HighWatermark, long? CommittedOffset, long Lag);

public sealed record ConsumerGroupStatusDto(string Topic, string ConsumerGroup, long TotalMessages, long TotalLag, List<PartitionStatusDto> Partitions);

/// <summary>
/// Read-only views on the Kafka topic, to demonstrate how Kafka differs from a queue:
/// messages stay in the log, and every consumer group has its own position (offset).
/// </summary>
public sealed class KafkaInspector(IOptions<KafkaOptions> options)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the most recent messages with a brand-new, temporary consumer group. This is an independent
    /// consumer: it does not affect the offsets of the "analytics-service" group.
    /// </summary>
    public StreamReadDto ReadLatest(int limit)
    {
        var o = options.Value;
        var group = $"analytics-inspector-{Guid.NewGuid():N}";
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = group,
            EnableAutoCommit = false,
        }).Build();

        var partitions = GetPartitions(o.Topic);
        var assignments = new List<TopicPartitionOffset>();
        var endOffsets = new Dictionary<int, long>();
        foreach (var tp in partitions)
        {
            var w = consumer.QueryWatermarkOffsets(tp, Timeout);
            if (w.High.Value <= w.Low.Value) continue; // empty partition
            endOffsets[tp.Partition.Value] = w.High.Value;
            assignments.Add(new TopicPartitionOffset(tp, new Offset(Math.Max(w.Low.Value, w.High.Value - limit))));
        }

        var messages = new List<StreamMessageDto>();
        if (assignments.Count > 0)
        {
            consumer.Assign(assignments);
            var deadline = DateTime.UtcNow + Timeout;
            var finished = new HashSet<int>();
            while (finished.Count < assignments.Count && DateTime.UtcNow < deadline)
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                if (result?.Message is null) continue;
                var header = result.Message.Headers?.FirstOrDefault(h => h.Key == "event-type");
                messages.Add(new StreamMessageDto(result.Partition.Value, result.Offset.Value,
                    result.Message.Timestamp.UtcDateTime, result.Message.Key,
                    header is null ? null : Encoding.UTF8.GetString(header.GetValueBytes()), result.Message.Value));
                if (result.Offset.Value + 1 >= endOffsets[result.Partition.Value]) finished.Add(result.Partition.Value);
            }
        }
        consumer.Close();

        var latest = messages.OrderByDescending(m => m.Timestamp).Take(limit).ToList();
        return new StreamReadDto(o.Topic, group, latest.Count, latest);
    }

    /// <summary>Per partition: how many messages exist and how far the analytics consumer group has read (lag).</summary>
    public ConsumerGroupStatusDto GetConsumerGroupStatus()
    {
        var o = options.Value;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = o.ConsumerGroup, // only reads committed offsets; does not join the group
            EnableAutoCommit = false,
        }).Build();

        var partitions = GetPartitions(o.Topic);
        var committed = consumer.Committed(partitions, Timeout).ToDictionary(c => c.Partition.Value, c => c.Offset);
        var result = new List<PartitionStatusDto>();
        foreach (var tp in partitions)
        {
            var w = consumer.QueryWatermarkOffsets(tp, Timeout);
            long? offset = committed.TryGetValue(tp.Partition.Value, out var c) && c != Offset.Unset ? c.Value : null;
            var lag = w.High.Value - (offset ?? w.Low.Value);
            result.Add(new PartitionStatusDto(tp.Partition.Value, w.Low.Value, w.High.Value, offset, lag));
        }
        consumer.Close();

        return new ConsumerGroupStatusDto(o.Topic, o.ConsumerGroup,
            result.Sum(p => p.HighWatermark - p.LowWatermark), result.Sum(p => p.Lag), result);
    }

    private List<TopicPartition> GetPartitions(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.Value.BootstrapServers }).Build();
        var metadata = admin.GetMetadata(topic, Timeout);
        return metadata.Topics.Single().Partitions.Select(p => new TopicPartition(topic, p.PartitionId)).OrderBy(p => p.Partition.Value).ToList();
    }
}
