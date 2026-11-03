# Distributed Ride-Hailing System

A simplified ride-hailing platform built to study and compare service communication mechanisms: **REST, gRPC, RabbitMQ, Kafka and SignalR/WebSocket**.

*Fontys University of Applied Sciences: Complex Software Systems, Personal Experiment 1.*

Passengers request rides, drivers receive offers in real time, accept or reject them, drive and complete the ride, and both can chat.

**Research focus.** The project is a *scenario-based investigation of communication decisions*:

> How do different communication approaches between services affect the behaviour and design of a distributed ride-hailing system?

- **Experiment A:** synchronous (REST) vs asynchronous (RabbitMQ) for Ride Service → Matching Service, in normal operation, with Matching Service unavailable, and with slow processing.
- **Experiment B:** message queuing (RabbitMQ) vs event streaming (Kafka) for ride lifecycle events, with one consumer, independent consumers, an offline consumer, and replay.
- **Experiment C:** an architectural trade-off analysis built on the measured evidence.

The goal is not to find a technology that is always better, but to justify which approach fits which interaction. See **[docs/research-design.md](docs/research-design.md)**.

## What is implemented

- **Five ASP.NET Core (.NET 10) services:** RideService, DriverService, MatchingService, RealtimeService, AnalyticsService. Each has its own database where needed (PostgreSQL + EF Core migrations).
- **Complete ride workflow:** request → matching → offer → accept/reject/expire → assigned → start → complete, plus cancellation and "no driver available". Status changes are validated by state machines.
- **Communication:**
  - **REST:** all frontend actions; Matching → Driver lookup and offer creation.
  - **gRPC:** the same driver lookup (switchable).
  - **RabbitMQ:** the workflow, with publisher confirms, manual acks, retries, a dead-letter queue, an outbox and idempotent consumers.
  - **Kafka:** ride lifecycle event stream, consumed by Analytics.
  - **SignalR:** notifications and two-way chat, with client acknowledgements.
- **Analytics:**
  - Business statistics from Kafka events, plus a Kafka inspection view (offsets, lag, independent consumer).
  - Experiment metrics with average, p50/p95/p99, throughput and error rate for every mechanism, and a precise definition of each measured interval.
- **Research experiments (dashboard + API):**
  - Experiment A (REST vs RabbitMQ for ride matching) with scenarios A1 normal, A2 unavailable, A3 slow, on isolated experiment resources. Durability is verified, not assumed. A real-outage script is included.
  - Experiment B (RabbitMQ vs Kafka) with scenarios B1 single consumer, B2 multiple consumers, B3 consumer offline, B4 replay.
  - Experiment C: findings per scenario and trade-off matrices, with measured evidence, hypotheses and the researcher's conclusions kept separate.
- **Supplementary experiments:**
  - REST vs gRPC benchmark (built in and with k6).
  - RabbitMQ failure/recovery.
  - Retry and dead-letter failure injection.
  - Kafka replay.
- **React + TypeScript frontend:** passenger, driver and experiment views in a single app; works with two tabs side by side.
- **Tests:**
  - 29 unit tests.
  - 14 end-to-end tests: the ride workflow (REST/SignalR/RabbitMQ/Kafka/gRPC) plus Experiments A2, A3, B2–B4 and C.
  - Two browser tests: passenger + driver windows, and the research dashboard (runs A2 and B4 from the UI).

## Repository layout

```
.
├── RideHailing.slnx                 .NET solution
├── docker-compose.yml               PostgreSQL, RabbitMQ, Kafka + the five services
├── .env.example                     Docker Compose settings (ports, credentials, lookup mode)
├── docker/postgres/                 Creates one database per service
├── src/
│   ├── shared/RideHailing.Shared/   Message contracts, RabbitMQ plumbing, outbox, metrics reporter
│   └── services/                    RideService, DriverService, MatchingService, RealtimeService, AnalyticsService
├── frontend/                        React + TypeScript + Vite app
├── tests/
│   ├── RideHailing.UnitTests/       xUnit: state machines, statistics, k6 import
│   └── e2e/                         End-to-end (run.mjs) and browser (ui.mjs) tests
├── experiments/                     k6 scripts, REST/gRPC k6 runner, RabbitMQ recovery experiment
└── docs/                            Architecture, communication, experiments, demo guide
```

## Ports

| Component | URL / port |
| --- | --- |
| Frontend (Vite dev server) | http://localhost:5173 |
| RideService | http://localhost:5001 |
| DriverService | http://localhost:5002 (REST) · localhost:5012 (gRPC, HTTP/2) |
| MatchingService | http://localhost:5003 |
| MatchingService, experiment instance (Experiment A only) | http://localhost:5013 |
| RealtimeService | http://localhost:5004 (SignalR hub `/hubs/rides`) |
| AnalyticsService | http://localhost:5005 |
| PostgreSQL | localhost:5432 (databases `rides`, `drivers`, `analytics`) |
| RabbitMQ / management UI | localhost:5672 / http://localhost:15672 |
| Kafka | localhost:9092 (containers: `kafka:19092`) |

Credentials for PostgreSQL and RabbitMQ: `ridehailing` / `ridehailing` (local development only). Every service exposes `GET /health` and, in Development, `GET /openapi/v1.json`.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running)
- [Node.js](https://nodejs.org/) 20.19+ or 22.12+ (frontend, tests, experiment scripts)
- [.NET SDK 10](https://dotnet.microsoft.com/download) (only needed to run or build services outside Docker)

## Quick start

```bash
# 1. Backend + infrastructure (first build takes a few minutes)
cp .env.example .env            # optional, the defaults are the same
docker compose up -d --build
docker compose ps               # all services "Up"; infrastructure "healthy"

# 2. Frontend
cd frontend
npm install
npm run dev                     # http://localhost:5173
```

Database migrations are applied and the six demo drivers are seeded automatically at start-up. Then follow the **[live demo guide](docs/demo-guide.md)**: one window as passenger, one as driver.

### Alternative: services on the host (for development/debugging)

```bash
docker compose up -d postgres rabbitmq kafka
dotnet run --project src/services/AnalyticsService
dotnet run --project src/services/DriverService
dotnet run --project src/services/RideService
dotnet run --project src/services/MatchingService
dotnet run --project src/services/RealtimeService
```

Use one terminal per service. Don't run the Docker services at the same time, because they use the same host ports (`docker compose stop ride-service driver-service matching-service realtime-service analytics-service`).

## Tests

```bash
dotnet test                                   # unit tests (no infrastructure needed)

# with the system running:
cd tests/e2e && npm install
npm test                                      # end-to-end: ride workflow + research experiments A/B/C (~2 min)
npx playwright install chromium               # once
npm run test:ui                               # browser test: passenger + driver windows (frontend must run)
npm run test:ui-experiments                   # browser test: runs Experiments A2 and B4 from the dashboard
```

## Experiments

Open http://localhost:5173/experiments → **Overview**, **A · Sync vs async**, **B · Messaging vs streaming**, **C · Architectural decisions**. Choose a scenario, run it, and inspect the measurements and observations. The guide with all parameters, how to read the results and what can and cannot be concluded is **[docs/experiments.md](docs/experiments.md)**.

```bash
# Experiment A / B via the API (the dashboard does the same)
curl -X POST http://localhost:5005/api/experiments/a -H "Content-Type: application/json" -d '{"scenario":"A2-unavailable","rides":5,"outageSeconds":10}'
curl -X POST http://localhost:5005/api/experiments/b -H "Content-Type: application/json" -d '{"scenario":"B4-replay","rides":50}'
# Experiment A2 with a real process stop of the experiment matching instance
node experiments/experiment-a-real-outage.mjs --rides 5 --outage 15
```

Supplementary tools (*Additional tools* on the dashboard):

```bash
# REST vs gRPC: .NET clients (also a button on the dashboard)
curl -X POST http://localhost:5003/api/experiments/driver-lookup -H "Content-Type: application/json" \
     -d '{"iterations":1000,"warmup":100,"concurrency":1,"rounds":4}'
# REST vs gRPC: k6 load test (runs k6 in Docker, imports results)
node experiments/run-k6-comparison.mjs --vus 10 --duration 30s
# RabbitMQ consumer outage and recovery
node experiments/rabbitmq-recovery.mjs --rides 5 --outage 10
```

Results appear on http://localhost:5173/experiments.

## Inspecting RabbitMQ and Kafka

**RabbitMQ:** http://localhost:15672 (`ridehailing` / `ridehailing`).
- *Exchanges → ridehailing.events*: the topic exchange; routing key = event type.
- *Queues*: `matching-service`, `ride-service`, `driver-service`, `realtime-service` and their `.dlq` dead-letter queues. *Get messages* on a queue shows the JSON envelope (eventId, correlationId, payload) and AMQP properties.

**Kafka:** *Additional tools → Kafka stream inspector* on the experiment dashboard shows partitions, offsets, lag and the latest records. From the command line:

```bash
docker compose exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --describe --topic ride-events
docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group analytics-service
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 \
  --topic ride-events --from-beginning --property print.key=true --property print.partition=true
```

(In Git Bash, prefix these with `MSYS_NO_PATHCONV=1` so the `/opt/...` paths are not rewritten.)

## Useful commands

| Task | Command |
| --- | --- |
| Build all backend projects | `dotnet build RideHailing.slnx` |
| Rebuild one service in Docker | `docker compose up -d --build ride-service` |
| Follow logs | `docker compose logs -f matching-service` |
| Reset drivers to the demo state | `curl -X POST http://localhost:5002/api/dev/reset` (or *Reset demo* on the driver dashboard) |
| Clear live measurements | `curl -X DELETE http://localhost:5005/api/metrics/measurements` |
| Use gRPC for live matching | `DRIVER_LOOKUP_MODE=Grpc` in `.env` + `docker compose up -d matching-service`, or the dashboard button |
| Add a migration | `dotnet tool restore` then `dotnet ef migrations add <Name> --project src/services/RideService --output-dir Data/Migrations` |
| Stop / stop and delete all data | `docker compose down` / `docker compose down -v` |

## Documentation

- [docs/research-design.md](docs/research-design.md): research question, sub-questions, hypotheses, scenarios, methodology, measurements, observations from the verification runs, limitations.
- [docs/architecture.md](docs/architecture.md): services, data ownership, architecture diagram, fixed communication decisions vs experimental alternatives, reliability patterns.
- [docs/communication-plan.md](docs/communication-plan.md): which mechanism is used where and why; sequence diagrams; event catalogue.
- [docs/experiments.md](docs/experiments.md): how to run Experiments A, B and C, how to read the results, what can and cannot be concluded, reproduction; supplementary tools and metric definitions.
- [docs/demo-guide.md](docs/demo-guide.md): step-by-step demo of the application and the research experiments.

## Known limitations

- **No authentication.** Each browser tab simply chooses a demo passenger or driver. Anyone can call any endpoint or join any chat group. Passengers are fixed demo identities; they are not stored by any service.
- **Chat history and SignalR state are in memory** (RealtimeService), so they are lost on restart. RealtimeService runs as a single instance; scaling it out would need a SignalR backplane (e.g. Redis), which was deliberately left out.
- **Duplicate detection in MatchingService/RealtimeService is in memory.** After a restart a redelivered message can be processed again. Offer creation is still idempotent (RequestKey), but a duplicate notification is possible.
- **No geography.** Matching picks the driver who has been available longest; there are no maps, locations, distances or prices.
- **At-least-once, not exactly-once.** All consumers are idempotent, but the outbox can publish an event twice if the service crashes between publishing and marking it as sent.
- **Measurements are local.** All containers share one machine, so network latency is near zero and results reflect serialisation, protocol and broker overhead more than network effects. Cross-process timestamps (RabbitMQ/Kafka delivery) assume synchronised clocks, which holds on one host only.
- **The dev endpoint `POST /api/dev/reset`** only works in the Development environment; the Compose file runs all services in Development.
- **Experiment scope.** Small workloads on one host; single runs are illustrative and must be repeated. Experiment A covers only the initial matching request (re-matching after a rejection always uses RabbitMQ). Experiment B simulates independent services as separate subscriptions inside AnalyticsService and tests classic RabbitMQ queues only (not RabbitMQ Streams). The dashboard outage is simulated; a real stop is available as a script. Details in [docs/research-design.md](docs/research-design.md#limitations-and-assumptions).
- **The frontend is not containerised.** It runs with the Vite dev server. The services' CORS settings allow only `http://localhost:5173`.
