using System.Text.Json;
using AnalyticsService.Data;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Messaging;

namespace AnalyticsService.Services;

public sealed record EventTimelineBucket(DateTimeOffset Minute, Dictionary<string, int> Counts);

public sealed record BusinessSummaryDto(
    int TotalEvents,
    int RidesRequested,
    int RidesAccepted,
    int RidesStarted,
    int RidesCompleted,
    int RidesCancelled,
    int RidesWithoutDriver,
    double? CompletionRate,
    double? AvgSecondsToAccept,
    double? AvgRideSeconds,
    DateTimeOffset? LastEventAt,
    Dictionary<string, int> EventsByType,
    List<EventTimelineBucket> Timeline);

public sealed record RideEventDto(
    long Id, Guid EventId, string EventType, Guid RideId, Guid CorrelationId, string Source,
    DateTimeOffset OccurredAt, DateTimeOffset? PublishedAt, DateTimeOffset ConsumedAt, int Partition, long Offset, JsonElement Payload);

/// <summary>Statistics computed from the ride events stored by the Kafka consumer.</summary>
public sealed class BusinessAnalyticsService(AnalyticsDbContext db)
{
    public async Task<BusinessSummaryDto> GetSummaryAsync(int timelineMinutes, CancellationToken ct)
    {
        var byType = await db.RideEvents.GroupBy(e => e.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Type, x => x.Count, ct);
        int Count(string type) => byType.GetValueOrDefault(type);

        // Durations between lifecycle events of the same ride (business time, from OccurredAt).
        var milestones = await db.RideEvents
            .Where(e => e.EventType == EventTypes.RideRequested || e.EventType == EventTypes.RideAssigned
                        || e.EventType == EventTypes.RideStarted || e.EventType == EventTypes.RideCompleted)
            .Select(e => new { e.RideId, e.EventType, e.OccurredAt })
            .ToListAsync(ct);
        var perRide = milestones.GroupBy(m => m.RideId)
            .Select(g => g.GroupBy(m => m.EventType).ToDictionary(x => x.Key, x => x.Min(m => m.OccurredAt)))
            .ToList();
        double? AvgSeconds(string from, string to)
        {
            var values = perRide.Where(r => r.ContainsKey(from) && r.ContainsKey(to))
                .Select(r => (r[to] - r[from]).TotalSeconds).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 2);
        }

        var since = DateTimeOffset.UtcNow.AddMinutes(-timelineMinutes);
        var recent = await db.RideEvents.Where(e => e.OccurredAt >= since)
            .Select(e => new { e.EventType, e.OccurredAt }).ToListAsync(ct);
        var timeline = recent
            .GroupBy(e => new DateTimeOffset(e.OccurredAt.Year, e.OccurredAt.Month, e.OccurredAt.Day, e.OccurredAt.Hour, e.OccurredAt.Minute, 0, TimeSpan.Zero))
            .OrderBy(g => g.Key)
            .Select(g => new EventTimelineBucket(g.Key, g.GroupBy(e => e.EventType).ToDictionary(x => x.Key, x => x.Count())))
            .ToList();

        var requested = Count(EventTypes.RideRequested);
        return new BusinessSummaryDto(
            TotalEvents: byType.Values.Sum(),
            RidesRequested: requested,
            RidesAccepted: Count(EventTypes.RideAssigned),
            RidesStarted: Count(EventTypes.RideStarted),
            RidesCompleted: Count(EventTypes.RideCompleted),
            RidesCancelled: Count(EventTypes.RideCancelled),
            RidesWithoutDriver: Count(EventTypes.RideNoDriverAvailable),
            CompletionRate: requested == 0 ? null : Math.Round((double)Count(EventTypes.RideCompleted) / requested, 4),
            AvgSecondsToAccept: AvgSeconds(EventTypes.RideRequested, EventTypes.RideAssigned),
            AvgRideSeconds: AvgSeconds(EventTypes.RideStarted, EventTypes.RideCompleted),
            LastEventAt: await db.RideEvents.MaxAsync(e => (DateTimeOffset?)e.OccurredAt, ct),
            EventsByType: byType,
            Timeline: timeline);
    }

    public async Task<List<RideEventDto>> GetEventsAsync(int limit, Guid? rideId, string? eventType, CancellationToken ct)
    {
        var query = db.RideEvents.AsNoTracking();
        if (rideId is not null) query = query.Where(e => e.RideId == rideId);
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(e => e.EventType == eventType);
        var events = await query.OrderByDescending(e => e.Id).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
        return events.Select(e => new RideEventDto(e.Id, e.EventId, e.EventType, e.RideId, e.CorrelationId, e.Source,
            e.OccurredAt, e.PublishedAt, e.ConsumedAt, e.Partition, e.Offset, JsonDocument.Parse(e.Payload).RootElement.Clone())).ToList();
    }
}
