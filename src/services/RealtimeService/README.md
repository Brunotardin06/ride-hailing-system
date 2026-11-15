# RealtimeService

The bridge between backend events and browsers: notifications, chat and the driver's live location. No database, no business rules.

- Port: **5004** · SignalR hub `/hubs/rides` · `GET /health`
- **Hub methods (browser → server):** `Register(role, userId)`, `JoinRideChat`, `LeaveRideChat`, `SendChatMessage`, `Acknowledge`, `UpdateLocation(driverId, lat, lng)`
- **Client events (server → browser):** `notification`, `chatMessage`, `driverLocation`
- **In**
  - RabbitMQ queue `realtime-service`: offer and ride status events → notifications (`Messaging/RealtimeMessageHandler.cs`)
  - Kafka `driver-locations` (consumer group `realtime-service`) → position pushed to the passenger of that ride (`Messaging/RealtimeLocationHandler.cs`)
- **Out:** Kafka `driver-locations` (producer, key = driverId), from the driver's `UpdateLocation`
- State in memory (`Services/RealtimeState.cs`, `Services/ActiveRideTracker.cs`): lost on restart; more instances would need a SignalR backplane
