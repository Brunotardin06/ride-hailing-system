# Tools I used for the experiments

This document lists what I used to run and measure the communication experiments, and what each tool was for.

## Environment

| Tool | What I used it for |
| --- | --- |
| **Docker + Docker Compose** | Run all services, PostgreSQL, RabbitMQ and Kafka on my laptop with one command (`docker compose up -d --build`). Also to cause real failures: `docker compose stop <service>` to take a service down and `start` to bring it back. |
| **.NET 10 / ASP.NET Core** | The services themselves, and the experiment runners that sent the requests and took the measurements. |
| **PostgreSQL** | Store every experiment run: parameters, results, details and timeline, so runs could be compared and exported later. |

## Communication technologies compared

| Technology | Library |
| --- | --- |
| REST (HTTP/1.1 + JSON) | ASP.NET Core controllers + `HttpClient` |
| gRPC (HTTP/2 + Protobuf) | `Grpc.AspNetCore` / `Grpc.Net.Client`, shared `.proto` contracts |
| RabbitMQ | `RabbitMQ.Client` (publisher confirms, manual acknowledgements) |
| Kafka | `Confluent.Kafka` (producer, consumer groups) |

## Measuring

| Tool | What I used it for |
| --- | --- |
| **`Stopwatch` (.NET)** | Measure durations on a monotonic clock (response times, delivery times). |
| **Timestamps in the messages** | Measure the time from sending to receiving across services (e.g. ride requested → offer created). |
| **Percentiles (p50, p95, max)** | Summarise the measurements instead of only using averages. |
| **k6** (`grafana/k6` Docker image) | Load test: many parallel calls for a fixed time, to compare REST and gRPC under load. |
| **Node.js scripts** | Automate scenarios that needed Docker control, e.g. stop a service, send ride requests, start it again, and measure how long recovery took. |

## Observing the brokers

| Tool | What I used it for |
| --- | --- |
| **RabbitMQ Management UI** (port 15672) | Watch queues, message counts, consumers and dead-letter queues during a run. |
| **AMQP passive queue declare** | Read the queue size every 250 ms from code during a run. |
| **RabbitMQ management API** | Check that queues were durable and messages persistent during an outage. |
| **Kafka CLI** (`kafka-consumer-groups.sh`, `kafka-topics.sh`) | See consumer-group offsets and lag, and reset offsets to replay events. |

## Results and analysis

| Tool | What I used it for |
| --- | --- |
| **JSON / CSV export** | Export all runs and raw measurements for analysis. |
| **Spreadsheet / tables** | Compare runs per scenario and per approach. |
| **draw.io** | Architecture diagram of the final system. |
