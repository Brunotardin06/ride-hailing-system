using AnalyticsService.Data;
using AnalyticsService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Metrics;

namespace AnalyticsService.Services;

public sealed record MetricSummaryDto(
    string Mechanism,
    string Kind,
    string Operation,
    int Count,
    int Errors,
    double ErrorRate,
    double? AvgMs,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    double? MaxMs,
    /// <summary>Measurements per second between the first and last one in the window (observed rate, not capacity).</summary>
    double? ObservedPerSecond,
    DateTimeOffset FirstAt,
    DateTimeOffset LastAt);

public sealed record MeasurementPointDto(DateTimeOffset RecordedAt, double DurationMs, bool Success, string Source, string? Error);

public sealed record MetricDefinitionDto(string Mechanism, string Kind, string Operation, string Start, string End, string MeasuredBy, string Notes);

/// <summary>Responsibility B: stores measurements from the services and aggregates them.</summary>
public sealed class MetricsService(AnalyticsDbContext db)
{
    private const int MaxRowsPerQuery = 100_000;

    public async Task<int> AddAsync(IReadOnlyCollection<MeasurementDto> batch, CancellationToken ct)
    {
        foreach (var m in batch)
        {
            db.Measurements.Add(new Measurement
            {
                Mechanism = m.Mechanism,
                Kind = m.Kind,
                Operation = m.Operation.Length > 150 ? m.Operation[..150] : m.Operation,
                DurationMs = m.DurationMs,
                Success = m.Success,
                RecordedAt = m.RecordedAt,
                Source = m.Source,
                CorrelationId = m.CorrelationId,
                Error = m.Error is { Length: > 500 } e ? e[..500] : m.Error,
            });
        }
        await db.SaveChangesAsync(ct);
        return batch.Count;
    }

    /// <summary>Aggregates of the live measurements (not part of an experiment run), per mechanism + kind + operation.</summary>
    public async Task<List<MetricSummaryDto>> GetLiveSummaryAsync(int? minutes, CancellationToken ct)
    {
        var query = db.Measurements.AsNoTracking().Where(m => m.RunId == null);
        if (minutes is > 0) query = query.Where(m => m.RecordedAt >= DateTimeOffset.UtcNow.AddMinutes(-minutes.Value));
        var rows = await query.OrderByDescending(m => m.Id).Take(MaxRowsPerQuery)
            .Select(m => new { m.Mechanism, m.Kind, m.Operation, m.DurationMs, m.Success, m.RecordedAt })
            .ToListAsync(ct);

        return rows.GroupBy(r => (r.Mechanism, r.Kind, r.Operation))
            .Select(g =>
            {
                var stats = Statistics.Summarize(g.Where(r => r.Success).Select(r => r.DurationMs));
                var count = g.Count();
                var errors = g.Count(r => !r.Success);
                var first = g.Min(r => r.RecordedAt);
                var last = g.Max(r => r.RecordedAt);
                var span = (last - first).TotalSeconds;
                return new MetricSummaryDto(g.Key.Mechanism, g.Key.Kind, g.Key.Operation, count, errors,
                    Math.Round((double)errors / count, 4), stats.AvgMs, stats.P50Ms, stats.P95Ms, stats.P99Ms, stats.MaxMs,
                    span > 0 ? Math.Round(count / span, 3) : null, first, last);
            })
            .OrderBy(s => s.Mechanism).ThenBy(s => s.Kind).ThenBy(s => s.Operation)
            .ToList();
    }

    public async Task<List<MeasurementPointDto>> GetSeriesAsync(string mechanism, string kind, string operation, int limit, CancellationToken ct)
    {
        var points = await db.Measurements.AsNoTracking()
            .Where(m => m.RunId == null && m.Mechanism == mechanism && m.Kind == kind && m.Operation == operation)
            .OrderByDescending(m => m.Id)
            .Take(Math.Clamp(limit, 1, 5000))
            .Select(m => new MeasurementPointDto(m.RecordedAt, m.DurationMs, m.Success, m.Source, m.Error))
            .ToListAsync(ct);
        points.Reverse();
        return points;
    }

    public Task<int> DeleteLiveAsync(CancellationToken ct) =>
        db.Measurements.Where(m => m.RunId == null).ExecuteDeleteAsync(ct);

    /// <summary>What exactly each measurement covers. Only rows with the same definition are comparable.</summary>
    public static readonly MetricDefinitionDto[] Definitions =
    [
        new(Mechanisms.Rest, MetricKinds.Call, "driver-lookup",
            "MatchingService calls HttpClient GET /api/drivers/available",
            "JSON response deserialised into AvailableDriver objects",
            "matching-service", "HTTP/1.1 + JSON. Includes connection pool, serialisation and the DriverService database query."),
        new(Mechanisms.Grpc, MetricKinds.Call, "driver-lookup",
            "MatchingService calls the gRPC client GetAvailableDrivers",
            "Protobuf reply received and mapped to AvailableDriver objects",
            "matching-service", "HTTP/2 (h2c) + Protobuf. Same query and parameters as the REST call."),
        new(Mechanisms.RabbitMq, MetricKinds.Delivery, "<event type> → <queue>",
            "Publisher sets PublishedAt immediately before BasicPublish",
            "Consumer callback invoked for the message (before handling)",
            "consuming service", "Includes broker routing, time waiting in the queue (e.g. while the consumer is down) and network. First delivery only, retries excluded. Uses clocks of two processes on the same host."),
        new(Mechanisms.RabbitMq, MetricKinds.Processing, "<event type> @ <queue>",
            "Consumer callback invoked",
            "Handler finished (before the ack); failures recorded with Success = false",
            "consuming service", "Business logic time inside the consumer, e.g. matching including its REST/gRPC calls."),
        new(Mechanisms.Kafka, MetricKinds.Delivery, "<event type> → analytics-service",
            "RideService sets PublishedAt immediately before ProduceAsync (outbox dispatcher)",
            "AnalyticsService consumer receives the record from Consume()",
            "analytics-service", "Includes producer batching, broker write (acks=all), consumer polling. Clocks of two processes on the same host."),
        new(Mechanisms.Kafka, MetricKinds.Processing, "<event type> @ analytics-service",
            "Record received from Consume()",
            "Event stored in PostgreSQL (before the offset commit)",
            "analytics-service", "Includes duplicate check and database insert."),
        new(Mechanisms.SignalR, MetricKinds.AckRoundTrip, "notification:<type> / chat-message",
            "RealtimeService sends the message to the SignalR group",
            "RealtimeService receives the client's Acknowledge(messageId) call",
            "realtime-service", "Round trip server → browser → server, both timestamps on the server clock (no clock skew). Includes client-side handling. No ack within 15 s = failed delivery."),
        new(Mechanisms.Rest, MetricKinds.Call, "driver-lookup (k6 client)",
            "k6 sends the request (http_req_duration / grpc_req_duration)",
            "k6 has received the complete response",
            "experiments/run-k6-comparison.mjs", "Load test from a k6 container directly against DriverService (REST or gRPC). Not comparable with the MatchingService benchmark, which uses the .NET clients."),
        new(Mechanisms.RabbitMq, "recovery", "ride request → offer (matching-service down)",
            "POST /api/rides returned 201 (consumer stopped)",
            "Offer visible via DriverService REST API (100 ms polling)",
            "experiments/rabbitmq-recovery.mjs", "Shows that queued messages survive a consumer outage. Duration includes the outage itself."),
    ];
}
