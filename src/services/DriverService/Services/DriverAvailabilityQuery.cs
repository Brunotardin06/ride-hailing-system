using DriverService.Data;
using DriverService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using RideHailing.Grpc;

namespace DriverService.Services;

/// <summary>
/// The driver-availability lookup. The REST endpoint and the gRPC service both call this same method,
/// so a REST/gRPC comparison only differs in the transport, not in the work done.
/// </summary>
public class DriverAvailabilityQuery(DriverDbContext db)
{
    public async Task<List<AvailableDriverDto>> GetAvailableDriversAsync(IReadOnlyCollection<Guid> excludeDriverIds, int maxResults, CancellationToken ct, bool experimentPool = false)
    {
        var drivers = await db.Drivers.AsNoTracking()
            .Where(d => d.Status == DriverStatus.Available && d.IsExperimentDriver == experimentPool && !excludeDriverIds.Contains(d.Id))
            .OrderBy(d => d.AvailableSince)
            .Take(Math.Clamp(maxResults, 1, 50))
            .ToListAsync(ct);
        return drivers.Select(d => new AvailableDriverDto(d.Id, d.Name, d.Vehicle, d.LicensePlate, d.Rating, d.AvailableSince)).ToList();
    }
}

/// <summary>gRPC server implementation (HTTP/2 endpoint, see Kestrel config in appsettings.json).</summary>
public class DriverAvailabilityGrpcService(DriverAvailabilityQuery query) : DriverAvailability.DriverAvailabilityBase
{
    public override async Task<GetAvailableDriversReply> GetAvailableDrivers(GetAvailableDriversRequest request, ServerCallContext context)
    {
        var exclude = request.ExcludeDriverIds.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).ToList();
        var maxResults = request.MaxResults > 0 ? request.MaxResults : 5;
        var drivers = await query.GetAvailableDriversAsync(exclude, maxResults, context.CancellationToken, request.ExperimentPool);

        var reply = new GetAvailableDriversReply();
        reply.Drivers.AddRange(drivers.Select(d => new AvailableDriver
        {
            Id = d.Id.ToString(),
            Name = d.Name,
            Vehicle = d.Vehicle,
            LicensePlate = d.LicensePlate,
            Rating = d.Rating,
            AvailableSinceUnixMs = d.AvailableSince?.ToUnixTimeMilliseconds() ?? 0,
        }));
        return reply;
    }
}
