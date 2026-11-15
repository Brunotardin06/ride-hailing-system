using RideHailing.Shared.Messaging;
using RideService.Services;

namespace RideService.Messaging;

/// <summary>
/// Handles messages from queue "ride-service" (bindings in RabbitMqTopology).
/// Like a controller, but for messages: it only routes to RideWorkflow.
/// </summary>
public class RideMessageHandler(RideWorkflow rides, ILogger<RideMessageHandler> logger) : IMessageHandler
{
    public Task HandleAsync(MessageEnvelope message, CancellationToken ct)
    {
        switch (message.EventType)
        {
            case EventTypes.RideMatchingFailed:
                return rides.HandleMatchingFailedAsync(message, ct);
            default:
                logger.LogWarning("Unexpected event type {EventType}; ignoring", message.EventType);
                return Task.CompletedTask;
        }
    }
}
