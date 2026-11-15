using DriverService.Data;
using DriverService.Models;
using Microsoft.EntityFrameworkCore;

namespace DriverService.Services;

/// <summary>"Who can take a ride?" — used by the gRPC GetAvailableDrivers call from MatchingService.</summary>
public class DriverAvailabilityQuery(DriverDbContext db)
{
    /// <summary>Available drivers, longest-waiting first (fair), excluding drivers already tried for this ride.</summary>
    public async Task<List<AvailableDriverDto>> GetAvailableAsync(IReadOnlyCollection<Guid> exclude, int maxResults, CancellationToken ct) =>
        await db.Drivers.AsNoTracking()
            .Where(d => d.Status == DriverStatus.Available && !exclude.Contains(d.Id))
            .OrderBy(d => d.AvailableSince)
            .Take(Math.Clamp(maxResults, 1, 50))
            .Select(d => new AvailableDriverDto(d.Id, d.Name, d.Vehicle, d.LicensePlate, d.Rating))
            .ToListAsync(ct);
}
