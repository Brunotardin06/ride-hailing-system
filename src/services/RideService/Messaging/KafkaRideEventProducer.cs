using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;

namespace RideService.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "ride-events";
    public int Partitions { get; set; } = 3;
}

/// <summary>
/// Publishes ride lifecycle events to the Kafka topic "ride-events" (used as an outbox destination).
/// The message key is the ride id, so all events of one ride land in the same partition and stay in order.
/// </summary>
public sealed class KafkaRideEventProducer : IOutboxTransport, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;

    public KafkaRideEventProducer(IOptions<KafkaOptions> options)
    {
        _options = options.Value;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = "ride-service",
            Acks = Acks.All,            // wait until the broker has stored the event
            EnableIdempotence = true,   // broker drops duplicates caused by producer retries
        }).Build();
    }

    public string Destination => OutboxDestinations.Kafka;

    public async Task SendAsync(MessageEnvelope message, CancellationToken ct)
    {
        var key = message.Payload.TryGetProperty("rideId", out var rideId) ? rideId.GetString()! : message.CorrelationId.ToString();
        message.PublishedAt = DateTimeOffset.UtcNow; // start of the Kafka "delivery" measurement

        await _producer.ProduceAsync(_options.Topic, new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(message, Json.Options),
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(message.EventType) },
                { "event-id", Encoding.UTF8.GetBytes(message.EventId.ToString()) },
                { "correlation-id", Encoding.UTF8.GetBytes(message.CorrelationId.ToString()) },
            },
        }, ct);
    }

    /// <summary>Creates the topic with several partitions if it does not exist yet.</summary>
    public static async Task EnsureTopicAsync(KafkaOptions options)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = options.Topic, NumPartitions = options.Partitions, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // already there
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
