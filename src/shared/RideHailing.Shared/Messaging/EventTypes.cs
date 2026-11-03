namespace RideHailing.Shared.Messaging;

/// <summary>
/// All event names in the system. On RabbitMQ the event type is used as the routing key;
/// on Kafka it is stored in the envelope and as a message header.
/// </summary>
public static class EventTypes
{
    // Ride workflow (RabbitMQ)
    public const string RideRequested = "ride.requested";            // RideService → MatchingService
    public const string RideOfferCreated = "ride.offer-created";     // DriverService → RealtimeService
    public const string DriverAcceptedRide = "driver.accepted-ride"; // DriverService → RideService
    public const string DriverRejectedRide = "driver.rejected-ride"; // DriverService → MatchingService, RealtimeService
    public const string RideOfferWithdrawn = "ride.offer-withdrawn"; // DriverService → RealtimeService (ride cancelled)
    public const string RideMatchingFailed = "ride.matching-failed"; // MatchingService → RideService

    // Experiment A: ride requests of experiment rides go to the dedicated "matching-experiment" instance,
    // so experiments never interfere with demo rides.
    public const string ExperimentRideRequested = "experiment.ride-requested"; // RideService → MatchingService (experiment instance)

    // Ride status changes published by RideService (RabbitMQ and Kafka)
    public const string RideAssigned = "ride.assigned";
    public const string RideStarted = "ride.started";
    public const string RideCompleted = "ride.completed";
    public const string RideCancelled = "ride.cancelled";
    public const string RideNoDriverAvailable = "ride.no-driver-available";
}
