using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace RideHailing.Shared.Messaging;

public interface IMessagePublisher
{
    /// <summary>Publishes to the events exchange and waits until the broker confirms it stored the message.</summary>
    Task PublishAsync(MessageEnvelope message, CancellationToken ct = default);
}

/// <summary>
/// Publishes with <b>publisher confirms</b>: PublishAsync only completes after RabbitMQ accepted and persisted
/// the message; otherwise it throws (and the outbox will try again).
/// Services with a database do not call this directly: they write to the outbox, and the OutboxDispatcher calls it.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnection connection, ILogger<RabbitMqPublisher> logger)
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
                DeliveryMode = DeliveryModes.Persistent, // written to disk: survives a broker restart
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(message, Json.Options);

            // exchange + routing key (= event type). The exchange copies the message to every bound queue.
            await _channel.BasicPublishAsync(RabbitMqTopology.EventsExchange, message.EventType, mandatory: false, properties, body, ct);
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
