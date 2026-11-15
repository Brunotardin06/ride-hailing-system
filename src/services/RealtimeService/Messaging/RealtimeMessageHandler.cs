using RealtimeService.Services;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Messaging;

namespace RealtimeService.Messaging;

/// <summary>
/// Queue "realtime-service" → SignalR. Translates each workflow event into notifications for the right people.
/// This is the bridge between the two worlds: backend events (RabbitMQ) and browsers (WebSocket).
/// </summary>
public sealed class RealtimeMessageHandler(
    NotificationService notify, ActiveRideTracker activeRides, ProcessedMessageCache processed, ILogger<RealtimeMessageHandler> logger)
    : IMessageHandler
{
    public async Task HandleAsync(MessageEnvelope m, CancellationToken ct)
    {
        if (!processed.TryMarkProcessed(m.EventId)) // no DB → in-memory duplicate check
        {
            logger.LogInformation("Skipping duplicate {EventType} {EventId}", m.EventType, m.EventId);
            return;
        }

        try
        {
            await NotifyAsync(m);
        }
        catch
        {
            processed.Remove(m.EventId); // let the retry send it
            throw;
        }
    }

    private async Task NotifyAsync(MessageEnvelope m)
    {
        var data = m.Payload;
        switch (m.EventType)
        {
            case EventTypes.RideOfferCreated:
            {
                var p = m.PayloadAs<RideOfferCreatedPayload>();
                await notify.ToDriverAsync(p.DriverId, "ride-offer", p.RideId, "New ride request",
                    $"{p.PassengerName}: {p.Pickup} → {p.Destination}", data);
                await notify.ToPassengerAsync(p.PassengerId, "driver-offered", p.RideId, "Driver found",
                    $"Waiting for {p.DriverName} to accept…", data);
                break;
            }
            case EventTypes.DriverRejectedRide:
            {
                var p = m.PayloadAs<DriverRejectedRidePayload>();
                await notify.ToDriverAsync(p.DriverId, "offer-closed", p.RideId, "Offer closed", p.Reason, data);
                await notify.ToPassengerAsync(p.PassengerId, "offer-declined", p.RideId, "Still searching",
                    $"{p.DriverName} could not take the ride. Looking for another driver…", data);
                break;
            }
            case EventTypes.RideOfferWithdrawn:
            {
                var p = m.PayloadAs<RideOfferWithdrawnPayload>();
                await notify.ToDriverAsync(p.DriverId, "offer-closed", p.RideId, "Request withdrawn", p.Reason, data);
                break;
            }
            case EventTypes.RideAssigned:
            {
                var p = m.PayloadAs<RideStatusChangedPayload>();
                if (p.DriverId is { } d)
                    activeRides.Start(d, p.RideId, p.PassengerId); // from now on: forward d's positions to the passenger
                await notify.ToPassengerAsync(p.PassengerId, "ride-assigned", p.RideId, "Driver on the way",
                    $"{p.DriverName} accepted your ride.", data);
                if (p.DriverId is { } driverId)
                    await notify.ToDriverAsync(driverId, "ride-assigned", p.RideId, "Ride confirmed", "Head to the pickup location.", data);
                break;
            }
            case EventTypes.RideStarted:
                await NotifyBothAsync(m, "ride-started", "Ride started", "Enjoy your ride!", "Ride started");
                break;
            case EventTypes.RideCompleted:
                StopTracking(m);
                await NotifyBothAsync(m, "ride-completed", "Ride completed", "You have arrived. Thanks for riding!", "Ride completed");
                break;
            case EventTypes.RideCancelled:
                StopTracking(m);
                await NotifyBothAsync(m, "ride-cancelled", "Ride cancelled", "The ride was cancelled.", "The passenger cancelled the ride.");
                break;
            case EventTypes.RideNoDriverAvailable:
            {
                var p = m.PayloadAs<RideStatusChangedPayload>();
                await notify.ToPassengerAsync(p.PassengerId, "no-driver", p.RideId, "No driver available",
                    p.Reason ?? "No driver could be found.", data);
                break;
            }
            default:
                logger.LogWarning("Unexpected event type {EventType}; ignoring", m.EventType);
                break;
        }
    }

    private void StopTracking(MessageEnvelope m)
    {
        var p = m.PayloadAs<RideStatusChangedPayload>();
        if (p.DriverId is { } driverId) activeRides.Stop(driverId, p.RideId);
    }

    private async Task NotifyBothAsync(MessageEnvelope m, string type, string title, string passengerText, string driverText)
    {
        var p = m.PayloadAs<RideStatusChangedPayload>();
        await notify.ToPassengerAsync(p.PassengerId, type, p.RideId, title, passengerText, m.Payload);
        if (p.DriverId is { } driverId)
            await notify.ToDriverAsync(driverId, type, p.RideId, title, driverText, m.Payload);
    }
}
