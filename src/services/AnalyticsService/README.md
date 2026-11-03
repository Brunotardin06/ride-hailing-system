# AnalyticsService

Two responsibilities, one database (`analytics`):

- **A. Business event analytics:** consumes the Kafka topic `ride-events` (consumer group `analytics-service`, manual commit, duplicate-safe) and stores every event.
- **B. Experiment metrics:** stores communication measurements from all services and the results of experiment runs.

- URL: http://localhost:5005

## API (used by the Experiment Dashboard)

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/analytics/summary` | Ride counts, completion rate, time to accept, ride duration, events per minute |
| GET | `/api/analytics/events?limit=&rideId=&eventType=` | Stored events (with partition/offset) |
| GET | `/api/analytics/kafka/stream?limit=` | Latest records, read by a new, independent consumer group |
| GET | `/api/analytics/kafka/consumer-group` | Offsets and lag per partition |
| POST | `/api/metrics/measurements` | Batch of measurements (sent by the services) |
| GET | `/api/metrics/summary?minutes=` | Avg / p50 / p95 / p99 / max, error rate, observed rate per mechanism + kind + operation |
| GET | `/api/metrics/series?mechanism=&kind=&operation=` | Individual samples over time |
| GET | `/api/metrics/definitions` | Start and end of every measured interval |
| DELETE | `/api/metrics/measurements` | Clear live measurements |
| GET/POST/DELETE | `/api/experiments/runs` | Experiment runs (POST with raw samples) |
| POST | `/api/experiments/runs/import/k6` | Import a k6 `--summary-export` file |

See [docs/experiments.md](../../../docs/experiments.md) for the metric definitions.

## Scenario experiments (runner) and Experiment C

AnalyticsService also **runs** the research experiments (one at a time, in the background) and stores their results:

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/experiments/a` | Experiment A (REST vs RabbitMQ matching): `{ scenario, approaches, rides, intervalMs, restTimeoutMs, outageSeconds, slowDelayMs, outageMode }` |
| POST | `/api/experiments/b` | Experiment B (RabbitMQ vs Kafka distribution): `{ scenario, mechanisms, rides, offlineSeconds, rabbitQueueMode, sharedSubscription }` |
| GET | `/api/experiments/active` | The running experiment and its phase |
| GET | `/api/experiments/runs?experimentType=A&scenarioKey=…&groupId=…&includeDetails=true` | Runs with results, observations and details |
| PUT | `/api/experiments/runs/{id}/notes` | Qualitative notes per run |
| GET/PUT | `/api/findings`, `/api/findings/{key}` | Scenario findings (Experiment C) |
| GET/PUT | `/api/tradeoffs` | Trade-off matrix notes (Experiment C) |

Code: `Experiments/` (`ExperimentARunner`, `ExperimentBRunner`, `ExperimentBInfrastructure`, `RabbitMqInspector`, `ExperimentAnalysis`, `ExperimentCoordinator`, `Findings`).
