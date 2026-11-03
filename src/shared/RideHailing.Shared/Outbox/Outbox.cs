using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RideHailing.Shared.Messaging;

namespace RideHailing.Shared.Outbox;

// Transactional outbox + inbox for the services that own a database (RideService, DriverService).
//
// Problem: "save to the database, then publish to RabbitMQ" is two separate operations. If the service
// crashes in between, the database says one thing and no event was sent.
// Outbox: the event is saved as a row in the SAME transaction as the business change. A background
// dispatcher publishes pending rows afterwards and marks them as dispatched (at-least-once).
// Inbox: the ids of processed incoming messages are stored in the same transaction as their effect,
// so a redelivered message is recognised and skipped (idempotent consumer).

public static class OutboxDestinations
{
    public const string RabbitMq = "rabbitmq";
    public const string Kafka = "kafka";
}

/// <summary>An event waiting to be (or already) published.</summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Envelope { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Id of an incoming message that has been handled.</summary>
public sealed class ProcessedMessage
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Sends an outbox row to one destination (RabbitMQ or Kafka).</summary>
public interface IOutboxTransport
{
    string Destination { get; }
    Task SendAsync(MessageEnvelope message, CancellationToken ct);
}

public sealed class RabbitMqOutboxTransport(IMessagePublisher publisher) : IOutboxTransport
{
    public string Destination => OutboxDestinations.RabbitMq;
    public Task SendAsync(MessageEnvelope message, CancellationToken ct) => publisher.PublishAsync(message, ct);
}

/// <summary>Wakes the dispatcher right after a save, so events go out immediately instead of at the next poll.</summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0);
    public void Notify() { if (_signal.CurrentCount == 0) _signal.Release(); }
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

public static class OutboxExtensions
{
    public static ModelBuilder AddOutboxAndInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DispatchedAt);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.Property(x => x.Destination).HasMaxLength(20);
        });
        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventType).HasMaxLength(100);
        });
        return modelBuilder;
    }

    /// <summary>Queue an event for publishing; it is stored when SaveChanges is called.</summary>
    public static void AddToOutbox(this DbContext db, MessageEnvelope message, params string[] destinations)
    {
        var json = JsonSerializer.Serialize(message, Json.Options);
        foreach (var destination in destinations)
        {
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                EventId = message.EventId,
                EventType = message.EventType,
                Destination = destination,
                Envelope = json,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    public static Task<bool> IsProcessedAsync(this DbContext db, Guid eventId, CancellationToken ct) =>
        db.Set<ProcessedMessage>().AnyAsync(m => m.EventId == eventId, ct);

    public static void MarkProcessed(this DbContext db, MessageEnvelope message) =>
        db.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            EventId = message.EventId,
            EventType = message.EventType,
            ProcessedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>Registers the outbox dispatcher for <typeparamref name="TContext"/> with RabbitMQ as a destination.</summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<IOutboxTransport, RabbitMqOutboxTransport>();
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }
}

/// <summary>Publishes pending outbox rows in insertion order.</summary>
public sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IOutboxTransport> transports,
    OutboxSignal signal,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly Dictionary<string, IOutboxTransport> _transports = transports.ToDictionary(t => t.Destination);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var hadWork = false;
            try
            {
                hadWork = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Outbox dispatch failed: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            if (!hadWork)
            {
                try { await signal.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) { }
            }
        }
    }

    /// <returns>true if rows were dispatched (there may be more).</returns>
    private async Task<bool> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var pending = await db.Set<OutboxMessage>()
            .Where(m => m.DispatchedAt == null)
            .OrderBy(m => m.Id)
            .Take(50)
            .ToListAsync(ct);
        if (pending.Count == 0) return false;

        foreach (var row in pending)
        {
            try
            {
                var message = JsonSerializer.Deserialize<MessageEnvelope>(row.Envelope, Json.Options)!;
                await _transports[row.Destination].SendAsync(message, ct);
                row.DispatchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                row.Attempts++;
                row.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                logger.LogWarning("Could not publish {EventType} {EventId} to {Destination}: {Message}",
                    row.EventType, row.EventId, row.Destination, ex.Message);
                break; // keep the order: retry this row first on the next round
            }
            finally
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        return pending.Any(r => r.DispatchedAt != null);
    }
}
