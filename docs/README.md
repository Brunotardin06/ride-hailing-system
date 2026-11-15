# About this project

This is an **individual project** (Fontys ICT · Complex Software Systems · Personal Experiment 1) focused on
**communication between services** in a distributed system.

The goal is to practise different ways services can talk to each other and to learn:
- **when** each approach is the right choice (and when it is not);
- **how** to implement it properly, including the reliability work it needs.

A simplified ride-hailing platform is the context: a passenger requests a ride, the system offers it to the
nearest available driver, the driver accepts, and both follow the ride live.

## Communication approaches practised

| Approach | Type | Used for |
| --- | --- | --- |
| **REST** | synchronous | user actions from the web app (request ride, accept offer, …) |
| **gRPC** | synchronous | service-to-service calls that need an immediate answer, with a typed `.proto` contract |
| **RabbitMQ** | asynchronous messaging | work and events between known services; keeps working when a receiver is down |
| **Kafka** | event streaming | driver GPS positions, read by several services independently |
| **SignalR** | real-time push | notifications, chat and live driver position in the browser |

The main lesson: there is no "best" approach. Each interaction is chosen by asking whether the caller needs an
answer **now**, what should happen if the other service is **down or slow**, and **who else** needs the data.

## Implementation topics covered

- Transactional **outbox** and idempotent consumers (**inbox**) for reliable messaging
- Durable queues, retries and **dead-letter queues** (RabbitMQ)
- **Consumer groups**, partition keys and offsets (Kafka)
- gRPC **contracts**, deadlines and status codes
- SignalR **groups** for targeted push
- Optimistic concurrency and one database per service (PostgreSQL)

## In this folder

| File | Content |
| --- | --- |
| [architecture.drawio](architecture.drawio) | architecture diagram (open in draw.io / diagrams.net) |
| [experiment-tools.md](experiment-tools.md) | tools used to run and measure the communication experiments |

How to run the system: see the [main README](../README.md).
