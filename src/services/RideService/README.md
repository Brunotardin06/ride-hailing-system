# RideService

Owns ride requests and the ride lifecycle: `Requested → Assigned → InProgress → Completed`, or `Cancelled` / `NoDriverAvailable`.

- URL: http://localhost:5001 · database `rides` · health `GET /health`

## API (REST, called by the frontend)

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/rides` | Request a ride (one active ride per passenger; optional `X-Correlation-ID` header) |
| GET | `/api/rides/{id}` | Ride details |
| GET | `/api/rides?passengerId=&driverId=&limit=` | History, newest first |
| GET | `/api/rides/active?passengerId=` or `?driverId=` | Current ride (204 if none) |
| POST | `/api/rides/{id}/start` · `/complete` | Assigned driver only (`{ "driverId": … }`) |
| POST | `/api/rides/{id}/cancel` | Passenger, while Requested or Assigned |

Errors are ProblemDetails: 403 wrong actor, 404 unknown ride, 409 invalid transition.

## Messaging

- **Publishes** (via the transactional outbox) to RabbitMQ *and* Kafka (`ride-events`, key = ride id): `ride.requested`, `ride.assigned`, `ride.started`, `ride.completed`, `ride.cancelled`, `ride.no-driver-available`.
- **Consumes** queue `ride-service`: `driver.accepted-ride` (→ Assigned), `ride.matching-failed` (→ NoDriverAvailable). Processed event ids are stored, so handling is idempotent.

## Code

`Models/Ride.cs` (state machine) · `Services/RideWorkflow.cs` (application logic) · `Controllers/RidesController.cs` · `Messaging/RideMessageHandler.cs`, `Messaging/KafkaRideEventProducer.cs` · `Data/` (DbContext, migrations).

## Matching paths (Experiment A)

`POST /api/rides` accepts optional fields `matchingMode` (`RabbitMq` | `Rest`), `experimentRunId`, `simulatedMatchingDelayMs` and `matchingTimeoutMs` (the last two only for experiment rides). Default mode: `Matching:Mode` (`RabbitMq`).

- **RabbitMq:** the ride is stored and `ride.requested` (experiment rides: `experiment.ride-requested`) is published via the outbox; the response is 201 right away. The ride stays *Requested* with `offerCreatedAt = null` (pending) until `ride.offer-created` arrives.
- **Rest:** RideService calls `POST {MatchingService}/api/matching/requests` and waits (`Matching:RestTimeoutMs`, default 3000). Offer created → 201 with `offerCreatedAt`; no driver → ride NoDriverAvailable; timeout/unreachable/HTTP error → the ride is cancelled with the reason and the response is **503** (with `rideId` and `reason`).
- Experiment rides are not published to the business Kafka topic. `GET /api/rides?experimentRunId=` lists them.
