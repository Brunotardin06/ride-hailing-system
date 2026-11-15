using DriverService.Data;
using DriverService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using RideHailing.Grpc.Rides;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;

namespace DriverService.Services;

/// <summary>
/// Offer logic. Entry points that call it:
///   - REST     (OffersController, DriversController)    → accept / reject / list
///   - gRPC     (DriversGrpcService ← MatchingService)   → create
///   - gRPC client → RideService                         → assign the ride on accept
///   - RabbitMQ (DriverMessageHandler)                   → ride completed / cancelled
///   - Timer    (OfferExpiryService)                     → expire
///
/// Every change writes its event to the OUTBOX in the same SaveChanges (no "saved but not published").
/// </summary>
public class OfferService(DriverDbContext db, Rides.RidesClient rides, IConfiguration configuration, ILogger<OfferService> logger)
{
    public const string Source = "driver-service";

    private TimeSpan OfferTimeout => TimeSpan.FromSeconds(configuration.GetValue("Offers:TimeoutSeconds", 30));

    public async Task<List<RideOffer>> GetOffersAsync(Guid driverId, OfferStatus? status, CancellationToken ct)
    {
        var query = db.Offers.AsNoTracking().Where(o => o.DriverId == driverId);
        if (status is not null) query = query.Where(o => o.Status == status);
        return await query.OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(ct);
    }

    /// <summary>Reserves the driver (Available → Offered) and stores a Pending offer + ride.offer-created, in one transaction.</summary>
    public async Task<RideOffer> CreateOfferAsync(CreateOfferCommand command, CancellationToken ct)
    {
        // Idempotency: Matching retried the same request → return what we created the first time.
        var existing = await db.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.RequestKey == command.RequestKey, ct);
        if (existing is not null) return existing;

        var driver = await db.Drivers.FindAsync([command.DriverId], ct) ?? throw new NotFoundException("Driver not found.");
        driver.ReserveFor(command.RideId); // throws 409 if the driver is not Available

        var now = DateTimeOffset.UtcNow;
        var offer = new RideOffer
        {
            Id = Guid.NewGuid(),
            RequestKey = command.RequestKey,
            RideId = command.RideId,
            CorrelationId = command.CorrelationId,
            DriverId = command.DriverId,
            PassengerId = command.PassengerId,
            PassengerName = command.PassengerName,
            Pickup = command.Pickup,
            Destination = command.Destination,
            PickupLat = command.PickupLat,
            PickupLng = command.PickupLng,
            DestinationLat = command.DestinationLat,
            DestinationLng = command.DestinationLng,
            Attempt = command.Attempt,
            CreatedAt = now,
            ExpiresAt = now + OfferTimeout,
        };
        db.Offers.Add(offer);

        // → RealtimeService shows the offer on the driver's screen.
        db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideOfferCreated, offer.CorrelationId, Source,
            new RideOfferCreatedPayload(offer.Id, offer.RideId, driver.Id, driver.Name, offer.PassengerId, offer.PassengerName,
                offer.Pickup, offer.Destination, offer.Attempt, offer.ExpiresAt)));

        await db.SaveChangesAsync(ct);
        return offer;
    }

    /// <summary>
    /// Driver accepts → ask RideService (gRPC, synchronous) to assign the ride BEFORE confirming locally.
    ///   OK                  → offer Accepted, driver Busy. RideService publishes ride.assigned.
    ///   FailedPrecondition  → the ride is no longer waiting (e.g. passenger just cancelled) → offer Cancelled,
    ///                         driver free, 409 to the driver. This immediate answer is WHY this call is synchronous.
    ///   Unavailable/timeout → 503, nothing changed: the offer stays Pending and the driver can try again.
    /// </summary>
    public async Task<RideOffer> AcceptAsync(Guid offerId, Guid driverId, CancellationToken ct)
    {
        var (offer, driver) = await LoadPendingAsync(offerId, driverId, ct);

        try
        {
            await rides.AssignDriverAsync(new AssignDriverRequest
            {
                RideId = offer.RideId.ToString(),
                DriverId = driver.Id.ToString(),
                DriverName = driver.Name,
                Vehicle = driver.Vehicle,
                LicensePlate = driver.LicensePlate,
            }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.FailedPrecondition or StatusCode.NotFound)
        {
            offer.Respond(OfferStatus.Cancelled);
            driver.Release(offer.RideId);
            await db.SaveChangesAsync(ct);
            throw new ConflictException("This ride is no longer available.");
        }
        catch (RpcException ex)
        {
            logger.LogWarning("RideService did not answer AssignDriver ({Status}): {Detail}", ex.StatusCode, ex.Status.Detail);
            throw new ServiceUnavailableException("Could not confirm the ride right now. Please try again.");
        }

        offer.Respond(OfferStatus.Accepted);
        driver.ConfirmRide(offer.RideId); // Offered → Busy
        await db.SaveChangesAsync(ct);
        return offer;
    }

    public async Task<RideOffer> RejectAsync(Guid offerId, Guid driverId, CancellationToken ct)
    {
        var (offer, driver) = await LoadPendingAsync(offerId, driverId, ct);
        await DeclineAsync(offer, driver, OfferStatus.Rejected, "Rejected by driver", ct);
        await db.SaveChangesAsync(ct);
        return offer;
    }

    /// <summary>Called by OfferExpiryService: Pending offers older than the timeout count as rejected.</summary>
    public async Task<int> ExpireOverdueOffersAsync(CancellationToken ct)
    {
        var overdue = await db.Offers
            .Where(o => o.Status == OfferStatus.Pending && o.ExpiresAt < DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var offer in overdue)
        {
            var driver = await db.Drivers.FindAsync([offer.DriverId], ct);
            if (driver is null) continue;
            await DeclineAsync(offer, driver, OfferStatus.Expired, "Offer expired", ct);
        }

        if (overdue.Count > 0) await db.SaveChangesAsync(ct);
        return overdue.Count;
    }

    /// <summary>ride.completed (from RideService): the driver is free again and gets +1 completed ride.</summary>
    public async Task HandleRideCompletedAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return; // duplicate delivery

        var ride = message.PayloadAs<RideStatusChangedPayload>();
        if (ride.DriverId is { } driverId && await db.Drivers.FindAsync([driverId], ct) is { } driver)
            driver.FinishRide(ride.RideId); // Busy → Available (no-op if already done)

        db.MarkProcessed(message);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ride.cancelled (from RideService): withdraw a pending offer for this ride and free its driver,
    /// or free the assigned driver if the ride was already accepted.
    /// </summary>
    public async Task HandleRideCancelledAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return;

        var ride = message.PayloadAs<RideStatusChangedPayload>();

        var pending = await db.Offers.Where(o => o.RideId == ride.RideId && o.Status == OfferStatus.Pending).ToListAsync(ct);
        foreach (var offer in pending)
        {
            offer.Respond(OfferStatus.Cancelled);
            db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideOfferWithdrawn, offer.CorrelationId, Source,
                new RideOfferWithdrawnPayload(offer.Id, offer.RideId, offer.DriverId, "Ride cancelled by passenger"),
                causationId: message.EventId));
        }

        // Every driver still holding this ride (Offered or Busy) becomes Available again.
        var drivers = await db.Drivers.Where(d => d.CurrentRideId == ride.RideId).ToListAsync(ct);
        foreach (var driver in drivers) driver.Release(ride.RideId);

        logger.LogInformation("Ride {RideId} cancelled: {Offers} offer(s) withdrawn, {Drivers} driver(s) released",
            ride.RideId, pending.Count, drivers.Count);

        db.MarkProcessed(message);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reject / expire: free the driver and tell MatchingService to try the next one.</summary>
    private async Task DeclineAsync(RideOffer offer, Driver driver, OfferStatus outcome, string reason, CancellationToken ct)
    {
        offer.Respond(outcome);
        driver.Release(offer.RideId); // Offered → Available

        // Drivers already tried for this ride (incl. this one), so MatchingService never offers it to them again.
        var excluded = await db.Offers.Where(o => o.RideId == offer.RideId).Select(o => o.DriverId).Distinct().ToListAsync(ct);
        if (!excluded.Contains(driver.Id)) excluded.Add(driver.Id);

        db.AddToOutbox(MessageEnvelope.Create(EventTypes.DriverRejectedRide, offer.CorrelationId, Source,
            new DriverRejectedRidePayload(offer.Id, offer.RideId, driver.Id, driver.Name, offer.PassengerId, offer.PassengerName,
                offer.Pickup, offer.Destination, offer.Attempt, reason, excluded,
                offer.PickupLat, offer.PickupLng, offer.DestinationLat, offer.DestinationLng)));
    }

    private async Task<(RideOffer Offer, Driver Driver)> LoadPendingAsync(Guid offerId, Guid driverId, CancellationToken ct)
    {
        var offer = await db.Offers.FindAsync([offerId], ct) ?? throw new NotFoundException("Offer not found.");
        if (offer.DriverId != driverId) throw new ForbiddenException("This offer belongs to another driver.");
        if (offer.Status != OfferStatus.Pending) throw new ConflictException($"Offer is already {offer.Status}.");

        var driver = await db.Drivers.FindAsync([driverId], ct) ?? throw new NotFoundException("Driver not found.");
        return (offer, driver);
    }
}
