# RideService

Source of truth for rides and their lifecycle: `Requested → Assigned → InProgress → Completed` (or `Cancelled` / `NoDriverAvailable`).

- Ports: **5001** REST (HTTP/1.1) · **5011** gRPC (HTTP/2) · database `rides` · `GET /health`
- **In**
  - REST from the frontend: request, list, active ride, get, start, complete, cancel (`Controllers/RidesController.cs`)
  - gRPC `Rides.AssignDriver` from DriverService when a driver accepts (`Grpc/RidesGrpcService.cs`, contract `Protos/rides.proto`)
  - RabbitMQ queue `ride-service`: `ride.matching-failed` → `NoDriverAvailable` (`Messaging/RideMessageHandler.cs`)
- **Out** (RabbitMQ, via the transactional outbox): `ride.requested`, `ride.assigned`, `ride.started`, `ride.completed`, `ride.cancelled`, `ride.no-driver-available`
- Logic: `Services/RideWorkflow.cs` · state machine: `Models/Ride.cs`
