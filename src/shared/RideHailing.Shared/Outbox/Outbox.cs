using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RideHailing.Shared.Messaging;

namespace RideHailing.Shared.Outbox;

// Transactional OUTBOX + INBOX for services that own a database (RideService, DriverService).
//
// Problem ("dual write"): "save to the DB, then publish to RabbitMQ" are two separate operations.
//   Crash between them → the DB changed but no event was sent (or the reverse).
// Outbox: the event is saved as a ROW in the SAME transaction as the business change (one SaveChanges).
//   A background dispatcher then publishes pending rows and marks them dispatched (at-least-once).
// Inbox: ids of handled incoming messages are stored in the same transaction as their effect,
//   so a redelivered message is recognised and skipped (idempotent consumer).

/// <summary>An event waiting to be (or already) published.</summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
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

/// <summary>Wakes the dispatcher right after a save, so events go out immediately instead of at the next poll.</summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0);
    public void Notify() { if (_signal.CurrentCount == 0) _signal.Release(); }
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// EF Core interceptor: runs after every successful SaveChanges and wakes the dispatcher.
/// Registered on the DbContext: AddDbContext((sp, o) => o.UseNpgsql(...).AddInterceptors(sp.GetRequiredService&lt;OutboxNotifyInterceptor&gt;())).
/// </summary>
public sealed class OutboxNotifyInterceptor(OutboxSignal signal) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        signal.Notify();
        return ValueTask.FromResult(result);
    }
}

public static class OutboxExtensions
{
    /// <summary>Call in OnModelCreating: adds the OutboxMessages and ProcessedMessages tables.</summary>
    public static ModelBuilder AddOutboxAndInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DispatchedAt);
            e.Property(x => x.EventType).HasMaxLength(100);
        });
        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventType).HasMaxLength(100);
        });
        return modelBuilder;
    }

    /// <summary>Queue an event for publishing. Nothing is sent yet: it is stored when SaveChanges is called.</summary>
    public static void AddToOutbox(this DbContext db, MessageEnvelope message) =>
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            EventId = message.EventId,
            EventType = message.EventType,
            Envelope = JsonSerializer.Serialize(message, Json.Options),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>Inbox check: was this incoming message already handled?</summary>
    public static Task<bool> IsProcessedAsync(this DbContext db, Guid eventId, CancellationToken ct) =>
        db.Set<ProcessedMessage>().AnyAsync(m => m.EventId == eventId, ct);

    /// <summary>Inbox write: remember this message (saved together with its effect).</summary>
    public static void MarkProcessed(this DbContext db, MessageEnvelope message) =>
        db.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            EventId = message.EventId,
            EventType = message.EventType,
            ProcessedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>Registers the dispatcher that publishes the outbox of <typeparamref name="TContext"/> to RabbitMQ.</summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<OutboxNotifyInterceptor>();
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }
}

/// <summary>Background service: publishes pending outbox rows in insertion order.</summary>
public sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    OutboxSignal signal,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
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

            // Nothing to do → sleep until a save signals new rows, or at most 1 s (safety net).
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
                await publisher.PublishAsync(message, ct);
                row.DispatchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                row.Attempts++;
                row.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                logger.LogWarning("Could not publish {EventType} {EventId}: {Message}", row.EventType, row.EventId, ex.Message);
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
