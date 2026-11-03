# Experiment guide

This guide explains how to run the research experiments, what their results mean, and what can and cannot be concluded from them. The research design (questions, hypotheses, methodology, limitations) is in [research-design.md](research-design.md).

All experiments run on the full Docker stack (`docker compose up -d --build`). They are started from the **Experiment dashboard** (http://localhost:5173/experiments) or via the AnalyticsService API. Only one experiment can run at a time; each run is stored with its parameters, status (Completed / Incomplete / Failed), quantitative results, per-ride or per-consumer details, a timeline and factual observations.

## Isolation: experiments do not disturb the demo application

| Resource | Demo rides | Experiment A | Experiment B |
| --- | --- | --- | --- |
| Matching instance | `matching-service` (queue `matching-service`) | `matching-experiment` (queue `matching-experiment`, prefetch 1) | — |
| Drivers | 6 demo drivers | 20 hidden experiment drivers | — |
| Rides | normal | flagged with `ExperimentRunId`, not published to the business Kafka topic, cancelled after the run | — |
| Broker resources | exchange `ridehailing.events`, topic `ride-events` | same exchange, own routing key `experiment.ride-requested` | exchange and topic `experiment.ride-lifecycle`, per-run queues and consumer groups (deleted afterwards) |

## Experiment A: synchronous (REST) vs asynchronous (RabbitMQ)

**Dashboard:** *A · Sync vs async* → choose a scenario → tick REST and/or RabbitMQ → set parameters → **Run**. Results, charts, per-ride details and observations appear when the run finishes; *Earlier runs* lists previous runs.

**API:**

```bash
curl -X POST http://localhost:5005/api/experiments/a -H "Content-Type: application/json" \
  -d '{"scenario":"A2-unavailable","approaches":["REST","RabbitMQ"],"rides":5,"intervalMs":300,"restTimeoutMs":3000,"outageSeconds":10}'
# → {"groupId": "...", "runIds": [...]};  then:
curl "http://localhost:5005/api/experiments/runs?groupId=<groupId>&includeDetails=true"
```

| Parameter | Meaning |
| --- | --- |
| `scenario` | `A1-normal`, `A2-unavailable`, `A3-slow` |
| `rides`, `intervalMs` | number of ride requests and the fixed interval between them (open workload) |
| `restTimeoutMs` | how long RideService waits for MatchingService (REST approach) |
| `outageSeconds` | A2: how long the matching instance is unavailable |
| `slowDelayMs` | A3: extra processing time per experiment ride inside MatchingService |
| `outageMode` | A2: `simulated` (default) or `external` (used by the real-outage script) |

**What happens in a run:** the experiment driver pool is reset → (A2) the matching instance is made unavailable → ride requests are sent through RideService exactly like a passenger would (with `matchingMode` = Rest or RabbitMq) → the queue is sampled every 250 ms → (A2) the instance becomes available again → the runner waits for each ride's business outcome → results are computed and the experiment rides are cancelled.

**Real outage instead of the simulated one:**

```bash
node experiments/experiment-a-real-outage.mjs --rides 5 --outage 15
```

This stops the `matching-experiment` container for real (`docker compose stop`), runs A2 per approach in `external` mode, and starts the container again. Demo rides are unaffected.

### Reading the results

| Result | Meaning |
| --- | --- |
| Accepted / failed requests | did RideService accept the ride request (HTTP 2xx), or did the passenger get an error? |
| **Ride offers created** | the **business outcome**: matching produced an offer for a driver. This is what the approaches are compared on. |
| HTTP response p50/p95 | how long the passenger waited for RideService's answer. With RabbitMQ this does **not** include matching. |
| Request → offer p50/p95/max | time until the offer existed (ride requested at RideService → offer created at DriverService). |
| Publish confirm p50 | RabbitMQ only: time until the broker confirmed it stored `experiment.ride-requested`. Not matching time. |
| Pending at the end | accepted rides that were not matched before the run ended (asynchronous work still waiting). |
| Max messages waiting | backlog in queue `matching-experiment` (ready messages; with prefetch 1 at most one more is in flight). |
| Queue durable / delivery_mode | read from the broker during the A2 outage: `1` / `2` means the waiting work would survive a broker restart. |
| Timeouts, aborted work, late offers | what happened to synchronous requests that took too long, and to the work behind them. |
| Duplicate offers | rides with more than one offer (should be 0: each ride uses exactly one path and offer creation is idempotent). |

**What you can conclude:** how each approach behaves *in these scenarios*: whether the passenger gets an answer, whether work survives an outage, how slowness shows up (errors vs delay), and which extra states/handling each approach needs.

**What you cannot conclude:** general performance rankings (one host, tiny workloads, one consumer), behaviour under real network failures or at scale, or that one approach is "better". The time-to-offer of RabbitMQ in A2/A3 is dominated by the outage/backlog, not by RabbitMQ's speed.

## Experiment B: RabbitMQ (message queuing) vs Kafka (event streaming)

**Dashboard:** *B · Messaging vs streaming* → choose a scenario → tick RabbitMQ and/or Kafka → parameters → **Run**. The run shows a comparison table, events per consumer, a consumer table, the configuration each mechanism needed, and observations.

**API:**

```bash
curl -X POST http://localhost:5005/api/experiments/b -H "Content-Type: application/json" \
  -d '{"scenario":"B3-offline","mechanisms":["RabbitMQ","Kafka"],"rides":50,"offlineSeconds":5,"rabbitQueueMode":"durable"}'
```

| Parameter | Meaning |
| --- | --- |
| `scenario` | `B1-single`, `B2-multiple`, `B3-offline`, `B4-replay` |
| `rides` | number of rides; each produces 3–4 events (every 5th ride is cancelled) |
| `sharedSubscription` | B2: both consumers use the same queue / consumer group (competing consumers) |
| `offlineSeconds` | B3: time over which events are published while consumer B is offline |
| `rabbitQueueMode` | B3: `durable` (queue survives the consumer) or `temporary` (exclusive + auto-delete) |

**Set-up per run:** RabbitMQ: fanout exchange `experiment.ride-lifecycle`, one queue per subscriber, persistent messages, publisher confirms, manual ack, prefetch 50. Kafka: topic `experiment.ride-lifecycle` (3 partitions, key = ride id), producer `acks=all`, idempotent, `linger.ms=0`; one consumer group per subscriber, offset stored after processing and committed in the background. Producer and consumers run inside AnalyticsService, so **delivery latency is measured on one monotonic clock**.

### Reading the results

| Result | Meaning |
| --- | --- |
| Unique / missing / duplicates per consumer | did each subscriber get every event exactly once? |
| Out of order (per ride) | events of one ride arriving in the wrong sequence at a consumer |
| Rides split across consumers (B2 shared) | whether events of one ride were handled by two different consumers |
| Delivery p50/p95 | publish call → consumer callback (for B3, B's late events include the offline time) |
| Backlog while offline (B3) | messages in B's queue (RabbitMQ) or consumer-group lag (Kafka) |
| B recovery time (B3) | from B's restart (reconnect / rejoin) until its backlog was processed |
| Events replayed (B4) | how many earlier events a consumer that started later could still read |

**What you can conclude:** how the consumption models differ in practice (delete-on-ack vs retained log with offsets), which configuration provides independent consumption, survival of consumer downtime, and replay.

**What you cannot conclude:** broker throughput limits, behaviour with replication or failures of the broker itself, or anything about RabbitMQ Streams (not tested). "RabbitMQ cannot replay" is only true for the tested configuration.

## Experiment C: architectural decisions

**Dashboard:** *C · Architectural decisions*. For every scenario (A1–A3, B1–B4) you see the requirements, the hypothesis (marked as such), expected properties from theory, and the **measured evidence of the latest completed run**. Write your observations, advantages, disadvantages, recommendation and limitations, and set the evidence status. The trade-off matrices show, per decision criterion and approach, the measured evidence (if any run measured it) and your assessment, labelled *measured*, *expected* or *not tested*.

API: `GET/PUT /api/findings`, `GET/PUT /api/tradeoffs`; qualitative notes per run: `PUT /api/experiments/runs/{id}/notes`.

## Reproducing the experiments

1. `docker compose up -d --build`, wait until all services are healthy.
2. Optionally clear old live metrics (*Additional tools → Live communication metrics → Clear*).
3. Run each scenario at least **3 times** with the same parameters; use more rides (A: 10–20, B: 100+) for more stable numbers. The first REST request after a restart includes connection set-up.
4. Note the environment (machine, Docker Desktop CPU/memory) and the parameters stored with each run.
5. Use the automated checks: `cd tests/e2e && npm test` (tests 10–13 run A2, A3, B2–B4 and C and verify the expected behaviour), `npm run test:ui-experiments` (runs A2 and B4 from the dashboard).

---

# Supplementary tools and live metrics

The tools below come from the first version of the project. They measure individual mechanisms (REST vs gRPC latency, RabbitMQ/Kafka/SignalR delivery during demo rides) and are useful technical evidence, but they do not answer the research question on their own. They are under *Additional tools* on the dashboard.

AnalyticsService keeps two responsibilities: **business event analytics** (Kafka ride events → statistics, event history, Kafka inspection) and **experiment metrics** (live measurements, tool runs and the scenario experiments above).

AnalyticsService has two responsibilities:

- **A. Business event analytics:** ride lifecycle events from Kafka → counts, rates, durations and event history (*Additional tools → Business event analytics / Kafka stream inspector*).
- **B. Experiment metrics:** communication measurements reported by the services, plus results of repeatable experiment runs (*Additional tools → Live communication metrics / REST vs gRPC benchmark & k6 runs*).

No performance numbers in this repository are invented: everything on the dashboard was measured on the machine running the system. Results depend on the machine, Docker resources and load, so always report the scenario together with the numbers.

### How measurements are collected

```
service code ──Record(mechanism, kind, operation, ms, success)──► MetricsReporter (in-memory buffer)
                                                                        │ every 2 s, batched
                                                                        ▼
                                              POST /api/metrics/measurements (AnalyticsService) → table Measurements
```

- `MetricsReporter` (`src/shared/.../Metrics`) never blocks the measured code; if AnalyticsService is down, the batch is dropped and logged (metrics must not break the ride flow).
- AnalyticsService records its own Kafka measurements directly in its database.
- **Experiment runs** are stored with all their samples (`ExperimentRuns` + `Measurements` with a `RunId`) or imported from a k6 summary.
- Statistics: average, p50/p95/p99 (nearest-rank percentile, `Statistics.cs`) and max over **successful** samples; error rate = failed / all samples.
- Throughput of a run = successful samples ÷ active execution time (wall-clock time of the measured phases). For live metrics the dashboard shows the *observed rate* (samples per second between the first and last sample). That is the rate the demo produced, not the capacity.

### What each metric measures

A measurement is identified by **mechanism + kind + operation**. Only rows with the same kind *and* operation measure the same interval and may be compared. The dashboard enforces this: the latency chart shows one kind at a time, and the run comparison refuses runs with different kind/operation. The same table is served by `GET /api/metrics/definitions`.

| Mechanism | Kind | Operation | Start of interval | End of interval | Measured by |
| --- | --- | --- | --- | --- | --- |
| REST | call | `driver-lookup` | MatchingService calls `HttpClient` GET `/api/drivers/available` | JSON response deserialised into objects | MatchingService |
| gRPC | call | `driver-lookup` | MatchingService calls the gRPC client `GetAvailableDrivers` | Protobuf reply received and mapped to the same objects | MatchingService |
| REST / gRPC | call | `driver-lookup (k6 client)` | k6 sends the request (`http_req_duration` / `grpc_req_duration`) | k6 received the full response | k6 (imported) |
| RabbitMQ | delivery | `<event> → <queue>` | publisher sets `PublishedAt` right before `BasicPublish` | consumer callback invoked, before handling | consuming service |
| RabbitMQ | processing | `<event> @ <queue>` | consumer callback invoked | handler finished, before the ack (failed attempts = errors) | consuming service |
| RabbitMQ | recovery | `ride request → offer (matching-service down)` | `POST /api/rides` returned 201 while MatchingService was stopped | offer visible via DriverService REST (polled every 100 ms) | `rabbitmq-recovery.mjs` |
| Kafka | delivery | `<event> → analytics-service` | RideService outbox dispatcher sets `PublishedAt` right before `ProduceAsync` | AnalyticsService `Consume()` returned the record | AnalyticsService |
| Kafka | processing | `<event> @ analytics-service` | record returned by `Consume()` | event stored in PostgreSQL, before the offset commit | AnalyticsService |
| SignalR | ack-rtt | `notification:<type>`, `chat-message` | RealtimeService sends the message to the SignalR group | RealtimeService receives the client's `Acknowledge(id)` | RealtimeService |

Notes on interpretation:

- **REST vs gRPC** are compared on exactly the same operation: same DriverService query (`DriverAvailabilityQuery`), same parameters (`max=5`, no exclusions), same database. Only transport and serialisation differ (HTTP/1.1 + JSON vs HTTP/2 + Protobuf over h2c).
- **RabbitMQ/Kafka delivery** uses the clocks of two processes. They run on the same host (or in containers sharing the host kernel clock), so skew is negligible locally; on separate machines it would not be.
- **RabbitMQ delivery includes queueing time.** While a consumer is down, delivery times grow by the length of the outage; that is the point of the recovery experiment, not a performance regression.
- **Kafka delivery includes consumer-group effects.** After a consumer is killed without leaving its group, Kafka waits for the session timeout (set to 10 s) before reassigning partitions; the first deliveries after such a restart are slow. Clear the live metrics after restarts if you want steady-state numbers.
- **SignalR ack-rtt is a round trip** (server → browser → server). Both timestamps come from the server clock, so there is no clock skew. It includes the browser's handling of the message. A message that is not acknowledged within 15 s counts as a failed delivery (only measured when a client was connected to the target group).
- **Delivery (async) and call (sync) latencies measure different things** and must not be ranked against each other. A message delivery of 3 ms and a REST call of 3 ms are not "equally fast" in any meaningful sense.

### Running the experiments

Start the full stack in Docker first (`docker compose up -d --build`). Clear old live data on the dashboard (*Additional tools → Live communication metrics → Clear live metrics*) or with `curl -X DELETE http://localhost:5005/api/metrics/measurements` before a measurement session.

#### 1. Live metrics from the ride workflow

Run the demo (see [demo-guide.md](demo-guide.md)) or the automated end-to-end tests (`cd tests/e2e && npm test`). Every ride produces RabbitMQ, Kafka, SignalR and REST/gRPC measurements. Switch the transport for live matching on the dashboard (*Additional tools → REST vs gRPC benchmark & k6 runs → Use gRPC*) or with `DRIVER_LOOKUP_MODE=Grpc` in `.env`.

#### 2. REST vs gRPC: .NET client benchmark (MatchingService → DriverService)

Dashboard: *Additional tools → REST vs gRPC benchmark & k6 runs → Run REST vs gRPC benchmark*, or:

```bash
curl -X POST http://localhost:5003/api/experiments/driver-lookup \
  -H "Content-Type: application/json" \
  -d '{"iterations": 1000, "warmup": 100, "concurrency": 1, "rounds": 4, "label": "baseline c1"}'
```

- Both clients are warmed up first (connection set-up, JIT); warm-up samples are discarded.
- Modes alternate per round (REST, gRPC, gRPC, REST, …) so neither always runs first.
- `concurrency` = number of parallel callers (try 1, 8, 32 to see how the protocols behave under load).
- Result: two runs (REST and gRPC) with all samples, stored in AnalyticsService.

#### 3. REST vs gRPC: k6 load test (k6 → DriverService)

```bash
node experiments/run-k6-comparison.mjs --vus 10 --duration 30s --label "k6 10 VUs"
```

Runs `experiments/k6/driver-lookup-rest.js` and `driver-lookup-grpc.js` in the `grafana/k6` Docker image on the compose network (no local k6 installation needed), writes the summaries to `experiments/results/` and imports them (`POST /api/experiments/runs/import/k6`). Imported runs also record **bytes received/sent per call** (payload size incl. protocol overhead). k6 runs use operation `driver-lookup (k6 client)` and are compared only with each other.

You can import any other k6 summary manually:

```bash
k6 run --summary-export=summary.json my-test.js
curl -X POST http://localhost:5005/api/experiments/runs/import/k6 -H "Content-Type: application/json" \
  -d "{\"name\":\"my run\",\"mechanism\":\"REST\",\"operation\":\"driver-lookup (k6 client)\",\"scenario\":\"...\",\"summary\": $(cat summary.json)}"
```

#### 4. RabbitMQ failure and recovery

```bash
node experiments/rabbitmq-recovery.mjs --rides 5 --outage 10
```

Stops the MatchingService container, requests N rides (RideService keeps accepting them, the messages wait in queue `matching-service`), restarts MatchingService and measures, per ride, the time until its offer exists. Reports queued messages, drain time after the restart and lost messages (expected: 0). Stored as a run with kind `recovery`. Use an outage of at least 10 s: the RabbitMQ management statistics used to read the queue depth lag several seconds behind.

#### 5. RabbitMQ retries and dead-lettering

Dashboard: *Additional tools → REST vs gRPC benchmark & k6 runs → Fail next 2 (retry)* or *Fail next 4 (dead-letter)*, then request a ride. Or:

```bash
curl -X POST http://localhost:5003/api/experiments/fail-next -H "Content-Type: application/json" -d '{"count": 4}'
```

- 2 injected failures (< `MaxRetries` 3): the message is re-published with `x-retry-count` 1, 2 and succeeds; the ride is matched about 1.5 s later. The failed attempts appear as `processing` errors for `ride.requested @ matching-service`.
- 4 injected failures: after the 3rd retry the message is rejected and RabbitMQ moves it to `matching-service.dlq` (visible in the management UI). The ride stays `Requested` until the passenger cancels.

#### 6. Kafka: independent consumers and replay

- *Additional tools → Kafka stream inspector* → **Read latest 25**: a brand-new consumer group reads the topic directly; the `analytics-service` group's offsets are untouched.
- Partition table: committed offset and lag of `analytics-service`. Stop the analytics container (`docker compose stop analytics-service`), complete a few rides, and watch the lag grow; start it again and the consumer catches up from its committed offset without losing events.
- Full replay (rebuild analytics from the log):

  ```bash
  docker compose stop analytics-service
  docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 \
    --group analytics-service --topic ride-events --reset-offsets --to-earliest --execute
  docker compose start analytics-service
  ```

  All events are delivered again; the unique EventId index prevents duplicates in the statistics (the log shows "Duplicate Kafka event … ignored").

### Reporting checklist

For each result, write down: the scenario (tool, iterations/VUs, concurrency, duration), the environment (machine, Docker Desktop resources, all containers on one host), the exact metric definition (table above), and avg/p50/p95/p99, throughput and error rate. Repeat runs (at least 3) and report variation; single runs on a laptop are noisy.
