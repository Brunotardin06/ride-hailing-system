using System.Text.Json;
using System.Text.Json.Serialization;

namespace RideHailing.Shared.Messaging;

/// <summary>
/// Wrapper around every message sent through RabbitMQ or Kafka.
/// The metadata (ids, timestamps) is the same for every event; only <see cref="Payload"/> differs.
/// </summary>
public sealed class MessageEnvelope
{
    /// <summary>Unique id of this message. Consumers use it to detect duplicates (idempotency).</summary>
    public Guid EventId { get; init; }

    /// <summary>Event name, e.g. "ride.requested". Also used as the RabbitMQ routing key.</summary>
    public string EventType { get; init; } = "";

    /// <summary>Id shared by all messages belonging to one ride workflow.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>EventId of the message that caused this one (if any).</summary>
    public Guid? CausationId { get; init; }

    /// <summary>Name of the service that created the message.</summary>
    public string Source { get; init; } = "";

    /// <summary>When the business fact happened.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Set by the publisher immediately before handing the message to the broker.
    /// Start of the "delivery" latency measurement.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

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

/// <summary>JSON settings used for all messages (camelCase, enums as strings).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
