using DriverService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Outbox;

namespace DriverService.Data;

/// <summary>
/// DriverService's own database ("drivers"). Each DbSet becomes a table.
/// OnModelCreating describes how the C# classes map to tables; "dotnet ef migrations add" turns that into SQL.
/// </summary>
public class DriverDbContext(DbContextOptions<DriverDbContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<RideOffer> Offers => Set<RideOffer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(80);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20); // stored as "Available", not 1
            e.Property(d => d.Version).IsRowVersion();                         // Npgsql maps this to xmin
            e.HasData(DemoDrivers.All);                                         // seed rows (inserted by the migration)
        });

        modelBuilder.Entity<RideOffer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Version).IsRowVersion();
            e.Property(o => o.RequestKey).HasMaxLength(100);
            e.HasIndex(o => o.RequestKey).IsUnique();      // idempotent CreateOffer
            e.HasIndex(o => new { o.DriverId, o.Status }); // "pending offers of driver X"
            e.HasIndex(o => o.RideId);
        });

        modelBuilder.AddOutboxAndInbox(); // OutboxMessages + ProcessedMessages tables
    }
}

/// <summary>
/// Demo drivers. Emma and Lucas start Available (Emma first: she has waited longest), the rest Offline.
/// The ids are fixed because the frontend uses Emma's id as the default driver (state/identity.ts).
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
