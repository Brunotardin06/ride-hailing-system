using RabbitMQ.Client;

namespace RideHailing.Shared.Messaging;

/// <summary>
/// The whole RabbitMQ setup in one place:
///
///   publisher ──► exchange "ridehailing.events" (topic) ──routing key = event type──► one queue per consuming service
///                                                                                       │ (failed after retries)
///                                                                                       ▼
///                                       exchange "ridehailing.dead-letter" (direct) ──► "&lt;queue&gt;.dlq"
///
/// - Publishers only know the EXCHANGE, never the queues → adding a consumer does not change the publisher.
/// - Every service declares the whole topology on connect (declaring is idempotent). So a queue exists, and
///   buffers messages, even if its consumer has not started yet. This is what "lost 0 of 76" measured in v1.
/// </summary>
public static class RabbitMqTopology
{
    public const string EventsExchange = "ridehailing.events";
    public const string DeadLetterExchange = "ridehailing.dead-letter";

    public static class Queues
    {
        public const string RideService = "ride-service";
        public const string DriverService = "driver-service";
        public const string MatchingService = "matching-service";
        public const string RealtimeService = "realtime-service";
    }

    /// <summary>Bindings: which event types (routing keys) each queue receives.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Bindings = new Dictionary<string, string[]>
    {
        [Queues.MatchingService] = [EventTypes.RideRequested, EventTypes.DriverRejectedRide],
        [Queues.RideService] = [EventTypes.RideMatchingFailed],
        [Queues.DriverService] = [EventTypes.RideCompleted, EventTypes.RideCancelled],
        [Queues.RealtimeService] =
        [
            EventTypes.RideOfferCreated, EventTypes.RideOfferWithdrawn, EventTypes.DriverRejectedRide,
            EventTypes.RideAssigned, EventTypes.RideStarted, EventTypes.RideCompleted, EventTypes.RideCancelled,
            EventTypes.RideNoDriverAvailable,
        ],
    };

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        // durable = survives a broker restart
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);

        foreach (var (queue, routingKeys) in Bindings)
        {
            // A rejected message (requeue: false) is moved by RabbitMQ to the dead-letter exchange → "<queue>.dlq".
            var arguments = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queue,
            };
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: ct);
            foreach (var key in routingKeys)
                await channel.QueueBindAsync(queue, EventsExchange, key, cancellationToken: ct);

            var dlq = DeadLetterQueue(queue);
            await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await channel.QueueBindAsync(dlq, DeadLetterExchange, queue, cancellationToken: ct);
        }
    }
}
