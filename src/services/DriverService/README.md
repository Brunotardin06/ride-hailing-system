# DriverService

Source of truth for drivers (`Offline ⇄ Available → Offered → Busy`) and ride offers (`Pending → Accepted / Rejected / Expired / Cancelled`).
Offer creation and driver reservation happen in one transaction, which is why drivers and offers live in one service.

- Ports: **5002** REST (HTTP/1.1) · **5012** gRPC (HTTP/2) · database `drivers` · `GET /health`
- **In**
  - REST from the frontend: drivers, availability, pending offers, accept / reject, `POST /api/dev/reset`
  - gRPC `Drivers.GetAvailableDrivers` / `Drivers.CreateOffer` from MatchingService (`Grpc/DriversGrpcService.cs`, contract `Protos/drivers.proto`)
  - RabbitMQ queue `driver-service`: `ride.completed` (free driver, +1 ride), `ride.cancelled` (withdraw offer, free driver)
- **Out**
  - gRPC `Rides.AssignDriver` to RideService on accept (synchronous: the driver gets 409 if the ride was cancelled, 503 if RideService is down)
  - RabbitMQ (outbox): `ride.offer-created`, `driver.rejected-ride` (rejected or expired), `ride.offer-withdrawn`
- Background: `Services/OfferExpiryService.cs` expires offers after `Offers:TimeoutSeconds` (30 s)
