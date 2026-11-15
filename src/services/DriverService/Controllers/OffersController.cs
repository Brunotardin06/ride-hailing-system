using DriverService.Models;
using DriverService.Services;
using Microsoft.AspNetCore.Mvc;

namespace DriverService.Controllers;

/// <summary>REST for the frontend: the driver answers an offer.</summary>
[ApiController]
[Route("api/offers")]
public class OffersController(OfferService offers) : ControllerBase
{
    // POST /api/offers/{id}/accept   body: { "driverId": "..." }
    [HttpPost("{offerId:guid}/accept")]
    public async Task<OfferDto> Accept(Guid offerId, OfferResponseRequest request, CancellationToken ct) =>
        OfferDto.From(await offers.AcceptAsync(offerId, request.DriverId, ct));

    // POST /api/offers/{id}/reject   body: { "driverId": "..." }
    [HttpPost("{offerId:guid}/reject")]
    public async Task<OfferDto> Reject(Guid offerId, OfferResponseRequest request, CancellationToken ct) =>
        OfferDto.From(await offers.RejectAsync(offerId, request.DriverId, ct));

    // Offer creation is NOT a REST endpoint in v2: MatchingService creates offers via gRPC (later phase).
}
