using AnalyticsService.Models;
using Microsoft.EntityFrameworkCore;

namespace AnalyticsService.Data;

/// <summary>AnalyticsService's own database ("analytics"): processed events and experiment results.</summary>
public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<RideEvent> RideEvents => Set<RideEvent>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<ExperimentRun> ExperimentRuns => Set<ExperimentRun>();
    public DbSet<ScenarioFinding> ScenarioFindings => Set<ScenarioFinding>();
    public DbSet<TradeOffNote> TradeOffNotes => Set<TradeOffNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RideEvent>(e =>
        {
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => x.RideId);
            e.HasIndex(x => new { x.EventType, x.OccurredAt });
            e.Property(x => x.EventType).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(50);
        });

        modelBuilder.Entity<Measurement>(e =>
        {
            e.HasIndex(x => new { x.RunId, x.RecordedAt });
            e.HasIndex(x => new { x.Mechanism, x.Kind, x.Operation });
            e.Property(x => x.Mechanism).HasMaxLength(20);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Operation).HasMaxLength(150);
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.Error).HasMaxLength(500);
        });

        modelBuilder.Entity<ExperimentRun>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Mechanism).HasMaxLength(20);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Operation).HasMaxLength(150);
            e.HasMany<Measurement>().WithOne().HasForeignKey(m => m.RunId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.ExperimentType).HasMaxLength(20);
            e.Property(x => x.ScenarioKey).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.ExperimentType, x.CreatedAt });
            e.HasIndex(x => x.GroupId);
        });

        modelBuilder.Entity<ScenarioFinding>(e => e.HasKey(x => x.Key));
        modelBuilder.Entity<TradeOffNote>(e => e.HasKey(x => new { x.Experiment, x.Criterion, x.Approach }));
    }
}
