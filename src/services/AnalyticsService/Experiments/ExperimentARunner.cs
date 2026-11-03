using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using AnalyticsService.Data;
using AnalyticsService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;
using static AnalyticsService.Experiments.ExperimentAnalysis;

namespace AnalyticsService.Experiments;

/// <summary>
/// Experiment A — synchronous (REST) vs asynchronous (RabbitMQ) communication for Ride Service → Matching Service.
///
/// For one approach and scenario it:
///  1. resets the experiment driver pool (isolated from demo drivers),
///  2. (A2) makes the dedicated "matching-experiment" instance unavailable,
///  3. sends N ride requests to RideService at a fixed interval (open workload, like independent passengers),
///     measuring the HTTP response time on the client side,
///  4. samples the matching-experiment queue (messages, consumers) every 250 ms,
///  5. (A2) restores the instance after the outage,
///  6. waits until every accepted ride has a business outcome (offer created / no driver) or the timeout,
///  7. computes results + factual observations, stores them, and cancels the experiment rides (cleanup).
///
/// Measured intervals (single clock where possible):
///  - HTTP response time: Stopwatch in this process around POST /api/rides (passenger-facing latency).
///  - Time to offer: ride.RequestedAt (RideService clock) → offer created (DriverService clock, same host).
///  - Publish confirm (RabbitMQ only): RideService's BasicPublish → broker confirm (reported metric).
/// </summary>
public sealed class ExperimentARunner(
    IHttpClientFactory httpClientFactory,
    RabbitMqInspector rabbit,
    AnalyticsDbContext db,
    ILogger<ExperimentARunner> logger)
{
    public const string RideClient = "ride";
    public const string DriverClient = "driver";
    public const string MatchingExperimentClient = "matching-experiment";
    private const string Queue = RabbitMqTopology.Queues.MatchingExperiment;

    private sealed record SendResult(int Index, double SentAtMs, double HttpMs, int Status, Guid? RideId, Guid PassengerId, string? Reason);
    private sealed record RideView(Guid Id, Guid CorrelationId, Guid PassengerId, string Status, DateTimeOffset RequestedAt,
        DateTimeOffset? OfferCreatedAt, string? StatusReason);
    private sealed record OfferCount(Guid RideId, int Offers);

    public async Task RunAsync(Guid runId, string approach, ExperimentARequest p, Action<string> phase, CancellationToken ct)
    {
        var run = await db.ExperimentRuns.FirstAsync(r => r.Id == runId, ct);
        var mode = approach == Mechanisms.Rest ? "Rest" : "RabbitMq";
        var rideHttp = httpClientFactory.CreateClient(RideClient);
        var driverHttp = httpClientFactory.CreateClient(DriverClient);
        var matchingHttp = httpClientFactory.CreateClient(MatchingExperimentClient);
        var timeline = new List<string>();
        var clock = Stopwatch.StartNew();
        var runStart = DateTimeOffset.UtcNow;
        void Log(string text) { timeline.Add($"t+{clock.Elapsed.TotalSeconds:F1}s {text}"); phase(text); }

        var sends = new List<SendResult>();
        var samples = new List<QueueSample>();
        double? recoveredAtMs = null;
        RabbitMqInspector.DurabilityCheck? durability = null;
        uint? consumersDuringOutage = null;

        try
        {
            var external = p.Scenario == ScenarioA.Unavailable && p.OutageMode == "external";
            (await driverHttp.PostAsync("/api/experiments/drivers/reset", null, ct)).EnsureSuccessStatusCode();
            if (!external)
            {
                await SetOutageAsync(matchingHttp, false, ct);
                (await matchingHttp.DeleteAsync("/api/experiments/stats", ct)).EnsureSuccessStatusCode();
            }
            Log($"Experiment driver pool reset; approach {approach}, scenario {p.Scenario}");

            using var samplerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sampler = SampleQueueAsync(samples, clock, samplerCts.Token);

            if (p.Scenario == ScenarioA.Unavailable)
            {
                if (external) Log("matching-experiment instance is stopped externally (real process outage)");
                else
                {
                    await SetOutageAsync(matchingHttp, true, ct);
                    Log("matching-experiment instance made unavailable (REST → 503, RabbitMQ consumer cancelled)");
                }
                consumersDuringOutage = (await WaitForConsumersAsync(0, ct))?.Consumers;
            }

            // Open workload: requests are sent at a fixed interval, independent of the responses.
            var delay = p.Scenario == ScenarioA.Slow ? p.SlowDelayMs : 0;
            var tasks = new List<Task<SendResult>>();
            for (var i = 0; i < p.Rides; i++)
            {
                tasks.Add(SendRideAsync(rideHttp, runId, mode, i, delay, p.RestTimeoutMs, clock, ct));
                if (i < p.Rides - 1) await Task.Delay(p.IntervalMs, ct);
            }
            Log($"{p.Rides} ride requests sent (every {p.IntervalMs} ms{(delay > 0 ? $", simulated matching delay {delay} ms" : "")})");

            if (p.Scenario == ScenarioA.Unavailable)
            {
                await Task.WhenAll(tasks); // let all requests finish during the outage
                if (approach == Mechanisms.RabbitMq) durability = await rabbit.CheckDurabilityAsync(Queue, ct);
                if (external)
                {
                    recoveredAtMs = await WaitForHealthyAsync(matchingHttp, clock, TimeSpan.FromSeconds(p.OutageSeconds + 90), ct);
                    Log(recoveredAtMs is null ? "matching-experiment did not come back within the waiting time" : "matching-experiment process is running again");
                }
                else
                {
                    var remaining = p.OutageSeconds * 1000 - clock.Elapsed.TotalMilliseconds;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), ct);
                    await SetOutageAsync(matchingHttp, false, ct);
                    recoveredAtMs = clock.Elapsed.TotalMilliseconds;
                    Log("matching-experiment instance available again");
                }
            }

            sends.AddRange(await Task.WhenAll(tasks));
            Log($"All responses received: {sends.Count(s => s.Status is >= 200 and < 300)} accepted, {sends.Count(s => s.Status is < 200 or >= 300)} failed");

            // Wait for the business outcome (or the timeout). For failed synchronous requests also wait a little,
            // to observe whether MatchingService still created a (late) offer after the client gave up.
            var rides = await WaitForOutcomesAsync(rideHttp, runId, sends, delay, p.CompletionTimeoutSeconds, clock, ct);
            samplerCts.Cancel();
            await sampler;
            Log("Outcomes collected");

            var rideIds = sends.Where(s => s.RideId is not null).Select(s => s.RideId!.Value).ToList();
            var offerCounts = rideIds.Count == 0 ? [] : await (await driverHttp.PostAsJsonAsync("/api/experiments/offers/count", new { rideIds }, ct))
                .Content.ReadFromJsonAsync<List<OfferCount>>(Json.Options, ct) ?? [];

            await Task.Delay(2500, ct); // let RideService flush its publish-confirm metrics
            var correlationIds = rides.Values.Select(r => (Guid?)r.CorrelationId).ToList();
            var publishConfirms = await db.Measurements.AsNoTracking()
                .Where(m => m.RunId == null && m.Kind == MetricKinds.PublishConfirm && m.Operation == EventTypes.ExperimentRideRequested
                            && correlationIds.Contains(m.CorrelationId))
                .ToDictionaryAsync(m => m.CorrelationId!.Value, m => m.DurationMs, ct);

            // Matching operations that MatchingService finished, per outcome (includes work finished after a client timeout).
            // After a real restart these in-memory counters start from zero again.
            var matchingWork = await matchingHttp.GetFromJsonAsync<Dictionary<string, int>>("/api/experiments/stats", ct) ?? [];
            if (external) matchingWork.Clear();

            var results = BuildRides(sends, rides, offerCounts, publishConfirms, runStart);
            Complete(run, approach, p, results, samples, recoveredAtMs, durability, consumersDuringOutage, matchingWork, timeline);
            await StoreSamplesAsync(run, approach, results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Experiment A run {RunId} failed", runId);
            run.Status = "Failed";
            run.Error = ex.Message;
            run.DetailsJson = JsonSerializer.Serialize(new { timeline }, Json.Options);
        }
        finally
        {
            // Clean up first, so a run only reports its final status once the experiment resources are free again.
            await CleanupAsync(matchingHttp, rideHttp, driverHttp, sends);
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<SendResult> SendRideAsync(HttpClient http, Guid runId, string mode, int index, int delayMs, int timeoutMs, Stopwatch clock, CancellationToken ct)
    {
        var passengerId = Guid.NewGuid();
        var sentAt = clock.Elapsed.TotalMilliseconds;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await http.PostAsJsonAsync("/api/rides", new
            {
                passengerId,
                passengerName = $"Experiment A #{index + 1}",
                pickup = "Experiment pickup",
                destination = "Experiment destination",
                matchingMode = mode,
                experimentRunId = runId,
                simulatedMatchingDelayMs = delayMs,
                matchingTimeoutMs = mode == "Rest" ? timeoutMs : (int?)null,
            }, Json.Options, ct);
            var httpMs = stopwatch.Elapsed.TotalMilliseconds;
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            Guid? rideId = body.TryGetProperty("id", out var id) ? id.GetGuid()
                : body.TryGetProperty("rideId", out var rid) && rid.ValueKind == JsonValueKind.String ? rid.GetGuid() : null;
            string? reason = body.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()
                : response.IsSuccessStatusCode ? null : body.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return new SendResult(index, sentAt, httpMs, (int)response.StatusCode, rideId, passengerId, reason);
        }
        catch (HttpRequestException ex)
        {
            return new SendResult(index, sentAt, stopwatch.Elapsed.TotalMilliseconds, 0, null, passengerId, ex.Message);
        }
    }

    private async Task<Dictionary<Guid, RideView>> WaitForOutcomesAsync(HttpClient http, Guid runId, List<SendResult> sends, int delayMs,
        int timeoutSeconds, Stopwatch clock, CancellationToken ct)
    {
        var deadline = clock.Elapsed + TimeSpan.FromSeconds(timeoutSeconds);
        var failedNeedObservation = sends.Any(s => s.Status is < 200 or >= 300 && s.RideId is not null);
        var observeUntil = clock.Elapsed + TimeSpan.FromMilliseconds(failedNeedObservation ? delayMs + 3000 : 0);
        Dictionary<Guid, RideView> rides = [];
        while (true)
        {
            rides = (await http.GetFromJsonAsync<List<RideView>>($"/api/rides?experimentRunId={runId}&limit=200", Json.Options, ct) ?? [])
                .ToDictionary(r => r.Id);
            var unresolved = sends.Count(s => s.Status is >= 200 and < 300 && s.RideId is { } id
                && rides.TryGetValue(id, out var ride) && ride.OfferCreatedAt is null && ride.Status == "Requested");
            if ((unresolved == 0 && clock.Elapsed >= observeUntil) || clock.Elapsed >= deadline) return rides;
            await Task.Delay(250, ct);
        }
    }

    private static List<ExperimentARide> BuildRides(List<SendResult> sends, Dictionary<Guid, RideView> rides, List<OfferCount> offerCounts,
        Dictionary<Guid, double> publishConfirms, DateTimeOffset runStart) =>
        sends.OrderBy(s => s.Index).Select(s =>
        {
            var accepted = s.Status is >= 200 and < 300;
            RideView? ride = s.RideId is { } id && rides.TryGetValue(id, out var r) ? r : null;
            var offer = ride?.OfferCreatedAt;
            return new ExperimentARide(
                Index: s.Index + 1,
                SentAtMs: Math.Round(s.SentAtMs, 1),
                HttpMs: Math.Round(s.HttpMs, 2),
                HttpStatus: s.Status,
                Accepted: accepted,
                RideId: s.RideId,
                CorrelationId: ride?.CorrelationId,
                FinalStatus: ride?.Status,
                Outcome: ClassifyRide(accepted, ride?.Status, offer is not null),
                TimeToOfferMs: offer is { } o ? Math.Round((o - ride!.RequestedAt).TotalMilliseconds, 1) : null,
                OfferAtMs: offer is { } at ? Math.Round((at - runStart).TotalMilliseconds, 1) : null,
                PublishConfirmMs: ride is not null && publishConfirms.TryGetValue(ride.CorrelationId, out var pc) ? pc : null,
                Offers: s.RideId is { } rid2 ? offerCounts.FirstOrDefault(c => c.RideId == rid2)?.Offers ?? 0 : 0,
                Reason: s.Reason ?? ride?.StatusReason);
        }).ToList();

    private static void Complete(ExperimentRun run, string approach, ExperimentARequest p, List<ExperimentARide> rides, List<QueueSample> samples,
        double? recoveredAtMs, RabbitMqInspector.DurabilityCheck? durability, uint? consumersDuringOutage, Dictionary<string, int> matchingWork, List<string> timeline)
    {
        int Count(string outcome) => rides.Count(r => r.Outcome == outcome);
        var accepted = rides.Count(r => r.Accepted);
        var offerTimes = rides.Where(r => r.Outcome == "offer-created").Select(r => r.TimeToOfferMs!.Value).ToList();
        var http = rides.Select(r => r.HttpMs).ToList();
        var timeouts = rides.Count(r => r.Reason?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true);
        var duplicates = rides.Count(r => r.Offers > 1);
        var maxDepth = samples.Count == 0 ? 0 : samples.Max(s => s.Messages);
        var lastOfferAfterRecovery = recoveredAtMs is { } rec && rides.Any(r => r.OfferAtMs is not null)
            ? rides.Where(r => r.OfferAtMs is not null).Max(r => r.OfferAtMs!.Value) - rec : (double?)null;
        var confirms = rides.Where(r => r.PublishConfirmMs is not null).Select(r => r.PublishConfirmMs!.Value).ToList();

        var results = new Dictionary<string, double?>
        {
            ["requests"] = rides.Count,
            ["accepted"] = accepted,
            ["failedRequests"] = rides.Count - accepted,
            ["timeouts"] = timeouts,
            ["offersCreated"] = Count("offer-created"),
            ["noDriver"] = Count("no-driver"),
            ["pendingAtEnd"] = Count("pending"),
            ["lateOffers"] = Count("late-offer"),
            ["ridesWithDuplicateOffers"] = duplicates,
            ["completionRate"] = rides.Count == 0 ? null : Math.Round((double)Count("offer-created") / rides.Count, 3),
            ["httpP50Ms"] = P(http, 50),
            ["httpP95Ms"] = P(http, 95),
            ["httpMaxMs"] = Max(http),
            ["timeToOfferP50Ms"] = P(offerTimes, 50),
            ["timeToOfferP95Ms"] = P(offerTimes, 95),
            ["timeToOfferMaxMs"] = Max(offerTimes),
            ["publishConfirmP50Ms"] = P(confirms, 50),
            ["maxQueueDepth"] = maxDepth,
            ["consumersDuringOutage"] = consumersDuringOutage,
            ["lastOfferAfterRecoveryMs"] = lastOfferAfterRecovery is null ? null : Math.Round(lastOfferAfterRecovery.Value, 1),
            ["queueDurable"] = durability?.QueueDurable is { } d ? (d ? 1 : 0) : null,
            ["messageDeliveryMode"] = durability?.MessageDeliveryMode,
            ["matchingFinishedOfferCreated"] = matchingWork.GetValueOrDefault("OfferCreated"),
            ["matchingFinishedRideClosed"] = matchingWork.GetValueOrDefault("RideClosed"),
            ["matchingFinishedNoDriver"] = matchingWork.GetValueOrDefault("NoDriverAvailable"),
            ["matchingAbortedByCaller"] = matchingWork.GetValueOrDefault("Aborted"),
        };

        var observations = new List<string>
        {
            $"{accepted}/{rides.Count} ride requests were accepted by RideService (HTTP 2xx); {rides.Count - accepted} failed" +
                (rides.Count - accepted > 0 ? $" ({string.Join(", ", rides.Where(r => !r.Accepted).GroupBy(r => r.HttpStatus).Select(g => $"{g.Count()}× HTTP {g.Key}"))})." : "."),
            $"Passenger-facing HTTP response time: p50 {Ms(P(http, 50))}, p95 {Ms(P(http, 95))}, max {Ms(Max(http))}.",
            $"{Count("offer-created")}/{rides.Count} rides reached the business outcome (ride offer created)" +
                (offerTimes.Count > 0 ? $"; request → offer p50 {Ms(P(offerTimes, 50))}, p95 {Ms(P(offerTimes, 95))}, max {Ms(Max(offerTimes))}." : "."),
        };
        if (Count("pending") > 0) observations.Add($"{Count("pending")} accepted rides were still waiting for matching when the run ended (pending asynchronous work).");
        if (timeouts > 0) observations.Add($"{timeouts} synchronous requests timed out after {p.RestTimeoutMs} ms; RideService cancelled those rides and returned HTTP 503.");
        var wasted = matchingWork.GetValueOrDefault("RideClosed");
        if (wasted > 0)
            observations.Add($"MatchingService still finished {wasted} matching operations after the ride had been cancelled (e.g. after the caller's timeout); DriverService refused those offers. This is work the passenger never benefited from.");
        var aborted = matchingWork.GetValueOrDefault("Aborted");
        if (aborted > 0)
            observations.Add($"{aborted} matching operations were cancelled inside MatchingService because RideService closed the HTTP connection at its timeout (ASP.NET Core propagates the abort via RequestAborted). Without such propagation, the work would continue after the caller had given up.");
        if (Count("late-offer") > 0) observations.Add($"{Count("late-offer")} rides whose request had already failed still received an offer afterwards (work done after the client gave up; the cancellation withdrew it).");
        observations.Add(duplicates == 0 ? "No ride received more than one offer (no duplicate processing)." : $"{duplicates} rides received more than one offer.");
        if (approach == Mechanisms.RabbitMq)
        {
            observations.Add($"Maximum number of messages waiting in queue \"{Queue}\": {maxDepth}.");
            if (consumersDuringOutage is not null) observations.Add($"Consumers on \"{Queue}\" during the outage: {consumersDuringOutage}.");
            if (durability is not null)
                observations.Add($"Durability check during the outage: queue durable = {Fmt(durability.QueueDurable)}, waiting message delivery_mode = {durability.MessageDeliveryMode?.ToString() ?? "n/a"} (2 = persistent). {durability.Note}");
        }
        var failedNotProcessed = rides.Count(r => !r.Accepted && r.Outcome == "failed");
        if (recoveredAtMs is not null && failedNotProcessed > 0)
            observations.Add($"None of the {failedNotProcessed} failed requests was processed after the matching instance recovered: a failed synchronous request is not stored for later, so the passenger (or RideService) must send it again.");
        if (lastOfferAfterRecovery is not null) observations.Add($"The last offer was created {Ms(lastOfferAfterRecovery)} after the matching instance became available again.");

        run.Status = Count("pending") > 0 ? "Incomplete" : "Completed";
        run.Count = rides.Count;
        run.Errors = rides.Count - Count("offer-created") - Count("no-driver");
        run.AvgMs = offerTimes.Count == 0 ? null : Math.Round(offerTimes.Average(), 2);
        run.P50Ms = P(offerTimes, 50);
        run.P95Ms = P(offerTimes, 95);
        run.P99Ms = P(offerTimes, 99);
        run.MaxMs = Max(offerTimes);
        run.ResultsJson = JsonSerializer.Serialize(results, Json.Options);
        run.ObservationsJson = JsonSerializer.Serialize(observations, Json.Options);
        run.DetailsJson = JsonSerializer.Serialize(new { rides, queueSamples = samples, timeline }, Json.Options);
    }

    private static string Fmt(bool? value) => value is null ? "unknown" : value.Value ? "true" : "false";

    private async Task StoreSamplesAsync(ExperimentRun run, string approach, List<ExperimentARide> rides)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var r in rides)
        {
            db.Measurements.Add(new Measurement { RunId = run.Id, Mechanism = approach, Kind = "http-response", Operation = "POST /api/rides",
                DurationMs = r.HttpMs, Success = r.Accepted, RecordedAt = now, Source = "experiment-a", CorrelationId = r.CorrelationId, Error = r.Accepted ? null : r.Reason });
            if (r.TimeToOfferMs is { } t)
                db.Measurements.Add(new Measurement { RunId = run.Id, Mechanism = approach, Kind = "time-to-offer", Operation = "ride request → offer created",
                    DurationMs = t, Success = r.Accepted, RecordedAt = now, Source = "experiment-a", CorrelationId = r.CorrelationId });
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task SampleQueueAsync(List<QueueSample> samples, Stopwatch clock, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await rabbit.GetQueueStateAsync(Queue, ct) is { } state)
                    lock (samples) samples.Add(new QueueSample(Math.Round(clock.Elapsed.TotalMilliseconds), state.Messages, state.Consumers));
                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Queue sampling failed: {Message}", ex.Message);
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task<(uint Messages, uint Consumers)?> WaitForConsumersAsync(uint expected, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var state = await rabbit.GetQueueStateAsync(Queue, ct);
            if (state?.Consumers == expected) return state;
            await Task.Delay(100, ct);
        }
        return await rabbit.GetQueueStateAsync(Queue, ct);
    }

    /// <summary>External outage: poll the instance's /health until it answers again; returns the time (ms since start).</summary>
    private static async Task<double?> WaitForHealthyAsync(HttpClient matching, Stopwatch clock, TimeSpan timeout, CancellationToken ct)
    {
        var until = clock.Elapsed + timeout;
        while (clock.Elapsed < until)
        {
            try
            {
                using var response = await matching.GetAsync("/health", ct);
                if (response.IsSuccessStatusCode) return clock.Elapsed.TotalMilliseconds;
            }
            catch (HttpRequestException) { /* still down */ }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { /* still down */ }
            await Task.Delay(250, ct);
        }
        return null;
    }

    private static async Task SetOutageAsync(HttpClient matching, bool enabled, CancellationToken ct)
    {
        using var response = await matching.PutAsJsonAsync("/api/experiments/simulated-outage", new { enabled }, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Cancel experiment rides that are still active, so experiment drivers are released.</summary>
    private async Task CleanupAsync(HttpClient matching, HttpClient rideHttp, HttpClient driverHttp, List<SendResult> sends)
    {
        try { await SetOutageAsync(matching, false, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning("Could not reset simulated outage: {Message}", ex.Message); }
        foreach (var s in sends.Where(s => s.RideId is not null))
        {
            try
            {
                await rideHttp.PostAsJsonAsync($"/api/rides/{s.RideId}/cancel", new { passengerId = s.PassengerId, reason = "Experiment cleanup" });
            }
            catch (Exception ex) { logger.LogDebug("Cleanup cancel failed: {Message}", ex.Message); }
        }
        try { await driverHttp.PostAsync("/api/experiments/drivers/reset", null); } catch { /* best effort */ }
    }
}
