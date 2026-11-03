using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Outbox;
using RideService.Models;

namespace RideService.Data;

/// <summary>RideService's own database ("rides"). No other service accesses it.</summary>
public class RideDbContext(DbContextOptions<RideDbContext> options) : DbContext(options)
{
    public DbSet<Ride> Rides => Set<Ride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ride>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.MatchingMode).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(r => r.ExperimentRunId);
            e.Property(r => r.PassengerName).HasMaxLength(80);
            e.Property(r => r.Pickup).HasMaxLength(200);
            e.Property(r => r.Destination).HasMaxLength(200);
            e.Property(r => r.Version).IsRowVersion(); // maps to PostgreSQL's xmin system column
            e.HasIndex(r => r.DriverId);

            // A passenger can have at most one active ride. Enforced by the database, so two
            // simultaneous requests cannot both succeed.
            e.HasIndex(r => r.PassengerId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Requested', 'Assigned', 'InProgress')")
                .HasDatabaseName("IX_Rides_OneActiveRidePerPassenger");
        });

        modelBuilder.AddOutboxAndInbox();
    }
}
