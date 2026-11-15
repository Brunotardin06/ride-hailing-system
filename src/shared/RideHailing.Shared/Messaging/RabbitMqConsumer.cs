using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace RideHailing.Shared.Messaging;

/// <summary>
/// Each service implements this once (e.g. DriverMessageHandler): "what do I do with a message from my queue?".
/// Registered as scoped: a new instance (and a new DbContext) per message.
/// </summary>
public interface IMessageHandler
{
    Task HandleAsync(MessageEnvelope message, CancellationToken ct);
}

/// <summary>
/// Background service that consumes ONE queue and passes every message to <typeparamref name="THandler"/>.
///
/// Reliability rules (the same for every service):
///  - Manual ack: a message is acknowledged only AFTER the handler succeeded. If the service crashes mid-way,
///    RabbitMQ delivers the message again.
///  - Retries: if the handler throws, the message is re-published to the same queue with x-retry-count + 1
///    (short back-off), up to MaxRetries.
///  - Dead-letter: after the last retry, or if the JSON is unreadable, the message is rejected and RabbitMQ
///    moves it to "&lt;queue&gt;.dlq", so one bad message never blocks the queue.
///  - Duplicates: delivery is at-least-once, so handlers must be idempotent (they check EventId).
/// </summary>
public sealed class RabbitMqConsumer<THandler>(
    string queue,
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumer<THandler>> logger) : BackgroundService
    where THandler : IMessageHandler
{
    private const string RetryHeader = "x-retry-count";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conn = await connection.GetAsync(stoppingToken);
        var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming RabbitMQ queue {Queue}", queue);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
        await channel.CloseAsync();
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var body = delivery.Body.ToArray();
        var retryCount = ReadRetryCount(delivery.BasicProperties);

        MessageEnvelope? message;
        try { message = JsonSerializer.Deserialize<MessageEnvelope>(body, Json.Options); }
        catch (JsonException) { message = null; }

        if (message is null)
        {
            logger.LogError("Unreadable message on {Queue}; moving it to the dead-letter queue", queue);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, ct);
            return;
        }

        try
        {
            logger.LogInformation("Received {EventType} {EventId} on {Queue} (correlation {CorrelationId})",
                message.EventType, message.EventId, queue, message.CorrelationId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            await handler.HandleAsync(message, ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (retryCount < options.Value.MaxRetries)
            {
                logger.LogWarning("Handling {EventType} {EventId} failed (attempt {Attempt}): {Error}. Retrying",
                    message.EventType, message.EventId, retryCount + 1, ex.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (retryCount + 1)), ct);
                await RepublishForRetryAsync(channel, delivery, body, retryCount + 1, ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            }
            else
            {
                logger.LogError(ex, "Handling {EventType} {EventId} failed after {Retries} retries; dead-lettering",
                    message.EventType, message.EventId, retryCount);
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, ct);
            }
        }
    }

    private async Task RepublishForRetryAsync(IChannel channel, BasicDeliverEventArgs delivery, byte[] body, int retryCount, CancellationToken ct)
    {
        var original = delivery.BasicProperties;
        var properties = new BasicProperties
        {
            MessageId = original.MessageId,
            CorrelationId = original.CorrelationId,
            Type = original.Type,
            AppId = original.AppId,
            ContentType = original.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?> { [RetryHeader] = retryCount },
        };
        // The default exchange ("") routes straight to the queue whose name equals the routing key,
        // so the retry goes back to THIS queue only (not to every service bound to the event).
        await channel.BasicPublishAsync("", queue, mandatory: false, properties, body, ct);
    }

    private static int ReadRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(RetryHeader, out var value)) return 0;
        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0,
        };
    }
}
