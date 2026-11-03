using System.Diagnostics;
using System.Text.Json;
using AnalyticsService.Data;
using AnalyticsService.Models;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace AnalyticsService.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "ride-events";
    public string ConsumerGroup { get; set; } = "analytics-service";
    public int Partitions { get; set; } = 3;
}

/// <summary>
/// Consumes "ride-events" in its own consumer group and stores each event (Responsibility A).
/// Kafka keeps the events after they are read, so other consumer groups can read the same stream
/// independently and this group can be reset to replay history.
///
/// Delivery guarantee: at-least-once. The offset is committed only after the event was saved; a
/// redelivered event is recognised by its unique EventId and not stored twice.
/// </summary>
public sealed class KafkaRideEventConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger<KafkaRideEventConsumer> logger)
    : BackgroundService
{
    public const string Source = "analytics-service";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken); // Consume() blocks, so run on its own thread

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var o = options.Value;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = o.ConsumerGroup,
            ClientId = Source,
            AutoOffsetReset = AutoOffsetReset.Earliest, // a new group starts at the beginning of the stream
            EnableAutoCommit = false,                   // commit manually after saving
            // If a consumer dies without leaving the group (e.g. killed process), Kafka waits this long
            // before giving its partitions to another member. Lowered from 45 s so restarts recover quickly.
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 3_000,
        }).Build();

        consumer.Subscribe(o.Topic);
        logger.LogInformation("Consuming Kafka topic {Topic} as group {Group}", o.Topic, o.ConsumerGroup);

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(ct);
                await StoreAsync(result, ct);
                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Processing Kafka message failed: {Message}. Retrying", ex.Message);
                if (result is not null) consumer.Seek(result.TopicPartitionOffset); // read the same message again
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        consumer.Close();
    }

    private async Task StoreAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var consumedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        MessageEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<MessageEnvelope>(result.Message.Value, Json.Options); }
        catch (JsonException) { envelope = null; }
        if (envelope is null)
        {
            logger.LogError("Skipping unreadable Kafka message at {Offset}", result.TopicPartitionOffset);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        if (await db.RideEvents.AnyAsync(e => e.EventId == envelope.EventId, ct))
        {
            logger.LogInformation("Duplicate Kafka event {EventId} ignored", envelope.EventId);
            return;
        }

        var rideId = envelope.Payload.TryGetProperty("rideId", out var r) && r.TryGetGuid(out var g) ? g : Guid.Empty;
        db.RideEvents.Add(new RideEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            RideId = rideId,
            CorrelationId = envelope.CorrelationId,
            Source = envelope.Source,
            OccurredAt = envelope.OccurredAt,
            PublishedAt = envelope.PublishedAt,
            ConsumedAt = consumedAt,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            Payload = envelope.Payload.GetRawText(),
        });

        // Kafka delivery: producer's PublishedAt → this consumer received the message.
        if (envelope.PublishedAt is { } publishedAt)
            db.Measurements.Add(NewMeasurement(MetricKinds.Delivery, $"{envelope.EventType} → {Source}", (consumedAt - publishedAt).TotalMilliseconds, envelope.CorrelationId));

        await db.SaveChangesAsync(ct);

        // Kafka processing: message received → event stored (before the offset commit).
        db.Measurements.Add(NewMeasurement(MetricKinds.Processing, $"{envelope.EventType} @ {Source}", stopwatch.Elapsed.TotalMilliseconds, envelope.CorrelationId));
        await db.SaveChangesAsync(ct);
    }

    private static Measurement NewMeasurement(string kind, string operation, double durationMs, Guid correlationId) => new()
    {
        Mechanism = Mechanisms.Kafka,
        Kind = kind,
        Operation = operation,
        DurationMs = Math.Round(durationMs, 3),
        Success = true,
        RecordedAt = DateTimeOffset.UtcNow,
        Source = Source,
        CorrelationId = correlationId,
    };

    public static async Task EnsureTopicAsync(KafkaOptions options)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = options.Topic, NumPartitions = options.Partitions, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists)) { }
    }
}
