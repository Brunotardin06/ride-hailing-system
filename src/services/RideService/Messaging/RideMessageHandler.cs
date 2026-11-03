using RideHailing.Shared.Messaging;
using RideService.Services;

namespace RideService.Messaging;

/// <summary>Handles messages from the "ride-service" RabbitMQ queue.</summary>
public class RideMessageHandler(RideWorkflow workflow, ILogger<RideMessageHandler> logger) : IMessageHandler
{
    public Task HandleAsync(MessageEnvelope message, CancellationToken ct) => message.EventType switch
    {
        EventTypes.DriverAcceptedRide => workflow.HandleDriverAcceptedAsync(message, ct),
        EventTypes.RideMatchingFailed => workflow.HandleMatchingFailedAsync(message, ct),
        EventTypes.RideOfferCreated => workflow.HandleOfferCreatedAsync(message, ct),
        _ => Ignore(message),
    };

    private Task Ignore(MessageEnvelope message)
    {
        logger.LogWarning("Ignoring unexpected event type {EventType}", message.EventType);
        return Task.CompletedTask;
    }
}
