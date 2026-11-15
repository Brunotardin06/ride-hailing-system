using Grpc.Core;
using RideHailing.Grpc.Drivers;
using RideHailing.Shared.Contracts;

namespace MatchingService.Services;

/// <summary>"Offer this ride to that driver" — gRPC CreateOffer on DriverService.</summary>
public class OfferClient(Drivers.DriversClient client)
{
    /// <returns>true = offer created; false = driver was taken meanwhile (try the next candidate).</returns>
    public async Task<bool> TryCreateOfferAsync(RideRequestedPayload ride, DriverCandidate driver, int attempt, Guid correlationId, CancellationToken ct)
    {
        try
        {
            var request = new CreateOfferRequest
            {
                // Same key on a retry of the same message → DriverService returns the existing offer.
                RequestKey = $"{ride.RideId}:{attempt}",
                RideId = ride.RideId.ToString(),
                CorrelationId = correlationId.ToString(),
                DriverId = driver.Id.ToString(),
                PassengerId = ride.PassengerId.ToString(),
                PassengerName = ride.PassengerName,
                Pickup = ride.Pickup,
                Destination = ride.Destination,
                Attempt = attempt,
            };
            // Optional proto fields: only set when known (unset ≠ 0.0).
            if (ride.PickupLat is { } pLat) request.PickupLat = pLat;
            if (ride.PickupLng is { } pLng) request.PickupLng = pLng;
            if (ride.DestinationLat is { } dLat) request.DestinationLat = dLat;
            if (ride.DestinationLng is { } dLng) request.DestinationLng = dLng;

            await client.CreateOfferAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
            return true;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            return false; // race: between the lookup and now, the driver got another offer or went offline
        }
        // Other errors (Unavailable, DeadlineExceeded) bubble up → the RabbitMQ consumer retries the message.
    }
}
