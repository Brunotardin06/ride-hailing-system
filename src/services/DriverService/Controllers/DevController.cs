using DriverService.Data;
using DriverService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriverService.Controllers;

/// <summary>Development helpers for demos and automated tests. Only mapped in the Development environment.</summary>
[ApiController]
[Route("api/dev")]
public class DevController(DriverDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Puts all drivers back to their seed availability and withdraws pending offers.</summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();

        // A message handler may update a driver at the same moment (optimistic concurrency) → retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ResetOnceAsync(ct);
                return NoContent();
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                db.ChangeTracker.Clear();
                await Task.Delay(100 * attempt, ct);
            }
        }
    }

    private async Task ResetOnceAsync(CancellationToken ct)
    {
        var seed = DemoDrivers.All.ToDictionary(d => d.Id);
        foreach (var driver in await db.Drivers.ToListAsync(ct))
        {
            if (seed.TryGetValue(driver.Id, out var s))
                driver.ResetForDemo(s.Status, s.Status == DriverStatus.Available ? DateTimeOffset.UtcNow.AddSeconds(seed.Keys.ToList().IndexOf(driver.Id)) : null);
        }
        foreach (var offer in await db.Offers.Where(o => o.Status == OfferStatus.Pending && !o.IsExperiment).ToListAsync(ct))
            offer.Respond(OfferStatus.Cancelled);

        await db.SaveChangesAsync(ct);
    }
}
