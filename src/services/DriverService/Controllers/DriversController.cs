using DriverService.Data;
using DriverService.Models;
using DriverService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;

namespace DriverService.Controllers;

[ApiController]
[Route("api/drivers")]
public class DriversController(DriverDbContext db, DriverAvailabilityQuery availabilityQuery) : ControllerBase
{
    /// <summary>Demo drivers (the experiment pool is hidden unless includeExperiment=true).</summary>
    [HttpGet]
    public async Task<IEnumerable<DriverDto>> GetDrivers(bool includeExperiment = false, CancellationToken ct = default) =>
        (await db.Drivers.AsNoTracking().Where(d => includeExperiment || !d.IsExperimentDriver).OrderBy(d => d.Name).ToListAsync(ct)).Select(DriverDto.From);

    [HttpGet("{driverId:guid}")]
    public async Task<DriverDto> GetDriver(Guid driverId, CancellationToken ct) =>
        DriverDto.From(await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, ct)
            ?? throw new NotFoundException("Driver not found."));

    /// <summary>Driver goes online (available) or offline.</summary>
    [HttpPut("{driverId:guid}/availability")]
    public async Task<DriverDto> SetAvailability(Guid driverId, SetAvailabilityRequest request, CancellationToken ct)
    {
        var driver = await db.Drivers.FindAsync([driverId], ct) ?? throw new NotFoundException("Driver not found.");
        driver.SetAvailability(request.IsAvailable);
        await db.SaveChangesAsync(ct);
        return DriverDto.From(driver);
    }

    /// <summary>
    /// REST version of the driver-availability lookup used by MatchingService.
    /// gRPC equivalent: DriverAvailability.GetAvailableDrivers (same query, see DriverAvailabilityQuery).
    /// </summary>
    [HttpGet("available")]
    public Task<List<AvailableDriverDto>> GetAvailable([FromQuery] Guid[]? exclude, [FromQuery] int max = 5, [FromQuery] string? pool = null, CancellationToken ct = default) =>
        availabilityQuery.GetAvailableDriversAsync(exclude ?? [], max, ct, experimentPool: pool == "experiment");

    /// <summary>Offers of a driver, e.g. ?status=Pending for the incoming-requests screen.</summary>
    [HttpGet("{driverId:guid}/offers")]
    public async Task<IEnumerable<OfferDto>> GetOffers(Guid driverId, OfferStatus? status, CancellationToken ct)
    {
        var query = db.Offers.AsNoTracking().Where(o => o.DriverId == driverId);
        if (status is not null) query = query.Where(o => o.Status == status);
        return (await query.OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(ct)).Select(OfferDto.From);
    }
}
