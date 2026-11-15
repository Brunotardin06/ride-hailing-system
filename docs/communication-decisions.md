# Communication decisions (v1 → v2)

This document justifies every service-to-service communication of the ride-hailing system.

- **v1** was the first, exploratory implementation. It used RabbitMQ as the default for almost every
  interaction and contained alternative paths so that mechanisms could be compared (Experiments A and B).
- **v2** is the rebuild. Every communication is chosen deliberately, based on the v1 measurements where they exist,
  and on an explicit trade-off analysis where they do not. v2 is implemented as described here (section 4).

Every number below comes from `research/v1-results/` (exported from the v1 analytics database and tool
output). Nothing is estimated. Each decision is marked with its evidence type:

| Evidence type | Meaning |
| --- | --- |
| **Measured** | Supported by v1 experiment runs |
| **Measured (indirect)** | A measured property of the mechanism that applies to this interaction, though this exact interaction was not tested |
| **Reasoned** | Trade-off analysis only; no measurement |

## 1. Decision criteria

| Use… | When |
| --- | --- |
| **Synchronous (REST / gRPC)** | the caller needs the answer to continue · a user is waiting for the result · immediate consistency is required |
| **Message queue (RabbitMQ)** | a command/event with a known consumer · the sender must not depend on the receiver being up · work may be slow and can be done later |
| **Event stream (Kafka)** | facts that several independent consumers read · history must be kept · new consumers must be able to replay |
| **Push (SignalR)** | the server must notify the browser without polling |

Asynchronous messaging is not free. In v1 it required a transactional outbox, idempotent consumers, retries,
a dead-letter queue and handling of intermediate states (eventual consistency). It should therefore be used only
where its benefits are actually needed.

## 2. Evidence from v1

### Test set-up and validity

- Everything ran on one machine with Docker Compose: a single broker of each type, no replication.
- Small samples: Experiment A used 3 rides per run, and Experiment B used 20 or 50 rides (76 or 190 events).
- **Only runs from the final measurement code are used** (2026-11-01 21:50 UTC and later). Earlier runs were made
  while the measurement tooling was still being fixed. Known issues in those early runs:
  - a negative RabbitMQ delivery latency (timestamp taken after the confirm);
  - roughly 500 ms of Kafka latency caused by synchronous offset commits;
  - an invisible queue backlog caused by a prefetch of 10.

  The two exceptions are A1 and B1, which have no later runs; these are reported separately.
- In Experiment A, an "unavailable" Matching Service is **simulated**: the experiment instance returns HTTP 503 and
  does not consume its queue. A separate tool test stopped the real container (see A2).

### Experiment A — Ride → Matching: REST (synchronous) vs RabbitMQ (asynchronous)

| Scenario | Runs | REST | RabbitMQ |
| --- | --- | --- | --- |
| **A1 normal** (5 rides, 1 run each, before the cut-off) | 1 + 1 | 5/5 accepted · HTTP p50 30 ms, p95 296 ms · request→offer p50 14 ms | 5/5 accepted · HTTP p50 7 ms, p95 44 ms · request→offer p50 17 ms |
| **A2 Matching unavailable** (5 s outage) | 11 + 11 | **0/3 accepted in every run** (HTTP 503, p50 10–25 ms) · 0 offers · ride cancelled | **3/3 accepted in every run** (HTTP p50 5–14 ms) · all offers created after recovery · request→offer p50 4.70–4.83 s · last offer 30–53 ms after recovery · 0 duplicates |
| **A3 Matching slow** (1500 ms processing, 1000 ms REST timeout) | 5 + 5 | **0/3 accepted** · 3/3 timed out at ~1.01–1.02 s → ride cancelled, HTTP 503 | **3/3 accepted** (HTTP p50 5–9 ms) · request→offer p50 2.82–2.85 s, max 4.13–4.17 s · queue depth up to 2 |

Real outage (tool test `rabbitmq-recovery`, MatchingService container stopped for 15 s, 5 rides):
5/5 rides queued, **0 lost**. The queue drained 698 ms after restart, and request→offer took ~17.06 s
(it includes the outage).

**What A shows**
- With REST, the availability of Ride Service's "request ride" depends on Matching Service. When Matching was
  down or slow, **no ride could be requested** (0 of 3 in all 16 runs).
- With RabbitMQ the passenger always got a fast confirmation, and every ride was eventually matched without
  duplicates. **Failure became delay**: the time to an offer grew with the outage length, and under slowness a
  backlog formed (later requests waited longer, up to ~4.2 s).
- The guarantee depends on the configuration: a durable queue plus persistent messages (verified in each run:
  `queueDurable=true`, `deliveryMode=2`).

### Experiment B — distributing ride lifecycle events: RabbitMQ vs Kafka

| Scenario | Runs | RabbitMQ | Kafka |
| --- | --- | --- | --- |
| **B1 single consumer** (50 rides = 190 events, before the cut-off) | 1 + 1 | 190/190, 0 duplicates, 0 out of order · delivery p50 0.9 ms | 190/190, 0 duplicates, 0 out of order · delivery p50 2.1 ms |
| **B2 two independent consumers** | 5 + 5 | each consumer got the full stream (76/76) · delivery p50 0.8–1.1 ms | each consumer got the full stream (76/76) · delivery p50 0.9–1.5 ms |
| B2 variant: *shared* subscription (1 run each, 50 rides) | 1 + 1 | events split 95/95 · events of the same ride went to both consumers for **20 of 50 rides** | events split 75/115 · same-ride events stayed on one consumer for **all 50 rides** (key = ride id) |
| **B3 consumer offline, durable queue / committed offset** | 5 + 5 | B missed **0 of 76** · 38 events waited · caught up in 9–40 ms | B missed **0 of 76** · lag 38 · caught up in 70–77 ms |
| B3 variant: **temporary queue** | 4 | B missed **38 of 76** (the queue was deleted when B disconnected) | — |
| **B4 replay for a new consumer** | 9 + 9 | the late consumer received **0** earlier events (0/76 or 0/190) | the late consumer received **all** events (76/76 or 190/190) |

**What B shows**
- For one or more *known* consumers, both deliver everything with ~1–2 ms latency. Performance does not decide between them.
- RabbitMQ only protects an offline consumer when its queue is **durable**: with a temporary queue, 50% of events were lost.
- Only Kafka could give a **new** consumer the history (100% vs 0%).
- With a key per ride, Kafka keeps the events of one ride together and in order across consumers. A shared RabbitMQ queue does not.

### Tool: REST vs gRPC (MatchingService → DriverService driver lookup)

| Test | REST | gRPC |
| --- | --- | --- |
| Benchmark, 200 sequential calls, 6 rounds each (final code) | p50 1.4–2.1 ms, p95 1.8–3.6 ms, 0 errors | p50 1.5–2.5 ms, p95 1.9–4.1 ms, 0 errors |
| k6, 10 virtual users for 15 s (1 run each) | 37,791 requests · p50 3.2 ms, p95 6.4 ms · 2,519 req/s | 28,855 requests · p50 4.0 ms, p95 9.2 ms · 1,922 req/s |

**What it shows:** at this scale (small JSON payload, local network) **gRPC was not faster than REST**. In the
load test it was even slower. A choice for gRPC can therefore **not** be justified on performance in this system.

## 3. Decision per interaction

| # | Interaction | v1 | **v2** | Evidence |
| --- | --- | --- | --- | --- |
| 1 | Frontend → Ride / Driver Service (user actions) | REST | **REST** | Reasoned |
| 2 | Services ⇄ browser (status, offers, chat, driver GPS) | SignalR | **SignalR** | Reasoned |
| 3 | Ride → Matching: new ride request | RabbitMQ (REST optional) | **RabbitMQ** | **Measured** (A1–A3, recovery test) |
| 4 | Matching → Driver: available drivers | REST or gRPC | **gRPC** | Measured (no performance difference) + Reasoned |
| 5 | Matching → Driver: create offer | REST | **gRPC** | Reasoned |
| 6 | Driver accepts → Ride assigns driver | RabbitMQ (`driver.accepted-ride`) | **Synchronous call (gRPC)** | Reasoned |
| 7 | Driver → Ride: offer created | RabbitMQ (`ride.offer-created`) | **Removed** | Reasoned |
| 8 | Driver rejects → Matching tries the next driver | RabbitMQ | **RabbitMQ** | Measured (indirect) (A2, B3) |
| 9 | Ride completed / cancelled → Driver frees the driver | RabbitMQ | **RabbitMQ** (durable queue) | Measured (indirect) (B3) |
| 10 | Status events → Realtime → browser | RabbitMQ | **RabbitMQ** (one queue per consumer) | Measured (indirect) (B2) |
| 11 | Ride lifecycle facts → Analytics | Kafka | **Removed** (Analytics Service dropped) | Reasoned |
| 12 | Driver GPS positions → Realtime + Matching | — (new in v2) | **Kafka** (key = driver id) | Measured (indirect) (B2–B4) + Reasoned |
| 13 | Matching → Ride: no driver found | RabbitMQ (`ride.matching-failed`) | **RabbitMQ** | Measured (indirect) (B3) |

### Justifications

**1 · Frontend → services: REST.** The browser needs an immediate answer (accepted, or an error message).
REST is the native, debuggable choice for a browser. There is no gateway; each call goes to the service that owns
the data.

**2 · Push to the browser: SignalR.** Status changes and offers must reach the user without polling, and the
chat is two-way. SignalR is the standard push mechanism in ASP.NET Core and falls back automatically when
WebSockets are not available.

**3 · Ride → Matching: RabbitMQ.** The passenger does not need the matching result inside the HTTP response; the UI
shows "looking for a driver". Experiment A showed the decisive difference:
- with REST, every ride request failed while Matching was down or slow (0/3 in 16 of 16 runs);
- with a durable queue, every request was accepted in ~5–14 ms and matched later, with nothing lost and no
  duplicates.

*Trade-off accepted:* delay instead of failure (seconds during an outage), a backlog under slowness, and the need
for an outbox plus idempotent handling.

**4 · Matching → Driver, driver lookup: gRPC.** Matching cannot continue without the list of drivers, so the
call must be synchronous. The measurements show **no performance gain** for gRPC (p50 1.5–2.5 ms vs 1.4–2.1 ms
for REST). gRPC is chosen for other reasons:
- a contract-first, strongly typed interface (`.proto`) shared by both services;
- generated client code;
- a clear separation between internal APIs (gRPC) and public APIs (REST).

*Trade-off:* binary payloads are harder to inspect, HTTP/2 is needed, and there is an extra build step.
REST would have been an equally valid choice.

**5 · Matching → Driver, create offer: gRPC.** This is synchronous for the same reason: Matching must know
**immediately** whether the driver could still be reserved, otherwise it tries the next one. It uses the same
mechanism as decision 4, so the Matching → Driver relationship stays on one technology.

**6 · Driver accepts → Ride assigns: synchronous.** This is the main change from v1.

In v1, DriverService confirmed the acceptance and RideService assigned the ride **later**, through a message. In
the meantime the passenger could cancel. The driver had then "accepted" a ride that no longer existed, and
compensation logic was needed (`ride.offer-withdrawn`, refusing late offers).

In v2, DriverService calls RideService synchronously and confirms to the driver only if the assignment
succeeded. This gives:
- immediate consistency;
- an honest answer to the driver;
- no compensation logic.

*Trade-off:* while RideService is down, a driver cannot accept rides. This is acceptable because without
RideService no ride can progress anyway. *Evidence:* reasoned. Not measured in v1; a possible additional experiment.

*Verified in v2 (functional test, not an experiment):* with RabbitMQ paused so that DriverService had not yet
heard of a cancellation, the driver's accept returned **409 "This ride is no longer available"** and the driver
was freed. With RideService stopped, accept returned **503** and the offer stayed pending; after RideService
was back, the same accept succeeded.

**7 · "Offer created" → Ride: removed.** In v1, RideService only stored the time of the first offer, mainly so
that Experiment A could measure "time to offer". It has no business purpose in v2. The passenger is informed
through Realtime (decision 10).

**8 · Driver rejects → Matching: RabbitMQ.** DriverService does not need an answer, it only reports the fact, and
Matching may be busy or briefly down. The same property measured in A2 applies: with a durable queue the
rejection waits and is processed later, instead of the re-matching being lost.

**9 · Ride completed / cancelled → Driver: RabbitMQ with a durable queue.** RideService must not fail (or wait)
when DriverService is unavailable. Freeing the driver a little later is acceptable (eventual consistency).

B3 shows that the queue **must be durable**: with a temporary queue, the offline consumer lost 38 of 76 events.
With a durable queue it lost 0 and caught up in milliseconds.

**10 · Status events → Realtime: RabbitMQ, one queue per consuming service.** Several services produce events and
RealtimeService only forwards them to browsers. With a direct call, Ride and Driver Service would have to know
about RealtimeService and would depend on it being up.

B2 shows that one queue per independent consumer gives each consumer the full stream (76/76). Sharing a queue
would split the events (95/95).

**11 · Ride lifecycle → Analytics: removed.** In v1, Analytics Service had two jobs: it was the experiment hub
(storing measurements, running Experiments A/B) and a Kafka consumer for business statistics. The experiments
are finished and their results are exported to `research/v1-results/`, so the first job is gone. The second
had no visible use in the product (its dashboard was part of the experiment pages). Removing it saves one
service and one database. Kafka stays in the system, for a use that fits it better (decision 12).

**12 · Driver GPS positions: Kafka, key = driver id.** The driver's app (simulated in the browser) sends its
position every ~2 s over its existing SignalR connection. RealtimeService writes it to the topic
`driver-locations`. Two services read the same positions, each as its own consumer group:
- RealtimeService pushes the position to the passenger of that ride (live map, distance, ETA);
- MatchingService keeps the last position per driver and offers a ride to the **nearest** available driver.

Why Kafka and not the alternatives:

| Requirement of location data | Kafka | RabbitMQ | gRPC / REST |
| --- | --- | --- | --- |
| Several independent readers of the same data | each consumer group reads the same log (B2: full stream for each) | one queue per consumer, every message copied | the producer would call every consumer itself (coupling) |
| Order per driver (no "jump back") | guaranteed per partition; key = driver id (B2 variant: same key stayed together and in order) | only within one queue with one consumer | — |
| High frequency, small messages | append-only log, batching | per-message tracking and ack | one call per position per consumer |
| A restarted consumer catches up | resumes from its committed offset; history available (B3, B4: 100%) | consumed messages are gone (B4: 0%) | no |

Location is a **stream that several services read**, not a **task for one service**: that is exactly the
difference between Kafka and RabbitMQ.

*Trade-off:* one more broker to run. At demo scale (a few drivers) RabbitMQ would also cope with the volume;
the choice follows from the *shape* of the data (stream, order per key, multiple readers), which does not change
with scale. *Evidence:* the Kafka properties were measured in v1 (B2–B4); this interaction itself was not.

*Verified in v2 (functional test):* Emma (waited longest) at the airport, Lucas at TU/e, pickup at Fontys →
Matching logged "Emma 7.4 km, Lucas 0.7 km" and offered the ride to **Lucas**. Both consumer groups showed
the same offsets with lag 0 (Lucas's 8 positions all on one partition, Emma's 2 on another).

**13 · Matching → Ride, no driver found: RabbitMQ.** Matching only reports a fact and needs no answer.
RideService marks the ride `NoDriverAvailable` and ignores the message if the ride is no longer waiting
(e.g. the passenger cancelled meanwhile), which makes it safe to process late or twice.

## Service set in v2

| Change | Reason |
| --- | --- |
| **Analytics Service removed** | See decision 11. |
| **Driver location added to Realtime Service, not as a new service** | The driver's browser already has a SignalR connection to Realtime; location, push and chat are the same kind of work (live, connection-based, no business rules, scales with connected users). A separate Location Service becomes worthwhile when positions must be **stored** (route history, distance-based fares). |
| **Driver Service not split into Drivers + Offers** | Creating an offer and reserving the driver (`Available → Offered`) must happen in **one database transaction**; otherwise two rides could reserve the same driver. Split, this would need a distributed transaction (saga). |
| Matching Service stays stateless | Everything it needs arrives in the messages (ride data, drivers already tried) or via gRPC; positions are a cache that refills from Kafka. |

## 4. Resulting v2 architecture (summary)

Diagram: `docs/architecture-v2.drawio`.

```
Browser ──REST──► Ride Service, Driver Service
Browser ◄─SignalR─► Realtime Service            (notifications, chat; driver tab sends GPS)

Ride Service ──RabbitMQ (ride.requested)──► Matching Service
Matching Service ──gRPC (lookup, create offer)──► Driver Service
Driver Service ──gRPC (assign accepted ride)──► Ride Service
Driver Service ──RabbitMQ (driver.rejected-ride)──► Matching Service
Matching Service ──RabbitMQ (ride.matching-failed)──► Ride Service
Ride Service ──RabbitMQ (ride.completed / ride.cancelled)──► Driver Service
Ride + Driver Service ──RabbitMQ (status / offer events)──► Realtime Service
Realtime Service ──Kafka (driver-locations, key = driverId)──► Realtime Service, Matching Service
```

Reliability mechanisms used with the asynchronous paths:

| Mechanism | Where | Purpose |
| --- | --- | --- |
| Transactional outbox | Ride, Driver | event saved in the same transaction as the data; published afterwards (verified: ride requested while RabbitMQ was stopped → 201, event published ~4 s after the broker returned) |
| Inbox / duplicate cache | Ride, Driver (table) · Matching, Realtime (memory) | at-least-once delivery → idempotent handling |
| Durable queues, persistent messages, publisher confirms | all RabbitMQ paths | nothing lost while a consumer is down (B3) |
| Retries + dead-letter queue per queue | all consumers | a failing message never blocks a queue |
| Idempotency key `rideId:attempt` | gRPC CreateOffer | a retried Matching message never creates a second offer |
| Deadlines (5 s) | all gRPC calls | a synchronous call never waits forever |
| Offer expiry (30 s) | Driver | an unanswered offer frees the driver and triggers the next match |

## 5. Limitations

- Small samples and a single machine: the results show **behaviour** (accepted / lost / replayed, order of
  magnitude of latency), not production performance.
- Single broker instances without replication; broker failure was not tested.
- In Experiment A, unavailability was mostly simulated (HTTP 503). The real container stop was tested once per
  outage length with the recovery tool.
- Decisions 1, 2, 5, 6, 7 and 11 are based on reasoning, not measurement; decision 12 on measured Kafka
  properties applied to a new interaction. Decision 6 is the best candidate for an additional experiment
  (synchronous vs asynchronous acceptance under a concurrent passenger cancellation).

Known limitations of the v2 implementation (accepted trade-offs):
- **Accept spans two databases.** RideService assigns the ride (gRPC) before DriverService saves the accepted
  offer. If DriverService crashed in between, the ride would be `Assigned` while the offer stays `Pending`.
  A full fix needs a saga or a reconciliation job.
- **Realtime state is in memory** (connections, chat history, "which passenger follows which driver"). It is lost
  on restart, and a second Realtime instance would need a SignalR backplane (e.g. Redis) and its own Kafka
  consumer group.
- **Simulated GPS:** straight lines instead of roads, accelerated speed, and only drivers with an open browser
  tab report a position (others fall back to "longest waiting"). A driver can also use the device's real GPS.
- **External map services:** map tiles (OpenStreetMap) and address search (Nominatim) are public services; typed
  addresses leave the system, and Nominatim allows at most 1 request per second (search runs only on Enter).

## Data

| File | Content |
| --- | --- |
| `research/v1-results/experiment-runs.json` | all 131 runs with parameters, results and automatic observations |
| `research/v1-results/experiment-runs-summary.csv` | one row per run (for spreadsheets) |
| `research/v1-results/measurements-raw.csv` | 22,932 individual measurements |
| `research/v1-results/scenario-findings.json` | scenario definitions (question, requirements, hypothesis) |
| `research/v1-results/tool-results/` | k6 REST/gRPC load test and the RabbitMQ recovery test |
