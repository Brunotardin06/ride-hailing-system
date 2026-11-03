# DriverService

Owns drivers, their availability and ride offers. Hosts the driver-availability lookup over **REST and gRPC**.

- REST: http://localhost:5002 (HTTP/1.1) · gRPC: localhost:5012 (HTTP/2, plaintext) · database `drivers`
- Six demo drivers are seeded by the initial migration (Emma and Lucas available).

## API

| Method | Path | Called by | Purpose |
| --- | --- | --- | --- |
| GET | `/api/drivers`, `/api/drivers/{id}` | frontend | Drivers and status |
| PUT | `/api/drivers/{id}/availability` | frontend | `{ "isAvailable": true }`: online/offline |
| GET | `/api/drivers/{id}/offers?status=Pending` | frontend | Incoming offers |
| POST | `/api/offers/{id}/accept` · `/reject` | frontend | `{ "driverId": … }` |
| GET | `/api/drivers/available?exclude=&max=` | MatchingService | **REST** driver lookup |
| gRPC | `DriverAvailability/GetAvailableDrivers` | MatchingService | **gRPC** driver lookup (`Protos/driver_availability.proto`) |
| POST | `/api/offers` | MatchingService | Create offer. 201 created · 200 same `requestKey` (idempotent) · 409 driver unavailable · 410 ride cancelled |
| POST | `/api/dev/reset` | demo/tests | Reset drivers to the seed state (Development only) |

The REST and gRPC lookups run the same query (`Services/DriverAvailabilityQuery.cs`).

## Messaging

- **Publishes** (outbox): `ride.offer-created`, `driver.accepted-ride`, `driver.rejected-ride` (reject or 30 s expiry; includes the drivers already tried), `ride.offer-withdrawn`.
- **Consumes** queue `driver-service`: `ride.completed` (free driver, count ride), `ride.cancelled` (withdraw offers, free driver, close ride).

Driver states: `Offline ⇄ Available → Offered → Busy → Available`. Optimistic concurrency (`xmin`) ensures a driver never gets two offers at once.

## Experiment driver pool (Experiment A)

20 hidden experiment drivers (`IsExperimentDriver`, seeded by migration) are used only for experiment rides: the lookup takes `pool=experiment` (REST) / `experiment_pool = true` (gRPC). They are not returned by `GET /api/drivers`, and their offers never expire.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/experiments/drivers/reset` | All experiment drivers available, pending experiment offers withdrawn |
| POST | `/api/experiments/offers/count` | Offers per ride (`{ "rideIds": [...] }`): detects duplicate processing |
