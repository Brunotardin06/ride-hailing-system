using System.Net;
using System.Net.Http.Json;
using RideHailing.Shared.Messaging;

namespace MatchingService.Services;

public sealed record CreateOfferRequest(
    string RequestKey, Guid RideId, Guid DriverId, Guid PassengerId, string PassengerName, string Pickup, string Destination, int Attempt);

public enum OfferResult { Created, AlreadyExists, DriverUnavailable, RideClosed }

/// <summary>Part of DriverService's OfferDto that MatchingService needs.</summary>
public sealed record CreatedOffer(Guid Id, Guid DriverId, DateTimeOffset CreatedAt);

/// <summary>REST client for POST {DriverService}/api/offers.</summary>
public sealed class OfferClient(HttpClient http)
{
    public async Task<(OfferResult Result, CreatedOffer? Offer)> CreateOfferAsync(CreateOfferRequest request, Guid correlationId, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/offers")
        {
            Content = JsonContent.Create(request, options: Json.Options),
        };
        message.Headers.Add("X-Correlation-ID", correlationId.ToString());

        using var response = await http.SendAsync(message, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.Created => (OfferResult.Created, await response.Content.ReadFromJsonAsync<CreatedOffer>(Json.Options, ct)),
            HttpStatusCode.OK => (OfferResult.AlreadyExists, await response.Content.ReadFromJsonAsync<CreatedOffer>(Json.Options, ct)),
            HttpStatusCode.Conflict => (OfferResult.DriverUnavailable, null),
            HttpStatusCode.Gone => (OfferResult.RideClosed, null),
            // Anything else is unexpected: throw so the RabbitMQ consumer retries the message.
            _ => throw new HttpRequestException($"Creating offer failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}"),
        };
    }
}
