using System.Text.Json;
using AnalyticsService.Data;
using AnalyticsService.Models;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace AnalyticsService.Experiments;

/// <summary>
/// Starts scenario experiments in the background, one at a time (experiments share the experiment
/// resources and would disturb each other's measurements). The dashboard polls the run records.
/// </summary>
public sealed class ExperimentCoordinator(IServiceScopeFactory scopeFactory, ILogger<ExperimentCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveExperimentDto? _active;

    public ActiveExperimentDto? Active => _active;

    public Task<ExperimentStarted> StartAAsync(ExperimentARequest request)
    {
        if (!ScenarioA.All.Contains(request.Scenario)) throw new ArgumentException($"Unknown scenario {request.Scenario}.");
        var approaches = Normalize(request.Approaches, [Mechanisms.Rest, Mechanisms.RabbitMq]);
        var p = request with
        {
            Rides = Math.Clamp(request.Rides, 1, 20),
            IntervalMs = Math.Clamp(request.IntervalMs, 0, 5000),
            SlowDelayMs = Math.Clamp(request.SlowDelayMs, 100, 30000),
            RestTimeoutMs = Math.Clamp(request.RestTimeoutMs, 200, 30000),
            OutageSeconds = Math.Clamp(request.OutageSeconds, 2, 120),
            CompletionTimeoutSeconds = Math.Clamp(request.CompletionTimeoutSeconds, 10, 300),
        };
        var parameters = new Dictionary<string, string>
        {
            ["rides"] = p.Rides.ToString(),
            ["intervalMs"] = p.IntervalMs.ToString(),
            ["restTimeoutMs"] = p.RestTimeoutMs.ToString(),
        };
        if (p.Scenario == ScenarioA.Slow) parameters["simulatedMatchingDelayMs"] = p.SlowDelayMs.ToString();
        if (p.Scenario == ScenarioA.Unavailable)
        {
            parameters["outageSeconds"] = p.OutageSeconds.ToString();
            parameters["outageMode"] = p.OutageMode == "external" ? "external (real process stop)" : "simulated (REST 503 + consumer cancelled)";
        }

        return StartAsync("A", p.Scenario, approaches, parameters,
            approach => ($"Experiment A · {p.Scenario} · {approach}", "ride matching: Ride Service → Matching Service"),
            (sp, runId, approach, phase, ct) => sp.GetRequiredService<ExperimentARunner>().RunAsync(runId, approach, p, phase, ct));
    }

    public Task<ExperimentStarted> StartBAsync(ExperimentBRequest request)
    {
        if (!ScenarioB.All.Contains(request.Scenario)) throw new ArgumentException($"Unknown scenario {request.Scenario}.");
        var mechanisms = Normalize(request.Mechanisms, [Mechanisms.RabbitMq, Mechanisms.Kafka]);
        var p = request with
        {
            Rides = Math.Clamp(request.Rides, 1, 500),
            OfflineSeconds = Math.Clamp(request.OfflineSeconds, 1, 60),
            RabbitQueueMode = request.RabbitQueueMode == "temporary" ? "temporary" : "durable",
        };
        var parameters = new Dictionary<string, string> { ["rides"] = p.Rides.ToString() };
        if (p.Scenario == ScenarioB.Offline)
        {
            parameters["offlineSeconds"] = p.OfflineSeconds.ToString();
            parameters["rabbitQueueMode"] = p.RabbitQueueMode;
        }
        if (p.Scenario == ScenarioB.Multiple) parameters["sharedSubscription"] = p.SharedSubscription.ToString();

        return StartAsync("B", p.Scenario, mechanisms, parameters,
            mechanism => ($"Experiment B · {p.Scenario} · {mechanism}", "ride lifecycle event distribution"),
            (sp, runId, mechanism, phase, ct) => sp.GetRequiredService<ExperimentBRunner>().RunAsync(runId, mechanism, p, phase, ct));
    }

    private static string[] Normalize(string[]? requested, string[] allowed)
    {
        var list = (requested is { Length: > 0 } ? requested : allowed)
            .Select(a => allowed.FirstOrDefault(x => x.Equals(a, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown approach {a}."))
            .Distinct().ToArray();
        return list;
    }

    private async Task<ExperimentStarted> StartAsync(string experiment, string scenario, string[] approaches, Dictionary<string, string> parameters,
        Func<string, (string Name, string Operation)> describe,
        Func<IServiceProvider, Guid, string, Action<string>, CancellationToken, Task> execute)
    {
        if (!await _gate.WaitAsync(0))
            throw new ConflictException($"Another experiment is running ({_active?.Experiment} {_active?.Scenario}). Wait until it has finished.");

        var groupId = Guid.NewGuid();
        var runIds = new List<Guid>();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            var now = DateTimeOffset.UtcNow;
            foreach (var approach in approaches)
            {
                var (name, operation) = describe(approach);
                var run = new ExperimentRun
                {
                    Id = Guid.NewGuid(), Name = name, Mechanism = approach, Kind = "scenario", Operation = operation,
                    Scenario = scenario, Tool = "experiment runner (AnalyticsService)", ExperimentType = experiment, ScenarioKey = scenario,
                    Status = "Queued", GroupId = groupId, StartedAt = now, FinishedAt = now, CreatedAt = now,
                    ParametersJson = JsonSerializer.Serialize(parameters, Json.Options),
                };
                db.ExperimentRuns.Add(run);
                runIds.Add(run.Id);
            }
            await db.SaveChangesAsync();
        }
        catch
        {
            _gate.Release();
            throw;
        }

        _active = new ActiveExperimentDto(experiment, scenario, groupId, DateTimeOffset.UtcNow, "Starting");
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < approaches.Length; i++)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                    var run = await db.ExperimentRuns.FindAsync(runIds[i]);
                    run!.Status = "Running";
                    run.StartedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();

                    var approach = approaches[i];
                    void Phase(string text) => _active = _active! with { Phase = $"{approach}: {text}" };
                    await execute(scope.ServiceProvider, runIds[i], approach, Phase, timeout.Token);
                    if (i < approaches.Length - 1) await Task.Delay(2000); // let cleanup settle before the next approach
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Experiment {Experiment} {Scenario} failed", experiment, scenario);
            }
            finally
            {
                _active = null;
                _gate.Release();
            }
        });

        return new ExperimentStarted(groupId, runIds);
    }
}
