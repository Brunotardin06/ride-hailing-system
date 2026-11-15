# Portfolio brief — Personal Experiment 1: Communication Between Services

> **For the AI assistant reading this:** you are helping a Fontys ICT student (Complex Software Systems
> semester) write the portfolio chapter for *Personal Experiment 1*. Everything below is the factual basis.
> Rules:
> 1. **Never invent numbers, runs or results.** Every number in this brief comes from measured data
>    (`research/v1-results/`) or from functional tests the student ran. If something is not here, say it was
>    not measured.
> 2. Keep the distinction between **Measured**, **Measured (indirect)** and **Reasoned** (section 7).
> 3. Performance numbers are *supporting evidence*, never the conclusion. The point of the experiment is
>    *which approach fits which interaction, and why*.
> 4. Write in clear, simple academic English. The teacher wants to see **justified decisions** and
>    **reflection on learning**, not marketing.
> 5. The student decides the final wording; propose, don't overwrite.

---

## 1. Student context (from the Personal Development Plan)

- Good base in full-stack development; wants to grow in **software architecture, distributed systems,
  scalable/resilient infrastructure**, and in **meaningful LLM integration** (group project: *LLM Judge*).
- Main semester objective, in the student's words: *"become more confident in making technical decisions and
  being able to explain why a certain architectural or technological choice is appropriate for a specific
  situation."*
- Feedback plan: ask feedback continuously; review the PDP each sprint and adjust.
- **Feedback already incorporated:** the original PE1 idea was broader (scalability, resilience, Kubernetes…).
  After discussing it with the teachers, the scope was **reduced to communication between services**. The
  ride-hailing system stays the context for later experiments (scalability, resilience, Kubernetes, observability).
- v1 was **demonstrated to the teachers**.

## 2. The experiment

**Main research question**
> How do different communication approaches between services affect the behaviour and design of a
> distributed ride-hailing system?

| Sub-question | Answered by |
| --- | --- |
| **SQ1** When is synchronous communication appropriate? | Experiment A + v2 decisions |
| **SQ2** When is asynchronous communication more appropriate? | Experiment A (+ B3) |
| **SQ3** What are the practical differences between RabbitMQ and Kafka? | Experiment B |
| **SQ4** What trade-offs do the different approaches introduce? | A + B, analysed in Experiment C and the v2 decisions |

Guiding principle (from the PDP): *"The objective is not to determine which technology is always better, but to
understand which communication approach is more suitable depending on the situation and system requirements."*

**Approach in two iterations**

| | v1 (exploration + measurement) | v2 (justified rebuild) |
| --- | --- | --- |
| Goal | Build the system with alternative communication paths so they can be **compared** | Rebuild from scratch, choosing **each** communication deliberately from the v1 evidence |
| Services | Ride, Driver, Matching, Realtime, Analytics (5) | Ride, Driver, Matching, Realtime (4) |
| Style | RabbitMQ used almost everywhere + switchable REST/gRPC/Kafka paths for experiments | One mechanism per interaction, each with a written justification |
| Output | 131 experiment runs, 22,932 measurements, experiment dashboard | Working system + decision document + architecture diagram |

This "measure first, then decide and rebuild" cycle is itself a strong portfolio story: v1 deliberately
over-used async messaging, the experiments showed where that was justified and where not, and v2 corrects it
(e.g. decision 6 below).

## 3. Methodology (v1 experiments)

- **Same business operation, only the communication differs.** Experiment A used the same matching logic for REST
  and RabbitMQ; Experiment B used an identical generated event set for RabbitMQ and Kafka.
- **Open workload:** ride requests at a fixed interval, independent of responses (like real passengers).
- **Isolation:** experiments used their own matching instance, a hidden pool of 20 experiment drivers, and
  per-run queues/consumer groups, so they never disturbed the demo.
- **Failure situations by design:** Matching made unavailable (simulated 503 + consumer cancelled, and a real
  container stop with a separate tool), Matching slowed down, consumers taken offline.
- **Verify, don't assume:** queue durability and message persistence were read from the broker during outages;
  duplicates were counted at DriverService.
- **Evidence before conclusions:** each run stored parameters, results, per-ride details, a timeline and
  automatically generated factual observations. Conclusions were written separately (Experiment C).
- **Data validity:** only runs from the final measurement code (2026-11-01 21:50 UTC and later) are used.
  Earlier runs were made while the tooling was being fixed (known issues: a negative RabbitMQ delivery latency,
  ~500 ms Kafka latency from synchronous offset commits, a backlog hidden by prefetch 10). Exceptions: A1 and B1
  have no later runs and are reported separately.
- Environment: one laptop, all containers in Docker Compose, single broker instances without replication.

## 4. Experiment results (v1, measured)

### Experiment A — Ride → Matching: REST (synchronous) vs RabbitMQ (asynchronous)

| Scenario | Runs (REST + RabbitMQ) | REST | RabbitMQ |
| --- | --- | --- | --- |
| **A1 normal** (5 rides, before cut-off) | 1 + 1 | 5/5 accepted · HTTP p50 30 ms, p95 296 ms · request→offer p50 14 ms | 5/5 accepted · HTTP p50 7 ms, p95 44 ms · request→offer p50 17 ms |
| **A2 Matching unavailable** (5 s outage) | 11 + 11 | **0/3 accepted in every run** (HTTP 503, p50 10–25 ms) · 0 offers · ride cancelled | **3/3 accepted in every run** (HTTP p50 5–14 ms) · all offers created after recovery · request→offer p50 4.70–4.83 s · last offer 30–53 ms after recovery · 0 duplicates |
| **A3 Matching slow** (1500 ms processing, 1000 ms REST timeout) | 5 + 5 | **0/3 accepted** · 3/3 timed out at ~1.01–1.02 s → ride cancelled, HTTP 503 | **3/3 accepted** (HTTP p50 5–9 ms) · request→offer p50 2.82–2.85 s, max 4.13–4.17 s · queue depth up to 2 |

Real outage (tool test, MatchingService container stopped 15 s, 5 rides): 5/5 rides queued, **0 lost**; the
queue drained 698 ms after restart; request→offer ~17.06 s (includes the outage).

Observations from early verification runs (single runs, *before* the cut-off — usable as qualitative
observations, not as numbers): with a **real** container stop, REST requests sometimes waited for the full
3 s timeout instead of failing fast (the simulated 503 understated the passenger's wait); in A3, the REST
timeout also **aborted the matching work** inside MatchingService (the closed HTTP connection cancelled it),
so the work was wasted.

**What A shows:** with REST, "request a ride" depends on Matching being up and fast — 0 of 3 accepted in all
16 unavailable/slow runs. With a durable RabbitMQ queue the passenger always got a fast confirmation and every
ride was eventually matched without duplicates: **failure became delay**. The guarantee depends on
configuration (durable queue + persistent messages, verified in every run).

### Experiment B — distributing ride lifecycle events: RabbitMQ vs Kafka

| Scenario | Runs | RabbitMQ | Kafka |
| --- | --- | --- | --- |
| **B1 single consumer** (50 rides = 190 events, before cut-off) | 1 + 1 | 190/190, 0 duplicates, 0 out of order · delivery p50 0.9 ms | 190/190, 0 duplicates, 0 out of order · delivery p50 2.1 ms |
| **B2 two independent consumers** | 5 + 5 | each consumer got the full stream (76/76) · p50 0.8–1.1 ms | each consumer got the full stream (76/76) · p50 0.9–1.5 ms |
| B2 variant: *shared* subscription (50 rides) | 1 + 1 | events split 95/95 · events of the same ride went to both consumers for **20 of 50 rides** | split 75/115 · same-ride events stayed on one consumer for **all 50 rides** (key = ride id) |
| **B3 consumer offline**, durable queue / committed offset | 5 + 5 | missed **0 of 76** · 38 waited · caught up in 9–40 ms | missed **0 of 76** · lag 38 · caught up in 70–77 ms |
| B3 variant: **temporary queue** | 4 | missed **38 of 76** (queue deleted when the consumer disconnected) | — |
| **B4 replay for a new consumer** | 9 + 9 | late consumer received **0** earlier events | late consumer received **all** events (76/76 or 190/190) |

**What B shows:** for known consumers both deliver everything in ~1–2 ms, so performance does not decide.
RabbitMQ protects an offline consumer **only with a durable queue** (temporary queue: 50 % lost). Only Kafka
gives a **new** consumer the history (100 % vs 0 %). With a key, Kafka keeps one entity's events together and
in order; a shared RabbitMQ queue does not.

Configuration lessons found during the work (good SQ3/SQ4 material): a RabbitMQ prefetch of 10 hid the backlog
inside the consumer (invisible as "ready" messages); a Kafka consumer committing every offset synchronously
reached only ~140 events/s with p50 ≈ 540 ms — committing in the background brought p50 to ≈ 2 ms. **The
configuration, not the technology, caused the latency.**

### Tool test — REST vs gRPC (Matching → Driver, driver lookup)

| Test | REST | gRPC |
| --- | --- | --- |
| Benchmark, 200 sequential calls, 6 rounds each | p50 1.4–2.1 ms, p95 1.8–3.6 ms, 0 errors | p50 1.5–2.5 ms, p95 1.9–4.1 ms, 0 errors |
| k6 load test, 10 virtual users, 15 s (1 run each) | 37,791 requests · p50 3.2 ms, p95 6.4 ms · 2,519 req/s | 28,855 requests · p50 4.0 ms, p95 9.2 ms · 1,922 req/s |

**What it shows:** at this scale **gRPC was not faster than REST** (slower under load). Choosing gRPC can
therefore **not** be justified by performance here. *(Good reflection point: the common expectation that gRPC
is faster was contradicted by the data at this scale.)*

## 5. Draft answers to the research questions

**SQ1 — When is synchronous communication appropriate?**
When the caller **cannot continue without the answer** and needs it **now**, or when **immediate consistency**
matters. In v2: the browser's actions (REST: the user waits for "accepted" or an error), Matching asking
Driver "who is available / was the offer created?" (gRPC: the next step depends on it), and Driver asking Ride
"assign this ride" when a driver accepts (gRPC: the driver must know immediately whether the ride still
exists). The cost is **temporal coupling**: Experiment A showed that a synchronous dependency makes the caller
fail when the callee is down or slow (0/3 accepted in 16/16 runs). So synchronous calls should be kept to
places where that coupling is acceptable — e.g. without RideService no ride can progress anyway.

**SQ2 — When is asynchronous communication more appropriate?**
When the sender **does not need the result to continue**, the receiver may be **down or slow**, and the work
**may happen a little later**. Experiment A: with RabbitMQ every request was accepted in ~5–14 ms and matched
after recovery, nothing lost, no duplicates — failure became delay. B3: a durable queue kept 100 % of events
for an offline consumer. The price: eventual consistency (a "looking for a driver" state), plus reliability
machinery — transactional outbox, idempotent consumers, retries, dead-letter queues.

**SQ3 — Practical differences between RabbitMQ and Kafka?**
RabbitMQ **delivers** messages to queues and deletes them after acknowledgement — ideal for **work/commands
for a known consumer** (routing by key, per-message ack, retries, DLQ). Kafka **appends** to a retained log;
every consumer group reads it independently with its own offset — ideal for **facts read by several
consumers**, **replay** (B4: 100 % vs 0 %) and **ordering per key** (B2 shared: 0 rides split vs 20/50).
Latency for normal delivery was similar (~1–2 ms). Both lose or keep data depending on configuration
(temporary vs durable queue; commit strategy).

**SQ4 — Trade-offs?**

| Criterion | Synchronous (REST/gRPC) | RabbitMQ | Kafka |
| --- | --- | --- | --- |
| Immediate answer | yes | no (pending state) | no |
| Temporal coupling | high — caller fails if callee is down (A2/A3) | low — messages wait (A2, B3) | low — consumer resumes from offset (B3) |
| Failure behaviour | error / timeout, work lost or aborted | delay instead of failure | delay; history kept |
| Delivery guarantees | none beyond the call; retries are the caller's job | at-least-once with ack, durable + persistent | at-least-once, retention, replay |
| Multiple consumers / replay | caller must call each one | one queue per consumer; no replay (B4 0 %) | consumer groups; replay (B4 100 %) |
| Complexity | lowest | outbox, idempotency, retries, DLQ, intermediate states | + offsets, lag, partitions, one more broker |
| Performance | not decisive (REST ≈ gRPC; gRPC slower under load) | not decisive (~1 ms) | not decisive (~1–2 ms) |
| Consistency | immediate | eventual | eventual |

**Main research question — overall answer (draft):** the communication approach changes **how failures
show up** (error vs delay), **how tightly services depend on each other at runtime**, **which states the
system must handle** (pending, retries, duplicates), and **how much reliability machinery is needed**. Raw
speed was not the differentiator. The design consequence is to choose per interaction: synchronous only where
an immediate answer is required, queues for work between known services, and a log/stream for facts that
several services read.

## 6. v2 architecture (the result)

Diagram: `docs/architecture-v2.drawio` (personas USER and DRIVER → Web App → services; legend per mechanism).

```
Browser ──REST──► Ride Service, Driver Service
Browser ◄─SignalR─► Realtime Service            (notifications, chat; driver tab sends GPS)

Ride ──RabbitMQ (ride.requested)──► Matching
Matching ──gRPC (available drivers, create offer)──► Driver
Driver ──gRPC (assign accepted ride)──► Ride
Driver ──RabbitMQ (driver.rejected-ride)──► Matching
Matching ──RabbitMQ (ride.matching-failed)──► Ride
Ride ──RabbitMQ (ride.completed / ride.cancelled)──► Driver
Ride + Driver ──RabbitMQ (status / offer events)──► Realtime
Realtime ──Kafka (driver-locations, key = driverId)──► Realtime + Matching
```

| Service | Responsibility | Data |
| --- | --- | --- |
| **Ride** | source of truth for rides: `Requested → Assigned → InProgress → Completed` (or Cancelled / NoDriverAvailable) | PostgreSQL `rides` |
| **Driver** | drivers (`Offline/Available/Offered/Busy`) and offers; reserving a driver and creating its offer happen in **one transaction** | PostgreSQL `drivers` |
| **Matching** | chooses the driver: available drivers (gRPC), **nearest** by GPS (Kafka), max 3 attempts; stateless | none |
| **Realtime** | bridge to browsers: SignalR notifications, chat, driver live location (producer + consumer of Kafka) | none (in memory) |

Technology: .NET 10 / ASP.NET Core, EF Core + PostgreSQL, RabbitMQ, Kafka (KRaft), gRPC, SignalR, React 19 +
TypeScript, Docker Compose.

## 7. Decision per interaction (v1 → v2)

Evidence types: **Measured** = supported by v1 runs; **Measured (indirect)** = a measured property of the
mechanism applied to this interaction; **Reasoned** = trade-off analysis only.

| # | Interaction | v1 | **v2** | Evidence | Key reason |
| --- | --- | --- | --- | --- | --- |
| 1 | Frontend → Ride / Driver | REST | **REST** | Reasoned | user waits for an answer; browser-native |
| 2 | Services ⇄ browser | SignalR | **SignalR** | Reasoned | push without polling; two-way chat |
| 3 | Ride → Matching: new request | RabbitMQ (REST optional) | **RabbitMQ** | **Measured** (A1–A3) | REST: 0/3 accepted when Matching down/slow; queue: 100 % accepted and matched later |
| 4 | Matching → Driver: available drivers | REST or gRPC | **gRPC** | Measured (no perf gain) + Reasoned | must be sync; gRPC for the typed `.proto` contract, **not** speed |
| 5 | Matching → Driver: create offer | REST | **gRPC** | Reasoned | must know immediately if the driver could be reserved |
| 6 | Driver accepts → Ride assigns | RabbitMQ | **gRPC (sync)** — *main change* | Reasoned + v2 functional test | v1 needed compensation when the passenger cancelled in between; sync gives an honest immediate answer |
| 7 | Driver → Ride "offer created" | RabbitMQ | **Removed** | Reasoned | only existed for experiment timing |
| 8 | Driver rejects → Matching | RabbitMQ | **RabbitMQ** | Measured (indirect) (A2, B3) | report a fact; Matching may be down |
| 9 | Ride completed/cancelled → Driver | RabbitMQ | **RabbitMQ, durable** | Measured (indirect) (B3) | Ride must not depend on Driver; temp queue lost 38/76 |
| 10 | Status events → Realtime | RabbitMQ | **RabbitMQ, queue per consumer** | Measured (indirect) (B2) | producers must not know/depend on Realtime |
| 11 | Ride lifecycle → Analytics | Kafka | **Removed** (service dropped) | Reasoned | its job was the experiments; no product use |
| 12 | Driver GPS → Realtime + Matching | — (new) | **Kafka**, key = driverId | Measured (indirect) (B2–B4) + Reasoned | a stream read by several services; order per driver; catch-up after restart |
| 13 | Matching → Ride: no driver | RabbitMQ | **RabbitMQ** | Measured (indirect) (B3) | report a fact; safe to process late/twice |

**Decision 6 in detail (good portfolio example):** in v1 the driver's acceptance was confirmed first and the
ride assigned later via a message; if the passenger cancelled in between, the driver had "accepted" a ride that
no longer existed and compensation logic was needed. v2 calls Ride synchronously and confirms only if the
assignment succeeded. Trade-off: while Ride is down a driver cannot accept — acceptable, since no ride can
progress without Ride anyway. **Verified in v2:** with RabbitMQ paused (so Driver hadn't heard of a
cancellation) the accept returned **409 "This ride is no longer available"** and the driver was freed; with Ride
stopped, accept returned **503** and the offer stayed pending; after Ride came back the same accept succeeded.

**Decision 12 in detail:** Analytics was removed; Kafka got a use that fits it better — driver GPS positions
(simulated in the driver's browser, every 2 s). Realtime writes them to topic `driver-locations`; Realtime and
Matching read them as **two consumer groups**. Why Kafka: several independent readers of the same data (B2),
order per driver via the key (B2 shared variant), catch-up after restart (B3/B4). RabbitMQ would need one queue
per consumer and loses consumed messages; sync calls would couple the producer to every consumer.
Honest trade-off: at demo scale RabbitMQ could handle the volume — the choice follows the *shape* of the data.
**Verified in v2:** Emma (waited longest) at the airport, Lucas at TU/e, pickup Fontys → Matching logged
"Emma 7.4 km, Lucas 0.7 km" and offered the ride to Lucas; both consumer groups showed the same offsets with
lag 0 (Lucas's 8 positions on one partition, Emma's 2 on another).

**Service-set decisions:** Analytics removed (decision 11). Location added to Realtime instead of a new
service (same kind of work: live, connection-based, no business rules; split only if positions must be stored).
Driver **not** split into Drivers + Offers (reservation + offer must be one transaction, otherwise a saga).

## 8. Reliability mechanisms in v2 (what async really costs)

| Mechanism | Where | Purpose / evidence |
| --- | --- | --- |
| Transactional outbox | Ride, Driver | event saved in the same DB transaction as the data. **Verified:** ride requested while RabbitMQ was stopped → HTTP 201, event published ~4 s after the broker came back |
| Inbox / duplicate cache | Ride, Driver (table); Matching, Realtime (memory) | at-least-once delivery → idempotent handling |
| Durable queues, persistent messages, publisher confirms | all RabbitMQ paths | nothing lost while a consumer is down (B3). **Observed:** Realtime started later and processed 26 events that had waited in its queue |
| Retries + dead-letter queue per queue | all consumers | a failing message never blocks the queue |
| Idempotency key `rideId:attempt` | gRPC CreateOffer | a retried message never creates a second offer |
| gRPC deadlines (5 s) | all gRPC calls | a sync call never waits forever |
| Offer expiry (30 s) | Driver | unanswered offer frees the driver and triggers the next match (verified ~29 s) |

## 9. Limitations (be honest about these)

- One machine, small samples (A: 3 rides per run; B: 76 or 190 events), single brokers without replication;
  results show **behaviour** (accepted/lost/replayed, order of magnitude), not production performance.
- In Experiment A, unavailability was mostly simulated (HTTP 503); a real container stop was tested with a tool.
- Decisions 1, 2, 5, 6, 7, 11 are reasoned; 12 applies measured Kafka properties to a new interaction.
  Decision 6 is the best candidate for a follow-up experiment.
- v2 implementation trade-offs: the accept spans two databases (no distributed transaction — a crash between
  the two writes could leave the ride Assigned and the offer Pending; a saga/reconciliation would fix it);
  Realtime state is in memory (lost on restart; multiple instances need a SignalR backplane); GPS is simulated
  (straight lines, accelerated speed).

## 10. Learning reflection material (link to the PDP)

| PDP goal | Evidence from this experiment |
| --- | --- |
| Confidence in technical decisions, able to explain *why* | 13 interactions each with a written justification + evidence type (`docs/communication-decisions.md`) |
| Understand sync vs async in practice | A2/A3: failure vs delay; v2 decision 6 moved one interaction *back* to sync |
| RabbitMQ and Kafka in practice | B1–B4; configuration effects (prefetch, commit strategy, durable vs temporary) |
| Concepts: coupling, delivery, latency, reliability, complexity | outbox, idempotency, DLQ, retries, offsets, consumer groups, deadlines all implemented and tested |
| Assumptions corrected by data | "gRPC is faster" did not hold here; Kafka "slowness" was caused by a sync commit; prefetch hid a backlog |
| Feedback | scope reduced after teacher feedback; v1 demonstrated to teachers; v2 rebuilt from the evidence |
| Prepares later experiments | same system extendable for scalability, resilience, Kubernetes, observability (e.g. the in-memory Realtime state is a natural scalability topic) |

Practical lessons worth a sentence: Windows Smart App Control blocked locally built DLLs, so v2 runs fully in
Docker Compose (services wait for healthy infrastructure, environment variables override config).

**Ask the student how to present** (facts to confirm with them): v2 was rebuilt step by step with an AI coding
assistant, with each concept (folder roles, outbox, gRPC contracts, consumer groups…) explained before moving
on. An extension added a real map (OpenStreetMap tiles via Leaflet): the passenger picks pickup/destination by
address search (Nominatim geocoder) or by clicking the map; the driver can place the car on the map or use the
device's real GPS. The coordinates travel through the whole chain (Ride → `ride.requested` → Matching → gRPC
CreateOffer → offer → `driver.rejected-ride` → re-match), so "nearest driver" works for any address. No
architecture change was needed — only richer data in the existing contracts (a good point about stable
architecture). Privacy note: typed addresses are sent to the public Nominatim service.

## 11. Suggested portfolio structure

1. **Introduction** — context, scope reduction after feedback, research question + sub-questions.
2. **Method** — two iterations (v1 measure, v2 decide), experiment design, data validity rules, environment.
3. **System overview** — v2 diagram, services and responsibilities, technology stack.
4. **Results** — Experiment A, B, REST vs gRPC (tables above, with "what it shows").
5. **Answers** — SQ1–SQ4 and the main question, each pointing to evidence.
6. **Architectural decisions** — the 13-row table + 2–3 deep dives (decisions 3, 6, 12).
7. **Reliability & trade-offs in the implementation** — section 8, limitations.
8. **Reflection** — PDP goals, expectations that were wrong, what I'd do differently, next experiments.
9. **Appendix** — data files, how to run (`docker compose up -d --build`, `npm run dev`).

## 12. Source files (the student can paste them if you need more detail)

| File | Content |
| --- | --- |
| `docs/communication-decisions.md` | full decision document (source of sections 4, 7–9) |
| `docs/architecture-v2.drawio` | architecture diagram |
| `research/v1-results/experiment-runs.json` / `experiment-runs-summary.csv` | all 131 runs |
| `research/v1-results/measurements-raw.csv` | 22,932 individual measurements |
| `research/v1-results/tool-results/` | k6 REST/gRPC load test, RabbitMQ recovery test |
| `README.md`, `src/services/*/README.md` | how to run; each service's inputs and outputs |
