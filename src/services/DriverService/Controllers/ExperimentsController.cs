using DriverService.Data;
using DriverService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriverService.Controllers;

/// <summary>
/// Support endpoints for Experiment A (used by AnalyticsService's experiment runner).
/// They only touch the hidden experiment driver pool, never the demo drivers.
/// </summary>
[ApiController]
[Route("api/experiments")]
public class ExperimentsController(DriverDbContext db) : ControllerBase
{
    /// <summary>Makes every experiment driver available again and withdraws pending experiment offers.</summary>
    [HttpPost("drivers/reset")]
    public async Task<IActionResult> ResetExperimentPool(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                foreach (var driver in await db.Drivers.Where(d => d.IsExperimentDriver).ToListAsync(ct))
                    driver.ResetForDemo(DriverStatus.Available, DateTimeOffset.UtcNow);
                foreach (var offer in await db.Offers.Where(o => o.IsExperiment && o.Status == OfferStatus.Pending).ToListAsync(ct))
                    offer.Respond(OfferStatus.Cancelled);
                await db.SaveChangesAsync(ct);
                return NoContent();
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                db.ChangeTracker.Clear();
                await Task.Delay(100 * attempt, ct);
            }
        }
    }

    public sealed record OfferCountRequest(List<Guid> RideIds);
    public sealed record OfferCount(Guid RideId, int Offers);

    /// <summary>Number of offers created per ride: more than one for a ride means duplicate processing.</summary>
    [HttpPost("offers/count")]
    public async Task<List<OfferCount>> CountOffers(OfferCountRequest request, CancellationToken ct)
    {
        var counts = await db.Offers.Where(o => request.RideIds.Contains(o.RideId))
            .GroupBy(o => o.RideId)
            .Select(g => new OfferCount(g.Key, g.Count()))
            .ToListAsync(ct);
        return request.RideIds.Select(id => counts.FirstOrDefault(c => c.RideId == id) ?? new OfferCount(id, 0)).ToList();
    }
}
