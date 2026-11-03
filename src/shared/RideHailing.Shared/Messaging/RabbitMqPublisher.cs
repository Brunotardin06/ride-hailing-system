using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RideHailing.Shared.Metrics;

namespace RideHailing.Shared.Messaging;

public interface IMessagePublisher
{
    /// <summary>Publishes the envelope to the events exchange and waits for the broker's confirmation.</summary>
    Task PublishAsync(MessageEnvelope message, CancellationToken ct = default);
}

/// <summary>
/// Publishes to the topic exchange with <b>publisher confirms</b>: PublishAsync only completes after
/// RabbitMQ has accepted (and persisted) the message, otherwise it throws.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnection connection, IMetricsReporter metrics, ILogger<RabbitMqPublisher> logger)
    : IMessagePublisher, IAsyncDisposable
{
    // A channel must not be used by several threads at once, so publishes are serialised.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(MessageEnvelope message, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                var conn = await connection.GetAsync(ct);
                _channel = await conn.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
            }

            var properties = new BasicProperties
            {
                MessageId = message.EventId.ToString(),
                CorrelationId = message.CorrelationId.ToString(),
                Type = message.EventType,
                AppId = message.Source,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent, // survive a broker restart
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            };

            message.PublishedAt = DateTimeOffset.UtcNow; // start of the RabbitMQ "delivery" measurement
            var body = JsonSerializer.SerializeToUtf8Bytes(message, Json.Options);

            // Publish-confirm time: BasicPublish → broker confirmed (persisted) the message. This is NOT the
            // time until a consumer has processed it; for that see the "delivery"/"processing" metrics.
            var stopwatch = Stopwatch.StartNew();
            await _channel.BasicPublishAsync(RabbitMqTopology.EventsExchange, message.EventType, mandatory: false, properties, body, ct);
            metrics.Record(Mechanisms.RabbitMq, MetricKinds.PublishConfirm, message.EventType, stopwatch.Elapsed.TotalMilliseconds, true, message.CorrelationId);
            logger.LogInformation("Published {EventType} {EventId} (correlation {CorrelationId})",
                message.EventType, message.EventId, message.CorrelationId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}
