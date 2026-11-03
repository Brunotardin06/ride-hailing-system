# Research design

*Personal Experiment 1 — Complex Software Systems, Fontys University of Applied Sciences.*

The ride-hailing system is the context; this first experiment focuses on **communication between services**. Scalability, resilience engineering, Kubernetes and observability are left for later experiments.

## Research question

> **How do different communication approaches between services affect the behaviour and design of a distributed ride-hailing system?**

| Sub-question | Answered by |
| --- | --- |
| **SQ1** When is synchronous communication appropriate? | Experiment A |
| **SQ2** When is asynchronous communication more appropriate? | Experiment A |
| **SQ3** What are the practical differences between RabbitMQ and Kafka? | Experiment B |
| **SQ4** What trade-offs do different communication approaches introduce? | Experiments A and B, analysed in Experiment C |

**Guiding principle:** a communication technology is evaluated *in the context of a specific scenario and its requirements*. Performance numbers are supporting evidence, not the answer. The aim is not to find a technology that is "always better" but to be able to justify a choice for a given interaction.

## Experiments

### Experiment A: synchronous vs asynchronous (REST vs RabbitMQ)

**Interaction:** Ride Service → Matching Service when a passenger requests a ride. Both approaches run **the same matching logic** (`MatchingWorkflow.MatchAsync`); only the communication differs.

| | REST (synchronous) | RabbitMQ (asynchronous) |
| --- | --- | --- |
| How | RideService calls `POST /api/matching/requests` and waits (timeout, default 3 s) | RideService stores the ride, publishes `experiment.ride-requested` (outbox, publisher confirms), answers immediately |
| Passenger sees | the matching outcome in the HTTP response, or an error (503) | "accepted, looking for a driver"; the outcome follows later (pending state + notification) |

| Scenario | Situation | Hypothesis (to be tested, not a result) |
| --- | --- | --- |
| **A1 Normal** | both services available | REST's response includes the matching time; RabbitMQ answers faster but the offer appears slightly later. |
| **A2 Unavailable** | the matching instance is unavailable for N seconds | REST requests fail and are not processed later; RabbitMQ keeps them (if durable/persistent) and matches them after recovery. |
| **A3 Slow** | matching takes longer than the REST timeout | REST callers time out and the work may be wasted; RabbitMQ accepts requests and builds a backlog, so time to offer grows. |

### Experiment B: message queuing vs event streaming (RabbitMQ vs Kafka)

**Scenario:** distributing ride lifecycle events (`requested`, `assigned`, `started`, `completed`, `cancelled`) to interested services. An identical, generated workload goes through either mechanism.

| Scenario | Situation | Hypothesis |
| --- | --- | --- |
| **B1 Single consumer** | one service consumes everything | Both deliver all events with low latency; RabbitMQ deletes acknowledged messages, Kafka keeps records and moves an offset. |
| **B2 Multiple consumers** | two independent services (optionally: sharing one subscription) | Both can give each service every event (queue per service / group per service); a shared queue/group splits the events. |
| **B3 Consumer offline** | a consumer stops while events keep coming | Durable queue and Kafka group keep the events; a temporary queue loses them. |
| **B4 Replay** | a new consumer starts after the events were published and processed | Kafka can replay retained events; the tested RabbitMQ configuration cannot. |

### Experiment C: architectural trade-off analysis

Not a benchmark. Per scenario the dashboard holds **requirements, hypothesis, expected properties (theory), the measured evidence of the latest run, and the researcher's observations, advantages, disadvantages, recommendation and limitations**, with an *evidence status* (not tested / hypothesis / supported / partially supported / contradicted). Two **trade-off matrices** (A and B) score each approach on eight decision criteria, each cell labelled as *measured*, *expected* or *not tested*:

1. Whether an immediate response is required
2. Temporal coupling between services
3. Behaviour when another service is unavailable
4. Processing and delivery guarantees
5. Persistence and replay requirements
6. Impact on system complexity
7. Performance, when it matters to the scenario
8. Consistency and delayed processing

## Methodology

- **Controlled scenarios, same business operation.** The same matching logic (A) or the same generated event set (B) is used for every approach. Approaches of one scenario run one after another with identical parameters (rides, interval, timeout, outage length).
- **Equivalent outcome.** Experiment A compares the *business outcome*: was a ride offer created, and when? The passenger-facing HTTP response time and the RabbitMQ publish-confirm time are recorded **separately**, because they measure different intervals.
- **Open workload.** Ride requests are sent at a fixed interval, independent of responses (like independent passengers), so backlogs and timeouts can occur.
- **Isolation.** Experiments use their own resources and never disturb the demo application: a dedicated `matching-experiment` instance with its own queue, a hidden pool of 20 experiment drivers, experiment rides flagged with an `ExperimentRunId` and excluded from business analytics (Kafka), and a dedicated exchange/topic with per-run queues and consumer groups for Experiment B. Every run cleans up after itself.
- **Failure situations are part of the design.** The matching instance can be made unavailable (simulated: REST 503 + RabbitMQ consumer cancelled; or a real container stop via `experiments/experiment-a-real-outage.mjs`), slowed down per experiment ride, and consumers can be taken offline.
- **Verify, don't assume.** Queue durability and message persistence are read from the broker during the outage; duplicate offers are counted at DriverService; work done after a client timeout is counted inside MatchingService.
- **Evidence before conclusions.** Each run stores its quantitative results, per-ride/per-consumer details, a timeline and *factual observations generated from the numbers*. Conclusions are written by the researcher in Experiment C. Failed and incomplete runs are stored as such.

### What is measured

| Measurement | Start → end | Clock |
| --- | --- | --- |
| HTTP response time (A) | experiment runner sends `POST /api/rides` → response received | one process, monotonic |
| Time to offer (A) | ride `RequestedAt` (RideService) → offer created (DriverService) | two processes on one host (synchronised host clock assumed) |
| Publish confirm (A, RabbitMQ) | RideService `BasicPublish` → broker confirm | one process |
| Queue depth / consumers (A, B) | AMQP passive declare every 250 ms (ready messages) | — |
| Delivery latency (B) | publish call → consumer callback | one process, monotonic (producer and consumers run in AnalyticsService) |
| Broker acknowledgement (B) | publish call → confirm (RabbitMQ) / ack `acks=all` (Kafka) | one process |
| Backlog (B) | messages in the consumer's queue (RabbitMQ) / consumer-group lag (Kafka) | — |
| Counts | requests accepted/failed/timeouts, offers, pending, late offers, duplicates, aborted work, events received/unique/missing/duplicate/out-of-order, replayed | — |

### Qualitative evaluation criteria

Implementation and configuration effort (what had to be set up — recorded per mechanism in Experiment B), extra states the application must handle (e.g. "pending matching"), what the user experiences in each situation, and what can go wrong (lost, duplicated, late or reordered work).

## Observations from the verification runs (1 November 2026)

These are the results of the runs executed while building and testing the experiments, on one laptop with all containers in Docker. **They are single, small runs** (3–5 rides, 190 events) and serve to show that the experiments work and what they reveal; repeat them (≥ 3 times, more rides) before using numbers in the report.

| Scenario | REST / RabbitMQ (A) · RabbitMQ / Kafka (B) |
| --- | --- |
| A1 normal (5 rides) | HTTP p50 30 ms vs 7 ms; request → offer p50 14 ms vs 17 ms; first REST request 296 ms (connection set-up) |
| A2 simulated outage (5 rides, 8 s) | REST 0/5 accepted (immediate 503), none processed later · RabbitMQ 5/5 accepted in ~6 ms, 5 queued (durable = true, delivery_mode = 2), all matched 43 ms after recovery; time to offer ≈ 7–8 s |
| A2 **real** container stop (5 rides, 15 s) | REST: 4 of 5 requests **waited for the full 3 s timeout** instead of failing immediately (the exact network-level cause inside Docker was not investigated), none processed later · RabbitMQ: 5/5 accepted, all matched 114 ms after the process was back. *The simulated outage (immediate 503) understated the passenger's waiting time for REST.* |
| A3 slow (5 rides, 4 s processing, 3 s timeout) | REST: 5/5 timeouts after 3.0 s; the 5 matching operations were **aborted inside MatchingService** because the closed HTTP connection cancelled them (ASP.NET Core `RequestAborted`) · RabbitMQ: 5/5 accepted in ~6 ms, up to 4 messages waiting, offers after 4.0, 7.7, 11.4, 15.1, 19.0 s (single consumer, one message at a time) |
| B1 single consumer (190 events) | delivery p50 1 ms vs 2 ms; 0 duplicates, 0 out of order |
| B2 independent consumers | both consumers received 190/190 with both mechanisms |
| B2 shared subscription | RabbitMQ 95/95 round-robin, events of **20 of 50 rides split** over both consumers · Kafka 75/115 by partition, **0 rides split** (key = ride id) |
| B3 consumer offline (durable) | 95 events waited for B (queue depth / lag); B missed 0 with both |
| B3 RabbitMQ temporary queue | B missed 95/190 (queue deleted when B disconnected); also observed: the broker rejected a publish routed to the queue while it was being deleted |
| B4 replay | Kafka: late consumer received 190/190 earlier events · RabbitMQ: 0/190 (messages only exist in queues bound at publish time; A's queue was empty after acknowledgement) |

Configuration effects discovered during the work, also evidence for SQ3/SQ4:

- With the RabbitMQ prefetch of 10 the A3 backlog sat **in the consumer's buffer (unacknowledged)**, invisible as "ready" messages; with prefetch 1 (used by the experiment instance) it is visible in the queue.
- A Kafka consumer that committed **every offset synchronously** reached only ~140 events/s and delivery p50 ≈ 540 ms in B1; storing offsets and committing in the background brought delivery p50 to ≈ 2 ms. The commit strategy, not "Kafka", caused the latency.

## Limitations and assumptions

- One Docker host: no real network latency or partitions; cross-process timestamps assume one clock.
- Small workloads; no statistical analysis yet: results are illustrative until repeated.
- Experiment A: one matching instance with one consumer processing one message at a time; synchronous calls have no automatic retry; the dashboard outage is simulated (a real stop is available as a script, see above for the observed difference).
- Experiment B: one broker each (no replication); consumers are separate subscriptions inside one process (AnalyticsService), so "independent services" are simulated at broker level; only classic RabbitMQ queues are tested; RabbitMQ Streams (which offer replay) are out of scope.
- Re-matching after a rejection always uses RabbitMQ; Experiment A only covers the initial matching request.
- No authentication, no security analysis.
