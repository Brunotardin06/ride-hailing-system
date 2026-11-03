# Live demo guide

A step-by-step script for demonstrating the system to teachers (about 20 minutes): first the real application (part 1, ~8 min), then the research experiments (part 2, ~12 min). It assumes everything is started as described in the [README](../README.md#quick-start).

**Setup:** open three browser windows side by side:

| Window | URL | Role |
| --- | --- | --- |
| A | http://localhost:5173/passenger/request | Passenger (Alice Jansen) |
| B | http://localhost:5173/driver/dashboard | Driver (Emma de Vries) |
| C | http://localhost:15672 (login `ridehailing` / `ridehailing`) | RabbitMQ management UI |

Each browser tab picks its own identity (stored per tab), so A and B can be in the same browser. Check that both A and B show a green **Live** badge (SignalR connected). To start from a clean state, click **Reset demo** on the driver dashboard (Emma and Lucas available, others offline).

## Part 1 — The real application

## 1. Request a ride (REST → RabbitMQ → REST/gRPC → SignalR)

1. **A:** pick pickup/destination and click **Request ride**.
   - REST `POST /api/rides` to RideService → `201`; the page switches to *Current ride* ("Looking for a driver…").
2. **B:** within a moment, a toast "New ride request" and **View request** appear, without reloading.
   - Behind the scenes: RideService → `ride.requested` (RabbitMQ) → MatchingService → driver lookup at DriverService (REST or gRPC) → `POST /api/offers` → `ride.offer-created` → RealtimeService → SignalR to `driver:<Emma>`.
3. **A:** shows "Driver found: waiting for Emma de Vries to accept…" in *Live updates*.
4. **C:** *Queues and Streams*: show the four service queues and their `.dlq` queues; the message rates graph shows the traffic.

## 2. Accept (REST → RabbitMQ → SignalR)

1. **B:** **View request** → the offer with a 30-second countdown → **Accept**.
   - REST `POST /api/offers/{id}/accept` to DriverService (not via RealtimeService).
2. **A:** badge changes to **Driver on the way**, driver card shows Emma, her car and licence plate.
   - `driver.accepted-ride` → RideService (Assigned) → `ride.assigned` → RealtimeService → passenger.

## 3. Chat (SignalR, two-way)

1. **A:** **Chat** → send "I'm at the main entrance".
2. **B:** **Chat** (sidebar) → the message is already there; reply. Both directions are instant.

## 4. Ride lifecycle

1. **B:** *Active ride* → **Passenger picked up · Start ride** → **Complete ride**.
2. **A:** the timeline advances live to *In progress* and *Completed*; **Request another ride** appears. *History* lists the ride.
3. **B:** dashboard shows Emma *Available* again and *Completed rides* + 1 (DriverService consumed `ride.completed`).

## 5. Rejection, re-matching and "no driver"

1. **A:** request another ride. **B:** **Reject**.
2. **A:** "Emma could not take the ride. Looking for another driver…". The offer goes to Lucas (open a second driver tab and select *Lucas Janssen* to show it arriving there, then reject it as well).
3. **A:** "No driver available" (red). Ride status *No driver*.

Optional: let an offer run out (30 s) to show expiry, or cancel a ride while the driver is looking at the offer: the offer disappears from the driver's screen ("Request withdrawn").

## Part 2 — The research: communication decisions

Open http://localhost:5173/experiments. Tip: run each scenario once **before** the presentation, so results are already there if time is short; you can still run one live (A2 takes ~25 s for both approaches).

## 6. Overview (1 min)

Show the **research question** and the four sub-questions, and the principle: *approaches are evaluated in the context of a scenario; numbers are supporting evidence, not the answer*. Point out that experiments use isolated resources (separate matching instance, experiment drivers, separate exchange/topic), so the app you just demonstrated is not affected.

## 7. Experiment A — synchronous vs asynchronous (5 min)

1. Open **A · Sync vs async**. Explain the two approaches in the top card: same matching logic, different communication.
2. Select **A2 · Matching Service unavailable**, keep both approaches, 5 rides, outage 10 s → **Run**. While it runs, explain what is happening (the progress line shows the phase).
3. In the results, compare:
   - *Accepted*: REST 0 (HTTP 503) vs RabbitMQ all accepted. → **temporal coupling**.
   - *Ride offers created*: REST 0 (failed requests are not stored) vs RabbitMQ all, shortly after recovery. → **behaviour when a service is unavailable**.
   - *Queue durable = 1, delivery_mode = 2* was read from the broker during the outage → durability was **verified**, not assumed.
   - *HTTP response* vs *request → offer*: different intervals; with RabbitMQ the passenger gets a fast answer but the outcome comes later → the app needs a "pending" state and notifications (complexity, consistency).
4. Show the **queue chart** (messages waiting during the outage) and one or two observations.
5. If time allows, show an earlier **A3 slow** run (Earlier runs): REST timeouts (and matching work cancelled inside MatchingService) vs RabbitMQ backlog with growing time to offer. And **A1 normal**: under normal conditions both work; REST gives the outcome in one response.
6. Mention the **real outage** check (`experiments/experiment-a-real-outage.mjs`): with a real container stop, REST requests waited for the full timeout instead of failing immediately — the simulation understated the passenger's wait.

## 8. Experiment B — message queuing vs event streaming (4 min)

1. Open **B · Messaging vs streaming**; explain the two models in the top card (delete on ack vs retained log with offsets).
2. Run **B4 · Replay** (both mechanisms, 50 rides, ~15 s). Result: the late consumer received all earlier events with Kafka and none with RabbitMQ (with this configuration). Open *What this needed to configure* for each mechanism.
3. Show earlier runs of **B3 · Consumer offline**: both keep events for an offline consumer when configured durably (backlog visible as queue depth / consumer lag); with a *temporary* RabbitMQ queue the events are lost. → **configuration determines the guarantee**.
4. Show **B2** with *shared subscription*: both split the work (competing consumers), but Kafka keeps all events of one ride at one consumer (partition key) while RabbitMQ's round-robin splits rides. Independent consumers (no sharing) get every event with both.

## 9. Experiment C — architectural decisions (2 min)

Open **C · Architectural decisions**. For one scenario (e.g. A2) show: requirements → hypothesis (labelled as hypothesis) → measured evidence from the latest run → your observations/recommendation and the evidence status. Then show the **trade-off matrix**: per criterion the measured evidence and your assessment, marked *measured*, *expected* or *not tested*. Conclude with the recommendation you wrote, e.g. which interaction in the ride-hailing system you would make synchronous and which asynchronous, and why.

## 10. Supplementary tools (optional)

*Additional tools* keeps the earlier technical experiments: live communication metrics of the demo rides, the REST vs gRPC benchmark and k6 runs, RabbitMQ failure injection (retry / dead-letter), business event analytics and the Kafka stream inspector. Present them as supporting evidence, not as the research answer.
