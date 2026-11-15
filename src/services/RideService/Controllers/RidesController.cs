using Microsoft.AspNetCore.Mvc;
using RideService.Models;
using RideService.Services;

namespace RideService.Controllers;

/// <summary>REST for the frontend (passenger and driver screens).</summary>
[ApiController]
[Route("api/rides")]
public class RidesController(RideWorkflow rides) : ControllerBase
{
    // POST /api/rides  → 201 Created + Location header pointing to GET /api/rides/{id}
    [HttpPost]
    // (not named "Request": that would hide ControllerBase.Request, the current HTTP request)
    public async Task<ActionResult<RideDto>> RequestRide(RequestRideRequest request, CancellationToken ct)
    {
        var ride = await rides.RequestAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { rideId = ride.Id }, RideDto.From(ride));
    }

    // GET /api/rides?passengerId=…&driverId=…&limit=50
    [HttpGet]
    public async Task<IEnumerable<RideDto>> List([FromQuery] Guid? passengerId, [FromQuery] Guid? driverId,
        [FromQuery] int limit = 50, CancellationToken ct = default) =>
        (await rides.ListAsync(passengerId, driverId, limit, ct)).Select(RideDto.From);

    // GET /api/rides/active?passengerId=… | ?driverId=…  → 200 with the ride, or 204 No Content if none
    // (declared before "{rideId:guid}"; the :guid constraint also stops "active" from matching that route)
    [HttpGet("active")]
    public async Task<ActionResult<RideDto>> Active([FromQuery] Guid? passengerId, [FromQuery] Guid? driverId, CancellationToken ct)
    {
        var ride = await rides.GetActiveAsync(passengerId, driverId, ct);
        return ride is null ? NoContent() : RideDto.From(ride);
    }

    // GET /api/rides/{id}
    [HttpGet("{rideId:guid}")]
    public async Task<RideDto> Get(Guid rideId, CancellationToken ct) =>
        RideDto.From(await rides.GetAsync(rideId, ct));

    // POST /api/rides/{id}/start     body: { "driverId": "..." }
    [HttpPost("{rideId:guid}/start")]
    public async Task<RideDto> Start(Guid rideId, DriverActionRequest request, CancellationToken ct) =>
        RideDto.From(await rides.StartAsync(rideId, request.DriverId, ct));

    // POST /api/rides/{id}/complete  body: { "driverId": "..." }
    [HttpPost("{rideId:guid}/complete")]
    public async Task<RideDto> Complete(Guid rideId, DriverActionRequest request, CancellationToken ct) =>
        RideDto.From(await rides.CompleteAsync(rideId, request.DriverId, ct));

    // POST /api/rides/{id}/cancel    body: { "passengerId": "...", "reason": "..." }
    [HttpPost("{rideId:guid}/cancel")]
    public async Task<RideDto> Cancel(Guid rideId, CancelRideRequest request, CancellationToken ct) =>
        RideDto.From(await rides.CancelAsync(rideId, request.PassengerId, request.Reason, ct));
}
