using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Outbox;
using RideService.Models;

namespace RideService.Data;

/// <summary>RideService's own database ("rides").</summary>
public class RideDbContext(DbContextOptions<RideDbContext> options) : DbContext(options)
{
    public DbSet<Ride> Rides => Set<Ride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ride>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.PassengerName).HasMaxLength(80);
            e.Property(r => r.Pickup).HasMaxLength(200);
            e.Property(r => r.Destination).HasMaxLength(200);
            e.Property(r => r.Version).IsRowVersion(); // xmin

            // Indexes for the frontend queries: "rides of passenger X" and "active ride of driver Y".
            e.HasIndex(r => new { r.PassengerId, r.RequestedAt });
            e.HasIndex(r => new { r.DriverId, r.Status });
        });

        modelBuilder.AddOutboxAndInbox(); // OutboxMessages + ProcessedMessages tables
    }
}
