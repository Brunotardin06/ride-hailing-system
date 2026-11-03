using AnalyticsService.Data;
using AnalyticsService.Experiments;
using AnalyticsService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;

namespace AnalyticsService.Controllers;

/// <summary>
/// Scenario-based experiments. A run executes in the background; poll GET /api/experiments/runs?groupId=…
/// (or /api/experiments/active) for progress and results.
/// </summary>
[ApiController]
[Route("api/experiments")]
public class ScenarioExperimentsController(ExperimentCoordinator coordinator) : ControllerBase
{
    /// <summary>Experiment A: REST vs RabbitMQ for ride matching (scenario A1-normal, A2-unavailable, A3-slow).</summary>
    [HttpPost("a")]
    public async Task<ActionResult<ExperimentStarted>> StartA(ExperimentARequest request) =>
        Accepted(await coordinator.StartAAsync(request));

    /// <summary>Experiment B: RabbitMQ vs Kafka for event distribution (B1-single, B2-multiple, B3-offline, B4-replay).</summary>
    [HttpPost("b")]
    public async Task<ActionResult<ExperimentStarted>> StartB(ExperimentBRequest request) =>
        Accepted(await coordinator.StartBAsync(request));

    /// <summary>The experiment that is currently running (204 if none).</summary>
    [HttpGet("active")]
    public ActionResult<ActiveExperimentDto> Active() => coordinator.Active is { } active ? active : NoContent();
}

/// <summary>Experiment C: structured findings per scenario and trade-off matrices.</summary>
[ApiController]
[Route("api")]
public class FindingsController(AnalyticsDbContext db) : ControllerBase
{
    [HttpGet("findings")]
    public async Task<List<ScenarioFinding>> GetFindings(CancellationToken ct) =>
        await db.ScenarioFindings.AsNoTracking().OrderBy(f => f.Key).ToListAsync(ct);

    public sealed record UpdateFindingRequest(
        string? Observations, string? Advantages, string? Disadvantages, string? Recommendation,
        string? Limitations, string? Hypothesis, string? Requirements, string? EvidenceStatus);

    private static readonly string[] EvidenceStatuses = ["NotTested", "Hypothesis", "Supported", "PartiallySupported", "Contradicted"];

    /// <summary>Save the researcher's analysis of a scenario (only the fields that are sent are changed).</summary>
    [HttpPut("findings/{key}")]
    public async Task<ScenarioFinding> UpdateFinding(string key, UpdateFindingRequest request, CancellationToken ct)
    {
        var f = await db.ScenarioFindings.FindAsync([key], ct) ?? throw new NotFoundException($"Scenario {key} not found.");
        if (request.EvidenceStatus is { } status && !EvidenceStatuses.Contains(status))
            throw new ArgumentException($"EvidenceStatus must be one of {string.Join(", ", EvidenceStatuses)}.");
        f.Observations = request.Observations ?? f.Observations;
        f.Advantages = request.Advantages ?? f.Advantages;
        f.Disadvantages = request.Disadvantages ?? f.Disadvantages;
        f.Recommendation = request.Recommendation ?? f.Recommendation;
        f.Limitations = request.Limitations ?? f.Limitations;
        f.Hypothesis = request.Hypothesis ?? f.Hypothesis;
        f.Requirements = request.Requirements ?? f.Requirements;
        f.EvidenceStatus = request.EvidenceStatus ?? f.EvidenceStatus;
        f.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return f;
    }

    public sealed record TradeOffMatrix(string Experiment, string[] Approaches, Findings.Criterion[] Criteria, List<TradeOffNote> Notes);

    /// <summary>Criteria, approaches and the researcher's notes for the trade-off matrices of Experiments A and B.</summary>
    [HttpGet("tradeoffs")]
    public async Task<List<TradeOffMatrix>> GetTradeOffs(CancellationToken ct)
    {
        var notes = await db.TradeOffNotes.AsNoTracking().ToListAsync(ct);
        return Findings.Approaches.Select(a => new TradeOffMatrix(a.Key, a.Value, Findings.Criteria,
            notes.Where(n => n.Experiment == a.Key).ToList())).ToList();
    }

    public sealed record UpdateTradeOffRequest(string Experiment, string Criterion, string Approach, string Text, string EvidenceType);

    [HttpPut("tradeoffs")]
    public async Task<TradeOffNote> UpdateTradeOff(UpdateTradeOffRequest request, CancellationToken ct)
    {
        if (!Findings.Approaches.TryGetValue(request.Experiment, out var approaches) || !approaches.Contains(request.Approach)
            || Findings.Criteria.All(c => c.Key != request.Criterion))
            throw new ArgumentException("Unknown experiment, approach or criterion.");
        if (request.EvidenceType is not ("Measured" or "Expected" or "NotTested"))
            throw new ArgumentException("EvidenceType must be Measured, Expected or NotTested.");

        var note = await db.TradeOffNotes.FindAsync([request.Experiment, request.Criterion, request.Approach], ct);
        if (note is null)
        {
            note = new TradeOffNote { Experiment = request.Experiment, Criterion = request.Criterion, Approach = request.Approach };
            db.TradeOffNotes.Add(note);
        }
        note.Text = request.Text.Trim();
        note.EvidenceType = request.EvidenceType;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return note;
    }
}
