using DriverService.Data;
using DriverService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;

namespace DriverService.Services;

/// <summary>Driver business logic used by DriversController (list, get, go online/offline).</summary>
public class DriverManager(DriverDbContext db)
{
    public async Task<List<Driver>> GetAllAsync(CancellationToken ct) =>
        await db.Drivers.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);

    public async Task<Driver> GetAsync(Guid driverId, CancellationToken ct) =>
        await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, ct)
        ?? throw new NotFoundException("Driver not found.");

    public async Task<Driver> SetAvailabilityAsync(Guid driverId, bool isAvailable, CancellationToken ct)
    {
        // No AsNoTracking here: EF must track the entity to detect the change and generate the UPDATE.
        var driver = await db.Drivers.FindAsync([driverId], ct) ?? throw new NotFoundException("Driver not found.");
        driver.SetAvailability(isAvailable); // rule check lives in the entity
        await db.SaveChangesAsync(ct);
        return driver;
    }
}
