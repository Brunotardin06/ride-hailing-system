using RabbitMQ.Client;

namespace RideHailing.Shared.Messaging;

/// <summary>
/// The complete RabbitMQ topology in one place.
///
///   publisher ──► exchange "ridehailing.events" (topic) ──routing key = event type──► queue per consuming service
///                                                                                       │ (rejected after retries)
///                                                                                       ▼
///                                         exchange "ridehailing.dead-letter" (direct) ──► "&lt;queue&gt;.dlq"
///
/// Every service declares the whole topology when it connects. Declarations are idempotent, and this way
/// queues exist (and buffer messages) even if the consuming service has not started yet.
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

        /// <summary>Experiment A: consumed only by the dedicated experiment instance of MatchingService.</summary>
        public const string MatchingExperiment = "matching-experiment";
    }

    /// <summary>Which event types each queue receives.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Bindings = new Dictionary<string, string[]>
    {
        [Queues.MatchingService] = [EventTypes.RideRequested, EventTypes.DriverRejectedRide],
        [Queues.MatchingExperiment] = [EventTypes.ExperimentRideRequested],
        [Queues.RideService] = [EventTypes.DriverAcceptedRide, EventTypes.RideMatchingFailed, EventTypes.RideOfferCreated],
        [Queues.DriverService] = [EventTypes.RideCompleted, EventTypes.RideCancelled],
        [Queues.RealtimeService] =
        [
            EventTypes.RideOfferCreated, EventTypes.DriverRejectedRide, EventTypes.RideOfferWithdrawn, EventTypes.RideAssigned,
            EventTypes.RideStarted, EventTypes.RideCompleted, EventTypes.RideCancelled, EventTypes.RideNoDriverAvailable,
        ],
    };

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);

        foreach (var (queue, routingKeys) in Bindings)
        {
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
