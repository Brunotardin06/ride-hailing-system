using DriverService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Outbox;

namespace DriverService.Data;

/// <summary>DriverService's own database ("drivers"): drivers and ride offers.</summary>
public class DriverDbContext(DbContextOptions<DriverDbContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<RideOffer> Offers => Set<RideOffer>();
    public DbSet<ClosedRide> ClosedRides => Set<ClosedRide>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Name).HasMaxLength(80);
            e.Property(d => d.Version).IsRowVersion();
            e.HasData([.. DemoDrivers.All, .. ExperimentDrivers.All]);
        });

        modelBuilder.Entity<RideOffer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.RequestKey).HasMaxLength(200);
            e.HasIndex(o => o.RequestKey).IsUnique(); // idempotent offer creation
            e.HasIndex(o => new { o.DriverId, o.Status });
            e.HasIndex(o => o.RideId);
            e.Property(o => o.Version).IsRowVersion();
        });

        modelBuilder.Entity<ClosedRide>(e => e.HasKey(c => c.RideId));

        modelBuilder.AddOutboxAndInbox();
    }
}

/// <summary>Experiment pool (Experiment A): 20 always-available test drivers, hidden from the demo UI.</summary>
public static class ExperimentDrivers
{
    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public static Driver[] All => Enumerable.Range(1, 20)
        .Select(i => Driver.Seed(Guid.Parse($"11111111-1111-1111-1111-1000000000{i:D2}"), $"Experiment driver {i:D2}",
            "Test vehicle", $"EXP-{i:D2}", 5.0, DriverStatus.Available, SeedTime.AddSeconds(i), experiment: true))
        .ToArray();
}

/// <summary>
/// Development seed data. Emma and Lucas start as Available (Emma first, she has waited longest),
/// so a ride can be demonstrated immediately; the others start Offline.
/// </summary>
public static class DemoDrivers
{
    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public static Driver[] All =>
    [
        Driver.Seed(Guid.Parse("11111111-1111-1111-1111-000000000001"), "Emma de Vries", "Toyota Prius · white", "GX-123-B", 4.9, DriverStatus.Available, SeedTime),
        Driver.Seed(Guid.Parse("11111111-1111-1111-1111-000000000002"), "Lucas Janssen", "Tesla Model 3 · black", "TS-482-K", 4.8, DriverStatus.Available, SeedTime.AddMinutes(1)),
        Driver.Seed(Guid.Parse("11111111-1111-1111-1111-000000000003"), "Sophie Bakker", "Skoda Octavia · grey", "ZK-907-L", 4.7, DriverStatus.Offline, null),
        Driver.Seed(Guid.Parse("11111111-1111-1111-1111-000000000004"), "Daan Visser", "Kia e-Niro · blue", "HN-551-T", 4.6, DriverStatus.Offline, null),
        Driver.Seed(Guid.Parse("11111111-1111-1111-1111-000000000005"), "Noor El Amrani", "Volkswagen ID.4 · red", "PR-318-D", 4.9, DriverStatus.Offline, null),
        Driver.Seed(Guid.Parse("11111111-1111-1111-1111-000000000006"), "Milan Peeters", "Mercedes E-Class · black", "VB-640-S", 5.0, DriverStatus.Offline, null),
    ];
}
