using MatchingService.Services;
using RideHailing.Shared.Messaging;

namespace MatchingService.Messaging;

/// <summary>Handles messages from the "matching-service" RabbitMQ queue.</summary>
public sealed class MatchingMessageHandler(MatchingWorkflow workflow, ProcessedMessageCache processed, ILogger<MatchingMessageHandler> logger)
    : IMessageHandler
{
    public async Task HandleAsync(MessageEnvelope message, CancellationToken ct)
    {
        // MatchingService has no database, so duplicates are detected in memory.
        // (Offer creation is idempotent as well, see MatchingWorkflow.)
        if (!processed.TryMarkProcessed(message.EventId))
        {
            logger.LogInformation("Skipping duplicate {EventType} {EventId}", message.EventType, message.EventId);
            return;
        }

        try
        {
            await (message.EventType switch
            {
                EventTypes.RideRequested or EventTypes.ExperimentRideRequested => workflow.HandleRideRequestedAsync(message, ct),
                EventTypes.DriverRejectedRide => workflow.HandleDriverRejectedAsync(message, ct),
                _ => Task.CompletedTask,
            });
        }
        catch
        {
            processed.Remove(message.EventId); // allow the retry to process it
            throw;
        }
    }
}
