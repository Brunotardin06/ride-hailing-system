using DriverService.Services;
using RideHailing.Shared.Messaging;

namespace DriverService.Messaging;

/// <summary>Handles messages from queue "driver-service" (bindings in RabbitMqTopology). Only routes to OfferService.</summary>
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
        logger.LogWarning("Unexpected event type {EventType}; ignoring", message.EventType);
        return Task.CompletedTask;
    }
}
