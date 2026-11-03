using System.Diagnostics;
using System.Net.Http.Json;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace MatchingService.Services;

public sealed record BenchmarkRequest(
    int Iterations = 500,
    int Warmup = 50,
    int Concurrency = 1,
    int Rounds = 2,
    DriverLookupMode[]? Modes = null,
    string? Label = null);

/// <summary>
/// Repeatable REST vs gRPC comparison of exactly the same operation: the driver-availability lookup
/// (same parameters, same DriverService query, same database). Measured interval per sample:
///   start = just before the client call, end = response deserialised into AvailableDriver objects.
///
/// Fairness: both clients are warmed up first (connections, JIT), and the order of the modes alternates
/// per round so neither always runs first. Results are stored in AnalyticsService as one run per mode.
/// </summary>
public sealed class DriverLookupBenchmark(DriverLookup lookup, IHttpClientFactory httpClientFactory, ILogger<DriverLookupBenchmark> logger)
{
    public const string AnalyticsClient = "analytics";

    public async Task<List<RunSummaryDto>> RunAsync(BenchmarkRequest request, CancellationToken ct)
    {
        var iterations = Math.Clamp(request.Iterations, 1, 20_000);
        var concurrency = Math.Clamp(request.Concurrency, 1, 64);
        var rounds = Math.Clamp(request.Rounds, 1, 10);
        var modes = request.Modes is { Length: > 0 } ? request.Modes.Distinct().ToArray() : [DriverLookupMode.Rest, DriverLookupMode.Grpc];

        var samples = modes.ToDictionary(m => m, _ => new List<RunSample>());
        var wallTime = modes.ToDictionary(m => m, _ => 0.0);
        var startedAt = DateTimeOffset.UtcNow;

        foreach (var mode in modes) await RunPhaseAsync(lookup.Get(mode), request.Warmup, concurrency, ct); // warm-up, discarded

        for (var round = 0; round < rounds; round++)
        {
            var order = round % 2 == 0 ? modes : modes.Reverse().ToArray();
            foreach (var mode in order)
            {
                var perRound = iterations / rounds + (round < iterations % rounds ? 1 : 0);
                var stopwatch = Stopwatch.StartNew();
                samples[mode].AddRange(await RunPhaseAsync(lookup.Get(mode), perRound, concurrency, ct));
                wallTime[mode] += stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        var finishedAt = DateTimeOffset.UtcNow;
        var label = string.IsNullOrWhiteSpace(request.Label) ? $"driver-lookup {startedAt:yyyy-MM-dd HH:mm:ss}" : request.Label.Trim();
        var parameters = new Dictionary<string, string>
        {
            ["iterations"] = iterations.ToString(),
            ["warmup"] = request.Warmup.ToString(),
            ["concurrency"] = concurrency.ToString(),
            ["rounds"] = rounds.ToString(),
            ["excludeDriverIds"] = "0",
            ["maxResults"] = "5",
        };

        var client = httpClientFactory.CreateClient(AnalyticsClient);
        var results = new List<RunSummaryDto>();
        foreach (var mode in modes)
        {
            var run = new CreateRunRequest(
                Name: $"{label} · {(mode == DriverLookupMode.Grpc ? "gRPC" : "REST")}",
                Mechanism: mode == DriverLookupMode.Grpc ? Mechanisms.Grpc : Mechanisms.Rest,
                Kind: MetricKinds.Call,
                Operation: "driver-lookup",
                Scenario: $"{iterations} calls, concurrency {concurrency}, {rounds} alternating rounds",
                Tool: "matching-service benchmark",
                StartedAt: startedAt,
                FinishedAt: finishedAt,
                WallTimeMs: wallTime[mode],
                Parameters: parameters,
                Samples: samples[mode],
                Notes: mode == DriverLookupMode.Grpc ? "HTTP/2 + Protobuf (h2c)" : "HTTP/1.1 + JSON");

            var response = await client.PostAsJsonAsync("/api/experiments/runs", run, Json.Options, ct);
            response.EnsureSuccessStatusCode();
            results.Add((await response.Content.ReadFromJsonAsync<RunSummaryDto>(Json.Options, ct))!);
        }

        logger.LogInformation("Driver-lookup benchmark finished: {Summary}",
            string.Join(", ", results.Select(r => $"{r.Mechanism} avg {r.AvgMs:F2} ms p95 {r.P95Ms:F2} ms")));
        return results;
    }

    private static async Task<List<RunSample>> RunPhaseAsync(IDriverDirectory directory, int count, int concurrency, CancellationToken ct)
    {
        var results = new RunSample[count];
        var next = -1;

        async Task Worker()
        {
            int i;
            while ((i = Interlocked.Increment(ref next)) < count)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await directory.GetAvailableDriversAsync([], 5, ct);
                    results[i] = new RunSample(stopwatch.Elapsed.TotalMilliseconds, true);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    results[i] = new RunSample(stopwatch.Elapsed.TotalMilliseconds, false, ex.GetType().Name);
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(concurrency, Math.Max(count, 1))).Select(_ => Worker()));
        return results.ToList();
    }
}
