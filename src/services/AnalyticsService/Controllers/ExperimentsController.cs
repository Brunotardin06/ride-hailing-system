using AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;
using RideHailing.Shared.Metrics;

namespace AnalyticsService.Controllers;

/// <summary>Responsibility B — results of repeatable experiment runs.</summary>
[ApiController]
[Route("api/experiments/runs")]
public class ExperimentsController(ExperimentRunService runs) : ControllerBase
{
    /// <summary>Runs, newest first. Filter by experimentType (A, B, tool), scenarioKey or groupId; includeDetails adds per-ride/per-consumer data.</summary>
    [HttpGet]
    public Task<List<RunSummaryDto>> List(string? experimentType, string? scenarioKey, Guid? groupId, bool includeDetails = false, CancellationToken ct = default) =>
        runs.ListAsync(experimentType, scenarioKey, groupId, includeDetails, ct);

    public sealed record NotesRequest(string? QualitativeNotes);

    /// <summary>Save qualitative observations for a run.</summary>
    [HttpPut("{id:guid}/notes")]
    public Task<RunSummaryDto> UpdateNotes(Guid id, NotesRequest request, CancellationToken ct) => runs.UpdateNotesAsync(id, request.QualitativeNotes, ct);

    [HttpGet("{id:guid}")]
    public Task<RunSummaryDto> Get(Guid id, CancellationToken ct) => runs.GetAsync(id, ct);

    [HttpGet("{id:guid}/samples")]
    public Task<List<double>> GetSamples(Guid id, CancellationToken ct) => runs.GetSampleDurationsAsync(id, ct);

    /// <summary>Submit a run with its raw samples (used by MatchingService's REST/gRPC benchmark).</summary>
    [HttpPost]
    public Task<RunSummaryDto> Create(CreateRunRequest request, CancellationToken ct) => runs.CreateAsync(request, ct);

    /// <summary>Import a k6 summary (k6 run --summary-export=file.json).</summary>
    [HttpPost("import/k6")]
    public Task<RunSummaryDto> ImportK6(K6ImportRequest request, CancellationToken ct) => runs.ImportK6Async(request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await runs.DeleteAsync(id, ct);
        return NoContent();
    }
}
