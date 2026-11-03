using DriverService.Models;
using DriverService.Services;
using Microsoft.AspNetCore.Mvc;

namespace DriverService.Controllers;

[ApiController]
[Route("api/offers")]
public class OffersController(OfferService offers) : ControllerBase
{
    /// <summary>
    /// Called by MatchingService. 201 = offer created, 200 = same RequestKey already handled (idempotent),
    /// 409 = driver no longer available, 410 = ride was cancelled.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OfferDto>> Create(CreateOfferRequest request, CancellationToken ct)
    {
        var correlationId = Guid.TryParse(Request.Headers["X-Correlation-ID"], out var id) ? id : Guid.NewGuid();
        var (offer, created) = await offers.CreateOfferAsync(request, correlationId, ct);
        return created ? StatusCode(StatusCodes.Status201Created, OfferDto.From(offer)) : Ok(OfferDto.From(offer));
    }

    /// <summary>Called by the driver frontend.</summary>
    [HttpPost("{offerId:guid}/accept")]
    public async Task<OfferDto> Accept(Guid offerId, OfferResponseRequest request, CancellationToken ct) =>
        OfferDto.From(await offers.AcceptAsync(offerId, request.DriverId, ct));

    [HttpPost("{offerId:guid}/reject")]
    public async Task<OfferDto> Reject(Guid offerId, OfferResponseRequest request, CancellationToken ct) =>
        OfferDto.From(await offers.RejectAsync(offerId, request.DriverId, ct));
}
