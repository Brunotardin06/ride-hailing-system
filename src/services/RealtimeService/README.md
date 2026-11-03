# RealtimeService

Pushes notifications and chat to browsers over **SignalR** (WebSocket). It does not handle business actions; those go via REST to the owning service.

- URL: http://localhost:5004 · hub: `/hubs/rides` · `GET /api/realtime/status` (connection count)

## Hub methods (called by the browser)

| Method | Purpose |
| --- | --- |
| `Register(role, userId)` | Join group `passenger:<id>` or `driver:<id>` |
| `JoinRideChat(rideId)` / `LeaveRideChat(rideId)` | Join chat group `ride:<id>`; returns the recent messages |
| `SendChatMessage({ rideId, senderRole, senderName, text })` | Push to the other participants; returns the stored message |
| `Acknowledge(messageId)` | Client confirms receipt → SignalR delivery round-trip metric |

Server → client: `notification` (`{ id, type, rideId, title, message, data, sentAt }`) and `chatMessage`.

## Messaging

Consumes queue `realtime-service` (`ride.offer-created`, `driver.rejected-ride`, `ride.offer-withdrawn`, `ride.assigned`, `ride.started`, `ride.completed`, `ride.cancelled`, `ride.no-driver-available`) and turns each event into notifications for the passenger and/or driver.

State (connections, chat history, pending acknowledgements) is kept in memory; see the limitations in the main README.
