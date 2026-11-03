using System.Text.Json;
using AnalyticsService.Data;
using AnalyticsService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace AnalyticsService.Services;

public sealed record K6ImportRequest(string Name, string Mechanism, string Operation, string Scenario, JsonElement Summary, string? Notes);

/// <summary>Stores experiment runs: either submitted with raw samples, or imported from a k6 summary.</summary>
public sealed class ExperimentRunService(AnalyticsDbContext db)
{
    public async Task<RunSummaryDto> CreateAsync(CreateRunRequest request, CancellationToken ct)
    {
        if (request.Samples.Count == 0) throw new ArgumentException("A run needs at least one sample.");

        var stats = Statistics.Summarize(request.Samples.Where(s => s.Success).Select(s => s.DurationMs));
        var errors = request.Samples.Count(s => !s.Success);
        var run = new ExperimentRun
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Mechanism = request.Mechanism,
            Kind = request.Kind,
            Operation = request.Operation,
            Scenario = request.Scenario,
            Tool = request.Tool,
            StartedAt = request.StartedAt,
            FinishedAt = request.FinishedAt,
            CreatedAt = DateTimeOffset.UtcNow,
            Count = request.Samples.Count,
            Errors = errors,
            AvgMs = stats.AvgMs,
            P50Ms = stats.P50Ms,
            P95Ms = stats.P95Ms,
            P99Ms = stats.P99Ms,
            MaxMs = stats.MaxMs,
            // Throughput = successful samples per second of active execution time.
            ThroughputPerSecond = request.WallTimeMs > 0 ? Math.Round((request.Samples.Count - errors) / (request.WallTimeMs / 1000), 2) : null,
            ParametersJson = request.Parameters is null ? null : JsonSerializer.Serialize(request.Parameters),
            Notes = request.Notes,
        };
        db.ExperimentRuns.Add(run);

        var now = DateTimeOffset.UtcNow;
        db.Measurements.AddRange(request.Samples.Select(s => new Measurement
        {
            RunId = run.Id,
            Mechanism = request.Mechanism,
            Kind = request.Kind,
            Operation = request.Operation,
            DurationMs = s.DurationMs,
            Success = s.Success,
            Error = s.Error,
            RecordedAt = now,
            Source = request.Tool,
        }));

        await db.SaveChangesAsync(ct);
        return ToDto(run);
    }

    /// <summary>
    /// Imports the JSON written by "k6 run --summary-export". Uses http_req_duration (REST) or
    /// grpc_req_duration (gRPC), the "iterations" counter for count/throughput and the "req_failed"
    /// (or http_req_failed) rate for errors.
    /// </summary>
    public async Task<RunSummaryDto> ImportK6Async(K6ImportRequest request, CancellationToken ct)
    {
        if (!request.Summary.TryGetProperty("metrics", out var metrics))
            throw new ArgumentException("Not a k6 summary export (no 'metrics' property).");

        var durationMetric = request.Mechanism == Mechanisms.Grpc ? "grpc_req_duration" : "http_req_duration";
        if (!metrics.TryGetProperty(durationMetric, out var duration))
            throw new ArgumentException($"k6 summary has no '{durationMetric}' metric.");

        double? Value(JsonElement metric, string name) =>
            metric.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Statistics.Round(v.GetDouble()) : null;

        var iterations = metrics.TryGetProperty("iterations", out var it) ? it : default;
        var count = iterations.ValueKind == JsonValueKind.Object ? (int)(Value(iterations, "count") ?? 0) : 0;
        var rate = iterations.ValueKind == JsonValueKind.Object ? Value(iterations, "rate") : null;

        var errors = 0;
        if (metrics.TryGetProperty("req_failed", out var failed) || metrics.TryGetProperty("http_req_failed", out failed))
            errors = (int)(Value(failed, "passes") ?? 0); // for a "failed" Rate metric, passes = number of failures

        // Payload size: bytes on the wire per call (k6 counts all bytes incl. headers / HTTP/2 framing).
        var parameters = new Dictionary<string, string>();
        foreach (var (metric, name) in new[] { ("data_received", "bytesReceivedPerCall"), ("data_sent", "bytesSentPerCall") })
        {
            if (count > 0 && metrics.TryGetProperty(metric, out var data) && Value(data, "count") is { } bytes)
                parameters[name] = Math.Round(bytes / count, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var now = DateTimeOffset.UtcNow;
        var run = new ExperimentRun
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Mechanism = request.Mechanism,
            ParametersJson = parameters.Count == 0 ? null : JsonSerializer.Serialize(parameters),
            Kind = MetricKinds.Call,
            Operation = request.Operation,
            Scenario = request.Scenario,
            Tool = "k6",
            StartedAt = now,
            FinishedAt = now,
            CreatedAt = now,
            Count = count,
            Errors = errors,
            AvgMs = Value(duration, "avg"),
            P50Ms = Value(duration, "med"),
            P95Ms = Value(duration, "p(95)"),
            P99Ms = Value(duration, "p(99)"),
            MaxMs = Value(duration, "max"),
            ThroughputPerSecond = rate is null ? null : Math.Round(rate.Value * (count == 0 ? 1 : (double)(count - errors) / count), 2),
            Notes = request.Notes ?? $"k6 {durationMetric}: request sent → response received (k6 definition).",
            RawImport = request.Summary.GetRawText(),
        };
        db.ExperimentRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return ToDto(run);
    }

    public async Task<List<RunSummaryDto>> ListAsync(string? experimentType, string? scenarioKey, Guid? groupId, bool includeDetails, CancellationToken ct)
    {
        var query = db.ExperimentRuns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(experimentType)) query = query.Where(r => r.ExperimentType == experimentType);
        if (!string.IsNullOrWhiteSpace(scenarioKey)) query = query.Where(r => r.ScenarioKey == scenarioKey);
        if (groupId is not null) query = query.Where(r => r.GroupId == groupId);
        var runs = await query.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        return runs.Select(r => ToDto(r, includeDetails)).ToList();
    }

    /// <summary>Researcher's qualitative notes on a run.</summary>
    public async Task<RunSummaryDto> UpdateNotesAsync(Guid id, string? notes, CancellationToken ct)
    {
        var run = await db.ExperimentRuns.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Run not found.");
        run.QualitativeNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync(ct);
        return ToDto(run, true);
    }

    public async Task<RunSummaryDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.ExperimentRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Run not found."), true);

    /// <summary>Latency histogram of a run's successful samples (for distribution charts).</summary>
    public async Task<List<double>> GetSampleDurationsAsync(Guid id, CancellationToken ct) =>
        await db.Measurements.AsNoTracking().Where(m => m.RunId == id && m.Success).Select(m => m.DurationMs).ToListAsync(ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (await db.ExperimentRuns.Where(r => r.Id == id).ExecuteDeleteAsync(ct) == 0) throw new NotFoundException("Run not found.");
    }

    private static RunSummaryDto ToDto(ExperimentRun r) => ToDto(r, false);

    private static RunSummaryDto ToDto(ExperimentRun r, bool includeDetails) => new(r.Id, r.Name, r.Mechanism, r.Kind, r.Operation, r.Scenario, r.Tool,
        r.StartedAt, r.FinishedAt, r.Count, r.Errors, r.Count == 0 ? 0 : Math.Round((double)r.Errors / r.Count, 4),
        r.AvgMs, r.P50Ms, r.P95Ms, r.P99Ms, r.MaxMs, r.ThroughputPerSecond,
        r.ParametersJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(r.ParametersJson, Json.Options),
        r.Notes,
        r.ExperimentType, r.ScenarioKey, r.Status, r.GroupId,
        r.ResultsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, double?>>(r.ResultsJson, Json.Options),
        r.ObservationsJson is null ? null : JsonSerializer.Deserialize<List<string>>(r.ObservationsJson, Json.Options),
        includeDetails && r.DetailsJson is not null ? JsonDocument.Parse(r.DetailsJson).RootElement.Clone() : null,
        r.QualitativeNotes, r.Error);
}
