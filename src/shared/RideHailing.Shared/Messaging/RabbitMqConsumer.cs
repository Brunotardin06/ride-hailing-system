using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RideHailing.Shared.Metrics;

namespace RideHailing.Shared.Messaging;

/// <summary>Handles messages from one queue. Registered as scoped: one instance (and DbContext) per message.</summary>
public interface IMessageHandler
{
    Task HandleAsync(MessageEnvelope message, CancellationToken ct);
}

/// <summary>
/// Background service that consumes one queue and passes each message to <typeparamref name="THandler"/>.
///
/// Reliability rules (the same for every service):
/// <list type="bullet">
/// <item>Manual acknowledgements: a message is only acked after the handler succeeded.</item>
/// <item>Retries: if the handler throws, the message is re-published to the same queue with an
/// incremented <c>x-retry-count</c> header (with a short back-off), up to <see cref="RabbitMqOptions.MaxRetries"/>.</item>
/// <item>Dead-lettering: after the last retry, or if the message cannot be parsed, it is rejected
/// and RabbitMQ moves it to <c>&lt;queue&gt;.dlq</c>.</item>
/// <item>Duplicates: handlers are idempotent (they check <see cref="MessageEnvelope.EventId"/>), because
/// RabbitMQ guarantees at-least-once delivery, not exactly-once.</item>
/// </list>
/// </summary>
public sealed class RabbitMqConsumer<THandler>(
    string queue,
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IMetricsReporter metrics,
    FailureInjection failureInjection,
    ConsumerPauseSwitch pauseSwitch,
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

        // Subscribe, and follow the pause switch: paused = the AMQP consumer is cancelled, so RabbitMQ sees
        // no consumer at all and keeps new messages in the (durable) queue, exactly like a stopped service.
        string? consumerTag = null;
        var gate = new SemaphoreSlim(1, 1);
        async Task ApplyAsync(bool paused)
        {
            await gate.WaitAsync(stoppingToken);
            try
            {
                if (paused && consumerTag is not null)
                {
                    await channel.BasicCancelAsync(consumerTag, cancellationToken: stoppingToken);
                    consumerTag = null;
                    logger.LogWarning("Consumer on {Queue} paused (simulated unavailability)", queue);
                }
                else if (!paused && consumerTag is null)
                {
                    consumerTag = await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
                    logger.LogInformation("Consuming RabbitMQ queue {Queue}", queue);
                }
            }
            finally { gate.Release(); }
        }

        void OnChanged(bool paused) => _ = ApplyAsync(paused);
        pauseSwitch.Changed += OnChanged;
        await ApplyAsync(pauseSwitch.Paused);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
        pauseSwitch.Changed -= OnChanged;
        await channel.CloseAsync();
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var body = delivery.Body.ToArray();
        var retryCount = ReadRetryCount(delivery.BasicProperties);

        MessageEnvelope? message;
        try
        {
            message = JsonSerializer.Deserialize<MessageEnvelope>(body, Json.Options);
        }
        catch (JsonException)
        {
            message = null;
        }
        if (message is null)
        {
            logger.LogError("Unreadable message on {Queue}; moving it to the dead-letter queue", queue);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, ct);
            return;
        }

        // Delivery latency: publisher's PublishedAt → this callback. Only measured on the first attempt,
        // otherwise retry back-off would be counted as delivery time.
        if (message.PublishedAt is { } publishedAt && retryCount == 0)
        {
            metrics.Record(Mechanisms.RabbitMq, MetricKinds.Delivery, $"{message.EventType} → {queue}",
                (receivedAt - publishedAt).TotalMilliseconds, success: true, message.CorrelationId);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            failureInjection.ThrowIfFailureRequested(message.EventType);

            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            await handler.HandleAsync(message, ct);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            metrics.Record(Mechanisms.RabbitMq, MetricKinds.Processing, $"{message.EventType} @ {queue}",
                stopwatch.Elapsed.TotalMilliseconds, success: true, message.CorrelationId);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            metrics.Record(Mechanisms.RabbitMq, MetricKinds.Processing, $"{message.EventType} @ {queue}",
                stopwatch.Elapsed.TotalMilliseconds, success: false, message.CorrelationId, ex.Message);

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
        // The default exchange ("") routes directly to the queue with the same name as the routing key.
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

/// <summary>
/// Experiment helper: pauses all RabbitMQ consumers of this process (simulated unavailability).
/// Only used by the dedicated experiment instance of MatchingService.
/// </summary>
public sealed class ConsumerPauseSwitch
{
    public bool Paused { get; private set; }
    public event Action<bool>? Changed;

    public void Set(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        Changed?.Invoke(paused);
    }
}

/// <summary>
/// Experiment helper: makes the next N messages fail on purpose, to demonstrate retries and dead-lettering.
/// </summary>
public sealed class FailureInjection
{
    private int _remainingFailures;

    public int Remaining => Volatile.Read(ref _remainingFailures);

    public void FailNext(int count) => Interlocked.Exchange(ref _remainingFailures, Math.Max(0, count));

    public void ThrowIfFailureRequested(string eventType)
    {
        while (true)
        {
            var current = Volatile.Read(ref _remainingFailures);
            if (current <= 0) return;
            if (Interlocked.CompareExchange(ref _remainingFailures, current - 1, current) == current)
                throw new InvalidOperationException($"Injected failure while handling {eventType} (experiment)");
        }
    }
}
