using DriverService.Services;
using RideHailing.Shared.Messaging;

namespace DriverService.Messaging;

/// <summary>Handles messages from the "driver-service" RabbitMQ queue: frees drivers when rides end.</summary>
public class DriverMessageHandler(OfferService offers, ILogger<DriverMessageHandler> logger) : IMessageHandler
{
    public Task HandleAsync(MessageEnvelope message, CancellationToken ct) => message.EventType switch
    {
        EventTypes.RideCompleted => offers.HandleRideCompletedAsync(message, ct),
        EventTypes.RideCancelled => offers.HandleRideCancelledAsync(message, ct),
        _ => Ignore(message),
    };

    private Task Ignore(MessageEnvelope message)
    {
        logger.LogWarning("Ignoring unexpected event type {EventType}", message.EventType);
        return Task.CompletedTask;
    }
}
