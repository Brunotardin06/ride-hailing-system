using DriverService.Data;
using DriverService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriverService.Controllers;

/// <summary>Development helpers (demo reset). Return 404 outside Development.</summary>
[ApiController]
[Route("api/dev")]
public class DevController(DriverDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    // POST /api/dev/reset  – "Reset demo" button: drivers back to seed state, pending offers cancelled.
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();

        var seed = DemoDrivers.All.ToDictionary(d => d.Id);
        var order = 0;
        foreach (var driver in await db.Drivers.OrderBy(d => d.Id).ToListAsync(ct))
        {
            if (!seed.TryGetValue(driver.Id, out var s)) continue;
            var availableSince = s.Status == DriverStatus.Available ? DateTimeOffset.UtcNow.AddSeconds(order++) : (DateTimeOffset?)null;
            driver.ResetForDemo(s.Status, availableSince);
        }

        foreach (var offer in await db.Offers.Where(o => o.Status == OfferStatus.Pending).ToListAsync(ct))
            offer.Respond(OfferStatus.Cancelled);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
