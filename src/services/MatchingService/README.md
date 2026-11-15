# MatchingService

Decides which driver gets each ride. Stateless: no database, no REST API for the frontend.

- Port: **5003** (`GET /health` only)
- **In**
  - RabbitMQ queue `matching-service`: `ride.requested` (attempt 1), `driver.rejected-ride` (next attempt)
  - Kafka `driver-locations` (consumer group `matching-service`): last known position per driver (`Services/DriverLocationCache.cs`)
- **Out**
  - gRPC to DriverService: `GetAvailableDrivers`, then `CreateOffer` for the **nearest** candidate (falls back to longest waiting when no position is known); idempotency key `rideId:attempt`
  - RabbitMQ `ride.matching-failed` after `Matching:MaxAttempts` (3) or when no driver is available
- Logic: `Services/MatchingWorkflow.cs` — async in (RabbitMQ), sync out (gRPC)
