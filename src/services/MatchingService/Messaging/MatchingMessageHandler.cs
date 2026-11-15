using MatchingService.Services;
using RideHailing.Shared.Messaging;

namespace MatchingService.Messaging;

/// <summary>
/// Handles messages from queue "matching-service" (bindings in RabbitMqTopology).
/// No database here, so duplicates are detected with the in-memory ProcessedMessageCache.
/// </summary>
public class MatchingMessageHandler(MatchingWorkflow matching, ProcessedMessageCache processed, ILogger<MatchingMessageHandler> logger)
    : IMessageHandler
{
    public async Task HandleAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (!processed.TryMarkProcessed(message.EventId))
        {
            logger.LogInformation("Duplicate {EventType} {EventId}; skipping", message.EventType, message.EventId);
            return;
        }

        try
        {
            switch (message.EventType)
            {
                case EventTypes.RideRequested:
                    await matching.HandleRideRequestedAsync(message, ct);
                    break;
                case EventTypes.DriverRejectedRide:
                    await matching.HandleDriverRejectedAsync(message, ct);
                    break;
                default:
                    logger.LogWarning("Unexpected event type {EventType}; ignoring", message.EventType);
                    break;
            }
        }
        catch
        {
            processed.Remove(message.EventId); // failed → forget it, so the retry is not skipped as a "duplicate"
            throw;
        }
    }
}
