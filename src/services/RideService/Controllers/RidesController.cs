using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;
using RideService.Data;
using RideService.Models;
using RideService.Services;

namespace RideService.Controllers;

/// <summary>REST API used directly by the passenger and driver frontends.</summary>
[ApiController]
[Route("api/rides")]
public class RidesController(RideWorkflow workflow, RideDbContext db) : ControllerBase
{
    /// <summary>
    /// Request a ride. RabbitMQ matching: 201 immediately, matching continues asynchronously.
    /// REST matching: 201 after MatchingService answered, or 503 if it did not (see RideWorkflow).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RideDto>> RequestRide(CreateRideRequest request, CancellationToken ct)
    {
        // Reuse the caller's correlation id if it sent one, otherwise start a new workflow id.
        var correlationId = Guid.TryParse(Request.Headers["X-Correlation-ID"], out var id) ? id : Guid.NewGuid();
        var ride = await workflow.RequestRideAsync(request, correlationId, ct);
        return CreatedAtAction(nameof(GetRide), new { rideId = ride.Id }, RideDto.From(ride));
    }

    [HttpGet("{rideId:guid}")]
    public async Task<ActionResult<RideDto>> GetRide(Guid rideId, CancellationToken ct)
    {
        var ride = await db.Rides.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rideId, ct)
            ?? throw new NotFoundException($"Ride {rideId} not found.");
        return RideDto.From(ride);
    }

    /// <summary>Ride history, newest first, filtered by passenger and/or driver.</summary>
    [HttpGet]
    public async Task<IEnumerable<RideDto>> GetRides(Guid? passengerId, Guid? driverId, Guid? experimentRunId, int limit = 50, CancellationToken ct = default)
    {
        var query = db.Rides.AsNoTracking();
        if (passengerId is not null) query = query.Where(r => r.PassengerId == passengerId);
        if (driverId is not null) query = query.Where(r => r.DriverId == driverId);
        if (experimentRunId is not null) query = query.Where(r => r.ExperimentRunId == experimentRunId);
        var rides = await query.OrderByDescending(r => r.RequestedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
        return rides.Select(RideDto.From);
    }

    /// <summary>The current (not yet finished) ride of a passenger or driver; 204 if there is none.</summary>
    [HttpGet("active")]
    public async Task<ActionResult<RideDto>> GetActiveRide(Guid? passengerId, Guid? driverId, CancellationToken ct)
    {
        if (passengerId is null && driverId is null) throw new ArgumentException("Specify passengerId or driverId.");
        var ride = await db.Rides.AsNoTracking()
            .Where(r => Ride.ActiveStatuses.Contains(r.Status))
            .Where(r => (passengerId != null && r.PassengerId == passengerId) || (driverId != null && r.DriverId == driverId))
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct);
        return ride is null ? NoContent() : RideDto.From(ride);
    }

    [HttpPost("{rideId:guid}/start")]
    public async Task<RideDto> Start(Guid rideId, DriverActionRequest request, CancellationToken ct) =>
        RideDto.From(await workflow.StartAsync(rideId, request.DriverId, ct));

    [HttpPost("{rideId:guid}/complete")]
    public async Task<RideDto> Complete(Guid rideId, DriverActionRequest request, CancellationToken ct) =>
        RideDto.From(await workflow.CompleteAsync(rideId, request.DriverId, ct));

    [HttpPost("{rideId:guid}/cancel")]
    public async Task<RideDto> Cancel(Guid rideId, CancelRideRequest request, CancellationToken ct) =>
        RideDto.From(await workflow.CancelAsync(rideId, request.PassengerId, request.Reason, ct));
}
