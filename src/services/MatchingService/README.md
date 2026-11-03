# MatchingService

Matches ride requests to available drivers. **Stateless** (no database): everything it needs is in the incoming message.

- URL: http://localhost:5003

## Workflow

1. Consumes `ride.requested` and `driver.rejected-ride` (queue `matching-service`).
2. Looks up available drivers at DriverService via **REST or gRPC**: `DriverLookup:Mode` = `Rest` | `Grpc` (runtime override: `PUT /api/experiments/lookup-mode`). Every lookup is measured.
3. Creates an offer with REST `POST /api/offers` and tries the next candidate on 409. The id of the triggering message is the idempotency key.
4. If no driver is available, or `Matching:MaxAttempts` (3) offers have been used, it publishes `ride.matching-failed`.

## Experiment endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/experiments/config` | Configured/active lookup mode, pending injected failures |
| PUT | `/api/experiments/lookup-mode` | `{ "mode": "Grpc" }` (`null` = back to the configured mode) |
| POST | `/api/experiments/driver-lookup` | REST vs gRPC benchmark → stores two runs in AnalyticsService |
| POST | `/api/experiments/fail-next` | `{ "count": 4 }`: the next N RabbitMQ messages fail (retry / dead-letter demo) |

Code: `Services/DriverDirectory.cs` (REST + gRPC clients, measured lookup), `Services/MatchingWorkflow.cs`, `Services/OfferClient.cs`, `Services/DriverLookupBenchmark.cs`.

## Synchronous matching and the experiment instance (Experiment A)

- `POST /api/matching/requests` runs the same `MatchingWorkflow.MatchAsync` synchronously and returns the outcome (`OfferCreated` / `NoDriverAvailable` / `RideClosed`). If the caller disconnects (timeout), the work is cancelled (`RequestAborted`) and counted.
- The same code runs as a second instance, **matching-experiment** (`Matching__Instance=Experiment`, port 5013): it consumes queue `matching-experiment` (prefetch 1), uses the experiment driver pool, and offers:
  - `GET/PUT /api/experiments/simulated-outage`: REST answers 503 and the RabbitMQ consumer is cancelled (only allowed on this instance);
  - `GET/DELETE /api/experiments/stats`: finished matching operations per outcome, incl. `Aborted`.
- Local run without Docker: `dotnet run --project src/services/MatchingService --launch-profile experiment`.
