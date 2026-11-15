namespace RideHailing.Shared.Messaging;

/// <summary>
/// All RabbitMQ event names. The event type is also the routing key: the exchange uses it to decide
/// which queues get a copy (see RabbitMqTopology.Bindings).
/// Naming: "&lt;subject&gt;.&lt;what happened&gt;", in past tense — an event is a FACT, not a command.
/// </summary>
public static class EventTypes
{
    // Matching flow
    public const string RideRequested = "ride.requested";              // Ride     → Matching
    public const string DriverRejectedRide = "driver.rejected-ride";   // Driver   → Matching, Realtime (rejected or expired)
    public const string RideMatchingFailed = "ride.matching-failed";   // Matching → Ride (no driver left)

    // Offers (for the driver's screen)
    public const string RideOfferCreated = "ride.offer-created";       // Driver   → Realtime
    public const string RideOfferWithdrawn = "ride.offer-withdrawn";   // Driver   → Realtime (ride cancelled)

    // Ride status changes, published by RideService
    public const string RideAssigned = "ride.assigned";                // Ride → Realtime
    public const string RideStarted = "ride.started";                  // Ride → Realtime
    public const string RideCompleted = "ride.completed";              // Ride → Driver (free the driver), Realtime
    public const string RideCancelled = "ride.cancelled";              // Ride → Driver (withdraw offer / free driver), Realtime
    public const string RideNoDriverAvailable = "ride.no-driver-available"; // Ride → Realtime
}
