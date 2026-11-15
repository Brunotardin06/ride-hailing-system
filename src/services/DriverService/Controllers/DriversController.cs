using DriverService.Models;
using DriverService.Services;
using Microsoft.AspNetCore.Mvc;

namespace DriverService.Controllers;

/// <summary>REST for the frontend: driver dashboard, availability toggle, incoming requests.</summary>
[ApiController]
[Route("api/drivers")]
public class DriversController(DriverManager drivers, OfferService offers) : ControllerBase
{
    // GET /api/drivers
    [HttpGet]
    public async Task<IEnumerable<DriverDto>> GetDrivers(CancellationToken ct) =>
        (await drivers.GetAllAsync(ct)).Select(DriverDto.From);

    // GET /api/drivers/{id}   (":guid" = route only matches a valid GUID, otherwise 404)
    [HttpGet("{driverId:guid}")]
    public async Task<DriverDto> GetDriver(Guid driverId, CancellationToken ct) =>
        DriverDto.From(await drivers.GetAsync(driverId, ct));

    // PUT /api/drivers/{id}/availability   body: { "isAvailable": true }
    [HttpPut("{driverId:guid}/availability")]
    public async Task<DriverDto> SetAvailability(Guid driverId, SetAvailabilityRequest request, CancellationToken ct) =>
        DriverDto.From(await drivers.SetAvailabilityAsync(driverId, request.IsAvailable, ct));

    // GET /api/drivers/{id}/offers?status=Pending
    [HttpGet("{driverId:guid}/offers")]
    public async Task<IEnumerable<OfferDto>> GetOffers(Guid driverId, [FromQuery] OfferStatus? status, CancellationToken ct) =>
        (await offers.GetOffersAsync(driverId, status, ct)).Select(OfferDto.From);
}
