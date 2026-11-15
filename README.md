# Distributed Ride-Hailing System (v2)

Fontys · Complex Software Systems · Personal Experiment 1.

A passenger requests a ride, the system offers it to the nearest available driver, the driver accepts, and both
follow the ride live (status, chat, driver position on a map). Four ASP.NET Core services communicate through
REST, gRPC, RabbitMQ, Kafka and SignalR; each communication was chosen from the v1 experiment results.

- **Why each communication was chosen:** [docs/communication-decisions.md](docs/communication-decisions.md)
- **Architecture diagram:** [docs/architecture-v2.drawio](docs/architecture-v2.drawio) (open in draw.io / diagrams.net)
- **v1 measurements:** `research/v1-results/` · v1 code: git tag `full-implementation`

## Structure

```
frontend/                      React + TypeScript + Vite (passenger + driver screens)
src/shared/RideHailing.Shared  contracts, RabbitMQ plumbing, outbox/inbox, Kafka driver-location stream, places
src/services/RideService       :5001 REST · :5011 gRPC   rides + lifecycle          (db: rides)
src/services/DriverService     :5002 REST · :5012 gRPC   drivers + offers           (db: drivers)
src/services/MatchingService   :5003                     picks the nearest driver   (stateless)
src/services/RealtimeService   :5004 SignalR /hubs/rides  notifications, chat, live location
docs/                          communication decisions + architecture diagram
```

Inside each service: `Controllers/` (REST in), `Grpc/` (gRPC in), `Messaging/` (RabbitMQ/Kafka in),
`Services/` (business logic, called by all of them), `Models/`, `Data/` (EF Core + migrations), `Protos/`.

## Communication at a glance

| From → To | Mechanism | What |
| --- | --- | --- |
| Frontend → Ride / Driver | REST | all user actions |
| Frontend ⇄ Realtime | SignalR | notifications, chat, driver GPS (up) and position (down) |
| Ride → Matching | RabbitMQ | `ride.requested` |
| Matching → Driver | gRPC | available drivers, create offer |
| Driver → Ride | gRPC | assign the ride when the driver accepts |
| Driver → Matching | RabbitMQ | `driver.rejected-ride` (rejected / expired) |
| Matching → Ride | RabbitMQ | `ride.matching-failed` |
| Ride → Driver | RabbitMQ | `ride.completed`, `ride.cancelled` |
| Ride + Driver → Realtime | RabbitMQ | status and offer events |
| Realtime → Realtime + Matching | Kafka | `driver-locations` (key = driverId) |

## Run

Requires Docker Desktop and Node.js.

```powershell
docker compose up -d --build        # PostgreSQL, RabbitMQ, Kafka + the 4 services (first build ~3 min)

cd frontend
npm.cmd install
npm.cmd run dev                     # http://localhost:5173
```

Open a **Driver** tab (go online) and a **Passenger** tab, request a ride, and accept it as the driver.

| Useful | |
| --- | --- |
| Rebuild one service after a code change | `docker compose up -d --build ride-service` |
| Logs of one service | `docker compose logs -f matching-service` |
| RabbitMQ UI (queues, bindings) | http://localhost:15672 · `ridehailing` / `ridehailing` |
| Reset demo drivers | "Reset demo" in the UI, or `POST http://localhost:5002/api/dev/reset` |
| Stop / wipe all data | `docker compose down` / `docker compose down -v` |
| Test requests | `src/services/*/*.http` (VS Code REST Client / Rider / Visual Studio) |

The services can also run on the host (`dotnet run --project src/services/RideService`); their `appsettings.json`
points to `localhost`. On this machine Windows Smart App Control may block freshly built DLLs, which is why
Docker is the default.
