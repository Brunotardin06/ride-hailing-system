using DriverService.Data;
using DriverService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;

namespace DriverService.Services;

public sealed class OfferOptions
{
    /// <summary>How long a driver has to answer an offer before it expires.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Creating offers (for MatchingService) and accepting/rejecting them (for drivers).</summary>
public class OfferService(DriverDbContext db, OutboxSignal outboxSignal, IOptions<OfferOptions> options, ILogger<OfferService> logger)
{
    public const string Source = "driver-service";

    /// <summary>
    /// Reserves the driver and stores the offer. Idempotent: a repeated request with the same
    /// RequestKey returns the existing offer (created = false) instead of creating a second one.
    /// </summary>
    public async Task<(RideOffer Offer, bool Created)> CreateOfferAsync(CreateOfferRequest request, Guid correlationId, CancellationToken ct)
    {
        var existing = await db.Offers.FirstOrDefaultAsync(o => o.RequestKey == request.RequestKey, ct);
        if (existing is not null) return (existing, false);

        if (await db.ClosedRides.AnyAsync(c => c.RideId == request.RideId, ct))
            throw new GoneException($"Ride {request.RideId} was cancelled.");

        var driver = await db.Drivers.FindAsync([request.DriverId], ct) ?? throw new NotFoundException("Driver not found.");
        driver.ReserveFor(request.RideId); // throws Conflict if the driver is no longer available

        var now = DateTimeOffset.UtcNow;
        var offer = new RideOffer
        {
            Id = Guid.NewGuid(),
            RequestKey = request.RequestKey,
            RideId = request.RideId,
            CorrelationId = correlationId,
            DriverId = driver.Id,
            PassengerId = request.PassengerId,
            PassengerName = request.PassengerName,
            Pickup = request.Pickup,
            Destination = request.Destination,
            Attempt = request.Attempt,
            IsExperiment = driver.IsExperimentDriver,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(options.Value.TimeoutSeconds),
        };
        db.Offers.Add(offer);

        var payload = new RideOfferCreatedPayload(offer.Id, offer.RideId, driver.Id, driver.Name, offer.PassengerId,
            offer.PassengerName, offer.Pickup, offer.Destination, offer.Attempt, offer.ExpiresAt);
        db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideOfferCreated, correlationId, Source, payload), OutboxDestinations.RabbitMq);

        try
        {
            await SaveAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another offer reserved this driver at the same moment (optimistic concurrency on Driver).
            throw new ConflictException($"Driver {driver.Name} was just reserved by another offer.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same request arrived twice concurrently; the other one won.
            db.ChangeTracker.Clear();
            return (await db.Offers.FirstAsync(o => o.RequestKey == request.RequestKey, ct), false);
        }

        logger.LogInformation("Offer {OfferId}: ride {RideId} → driver {Driver} (attempt {Attempt})", offer.Id, offer.RideId, driver.Name, offer.Attempt);
        return (offer, true);
    }

    public async Task<RideOffer> AcceptAsync(Guid offerId, Guid driverId, CancellationToken ct)
    {
        var (offer, driver) = await LoadForResponseAsync(offerId, driverId, ct);

        if (offer.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await CloseOfferAsync(offer, driver, OfferStatus.Expired, "Expired", ct);
            throw new ConflictException("The offer has expired.");
        }

        offer.Respond(OfferStatus.Accepted);
        driver.ConfirmRide(offer.RideId);

        var payload = new DriverAcceptedRidePayload(offer.Id, offer.RideId, driver.Id, driver.Name, driver.Vehicle, driver.LicensePlate, offer.PassengerId);
        db.AddToOutbox(MessageEnvelope.Create(EventTypes.DriverAcceptedRide, offer.CorrelationId, Source, payload), OutboxDestinations.RabbitMq);
        await SaveAsync(ct);

        logger.LogInformation("Driver {Driver} accepted ride {RideId}", driver.Name, offer.RideId);
        return offer;
    }

    public async Task<RideOffer> RejectAsync(Guid offerId, Guid driverId, CancellationToken ct)
    {
        var (offer, driver) = await LoadForResponseAsync(offerId, driverId, ct);
        await CloseOfferAsync(offer, driver, OfferStatus.Rejected, "Rejected by driver", ct);
        logger.LogInformation("Driver {Driver} rejected ride {RideId}", driver.Name, offer.RideId);
        return offer;
    }

    /// <summary>Called periodically by <see cref="OfferExpiryService"/>.</summary>
    public async Task<int> ExpireDueOffersAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.Offers.Where(o => o.Status == OfferStatus.Pending && !o.IsExperiment && o.ExpiresAt <= now).ToListAsync(ct);
        foreach (var offer in due)
        {
            var driver = await db.Drivers.FindAsync([offer.DriverId], ct);
            if (driver is null) continue;
            await CloseOfferAsync(offer, driver, OfferStatus.Expired, "Offer expired (no answer)", ct);
            logger.LogInformation("Offer {OfferId} for ride {RideId} expired", offer.Id, offer.RideId);
        }
        return due.Count;
    }

    // ---------- Events from RideService ----------

    public async Task HandleRideCompletedAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return;
        var ride = message.PayloadAs<RideStatusChangedPayload>();
        if (ride.DriverId is { } driverId && await db.Drivers.FindAsync([driverId], ct) is { } driver)
            driver.FinishRide(ride.RideId);
        db.MarkProcessed(message);
        await SaveAsync(ct);
    }

    public async Task HandleRideCancelledAsync(MessageEnvelope message, CancellationToken ct)
    {
        if (await db.IsProcessedAsync(message.EventId, ct)) return;
        var ride = message.PayloadAs<RideStatusChangedPayload>();

        if (!await db.ClosedRides.AnyAsync(c => c.RideId == ride.RideId, ct))
            db.ClosedRides.Add(new ClosedRide { RideId = ride.RideId, ClosedAt = DateTimeOffset.UtcNow });

        // Withdraw a pending offer and free every driver that is reserved for / driving this ride.
        var pending = await db.Offers.Where(o => o.RideId == ride.RideId && o.Status == OfferStatus.Pending).ToListAsync(ct);
        foreach (var offer in pending)
        {
            offer.Respond(OfferStatus.Cancelled);
            var withdrawn = new RideOfferWithdrawnPayload(offer.Id, offer.RideId, offer.DriverId, "The passenger cancelled the ride.");
            db.AddToOutbox(MessageEnvelope.Create(EventTypes.RideOfferWithdrawn, offer.CorrelationId, Source, withdrawn, message.EventId),
                OutboxDestinations.RabbitMq);
        }

        var drivers = await db.Drivers.Where(d => d.CurrentRideId == ride.RideId).ToListAsync(ct);
        foreach (var driver in drivers) driver.Release(ride.RideId);

        db.MarkProcessed(message);
        await SaveAsync(ct);
    }

    // ---------- Helpers ----------

    private async Task<(RideOffer, Driver)> LoadForResponseAsync(Guid offerId, Guid driverId, CancellationToken ct)
    {
        var offer = await db.Offers.FindAsync([offerId], ct) ?? throw new NotFoundException("Offer not found.");
        if (offer.DriverId != driverId) throw new ForbiddenException("This offer belongs to another driver.");
        if (offer.Status != OfferStatus.Pending) throw new ConflictException($"Offer is already {offer.Status}.");
        var driver = await db.Drivers.FindAsync([driverId], ct) ?? throw new NotFoundException("Driver not found.");
        return (offer, driver);
    }

    /// <summary>Rejected or expired: free the driver and ask MatchingService to try the next driver.</summary>
    private async Task CloseOfferAsync(RideOffer offer, Driver driver, OfferStatus outcome, string reason, CancellationToken ct)
    {
        offer.Respond(outcome);
        driver.Release(offer.RideId);

        var triedDrivers = await db.Offers.Where(o => o.RideId == offer.RideId).Select(o => o.DriverId).Distinct().ToListAsync(ct);
        var payload = new DriverRejectedRidePayload(offer.Id, offer.RideId, driver.Id, driver.Name, offer.PassengerId,
            offer.PassengerName, offer.Pickup, offer.Destination, offer.Attempt, reason, triedDrivers);
        db.AddToOutbox(MessageEnvelope.Create(EventTypes.DriverRejectedRide, offer.CorrelationId, Source, payload), OutboxDestinations.RabbitMq);
        await SaveAsync(ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        outboxSignal.Notify();
    }
}

/// <summary>Expires unanswered offers every few seconds, so a ride never waits forever for a driver.</summary>
public class OfferExpiryService(IServiceScopeFactory scopeFactory, ILogger<OfferExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OfferService>().ExpireDueOffersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Expiring offers failed: {Message}", ex.Message);
            }
        }
    }
}
