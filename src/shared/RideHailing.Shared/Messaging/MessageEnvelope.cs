using System.Text.Json;
using System.Text.Json.Serialization;

namespace RideHailing.Shared.Messaging;

/// <summary>
/// Wrapper around every RabbitMQ message. The metadata is the same for every event; only <see cref="Payload"/> differs.
///
///   { "eventId": "...", "eventType": "ride.requested", "correlationId": "...", "source": "ride-service",
///     "occurredAt": "...", "payload": { "rideId": "...", "pickup": "...", ... } }
/// </summary>
public sealed class MessageEnvelope
{
    /// <summary>Unique id of this message. Consumers use it to detect duplicates (idempotency).</summary>
    public Guid EventId { get; init; }

    /// <summary>Event name, e.g. "ride.requested". Also the RabbitMQ routing key.</summary>
    public string EventType { get; init; } = "";

    /// <summary>Same id for all messages of one ride, so you can follow a ride through every service's logs.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>EventId of the message that caused this one (if any).</summary>
    public Guid? CausationId { get; init; }

    /// <summary>Service that created the message.</summary>
    public string Source { get; init; } = "";

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>The event data, kept as raw JSON until the consumer knows which type to read it as.</summary>
    public JsonElement Payload { get; init; }

    public static MessageEnvelope Create<T>(string eventType, Guid correlationId, string source, T payload, Guid? causationId = null) => new()
    {
        EventId = Guid.NewGuid(),
        EventType = eventType,
        CorrelationId = correlationId,
        CausationId = causationId,
        Source = source,
        OccurredAt = DateTimeOffset.UtcNow,
        Payload = JsonSerializer.SerializeToElement(payload, Json.Options),
    };

    public T PayloadAs<T>() =>
        Payload.Deserialize<T>(Json.Options) ?? throw new InvalidOperationException($"Empty payload for {EventType}");
}

/// <summary>JSON settings for all messages (camelCase, enums as strings).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
