using System.Text.Json;
using AnalyticsService.Data;
using AnalyticsService.Services;
using DriverService.Models;
using Microsoft.EntityFrameworkCore;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideService.Models;

namespace RideHailing.UnitTests;

/// <summary>Ride lifecycle rules (RideService.Models.Ride).</summary>
public class RideStateMachineTests
{
    private static readonly Guid Driver = Guid.NewGuid();

    private static Ride NewRide() => Ride.Request(Guid.NewGuid(), "Alice", "A", "B", Guid.NewGuid());

    [Fact]
    public void Full_lifecycle_sets_status_and_timestamps()
    {
        var ride = NewRide();
        ride.AssignDriver(Driver, "Emma", "Prius", "GX-123-B");
        ride.Start(Driver);
        ride.Complete(Driver);

        Assert.Equal(RideStatus.Completed, ride.Status);
        Assert.NotNull(ride.AssignedAt);
        Assert.NotNull(ride.StartedAt);
        Assert.NotNull(ride.CompletedAt);
        Assert.False(ride.IsActive);
    }

    [Fact]
    public void Cannot_complete_before_start() =>
        Assert.Throws<ConflictException>(() =>
        {
            var ride = NewRide();
            ride.AssignDriver(Driver, "Emma", "Prius", "GX-123-B");
            ride.Complete(Driver);
        });

    [Fact]
    public void Only_assigned_driver_can_start()
    {
        var ride = NewRide();
        ride.AssignDriver(Driver, "Emma", "Prius", "GX-123-B");
        Assert.Throws<ForbiddenException>(() => ride.Start(Guid.NewGuid()));
    }

    [Fact]
    public void Cannot_cancel_a_ride_in_progress()
    {
        var ride = NewRide();
        ride.AssignDriver(Driver, "Emma", "Prius", "GX-123-B");
        ride.Start(Driver);
        Assert.Throws<ConflictException>(() => ride.Cancel("too late"));
    }

    [Fact]
    public void Cannot_assign_twice() =>
        Assert.Throws<ConflictException>(() =>
        {
            var ride = NewRide();
            ride.AssignDriver(Driver, "Emma", "Prius", "GX-123-B");
            ride.AssignDriver(Guid.NewGuid(), "Lucas", "Tesla", "TS-482-K");
        });

    [Theory]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    [InlineData(RideStatus.NoDriverAvailable)]
    public void Terminal_states_allow_no_transition(RideStatus terminal)
    {
        foreach (var next in Enum.GetValues<RideStatus>())
            Assert.False(Ride.CanTransition(terminal, next));
    }
}

/// <summary>Driver availability rules (DriverService.Models.Driver).</summary>
public class DriverStateTests
{
    private static Driver NewDriver(DriverStatus status = DriverStatus.Available) =>
        Driver.Seed(Guid.NewGuid(), "Emma", "Prius", "GX-123-B", 4.9, status, DateTimeOffset.UtcNow);

    [Fact]
    public void Reserved_driver_cannot_be_reserved_again()
    {
        var driver = NewDriver();
        driver.ReserveFor(Guid.NewGuid());
        Assert.Equal(DriverStatus.Offered, driver.Status);
        Assert.Throws<ConflictException>(() => driver.ReserveFor(Guid.NewGuid()));
    }

    [Fact]
    public void Offline_driver_cannot_be_reserved() =>
        Assert.Throws<ConflictException>(() => NewDriver(DriverStatus.Offline).ReserveFor(Guid.NewGuid()));

    [Fact]
    public void Cannot_go_offline_while_busy()
    {
        var driver = NewDriver();
        var ride = Guid.NewGuid();
        driver.ReserveFor(ride);
        driver.ConfirmRide(ride);
        Assert.Throws<ConflictException>(() => driver.SetAvailability(false));
    }

    [Fact]
    public void Finishing_ride_frees_driver_and_counts_it()
    {
        var driver = NewDriver();
        var ride = Guid.NewGuid();
        driver.ReserveFor(ride);
        driver.ConfirmRide(ride);
        driver.FinishRide(ride);
        Assert.Equal(DriverStatus.Available, driver.Status);
        Assert.Null(driver.CurrentRideId);
        Assert.Equal(1, driver.CompletedRides);
    }

    [Fact]
    public void Release_for_other_ride_is_ignored()
    {
        var driver = NewDriver();
        var ride = Guid.NewGuid();
        driver.ReserveFor(ride);
        driver.Release(Guid.NewGuid());
        Assert.Equal(DriverStatus.Offered, driver.Status);
    }

    [Fact]
    public void Offer_can_only_be_answered_once()
    {
        var offer = new RideOffer();
        offer.Respond(OfferStatus.Accepted);
        Assert.Throws<ConflictException>(() => offer.Respond(OfferStatus.Rejected));
    }
}

/// <summary>Percentile calculation used for all experiment results.</summary>
public class StatisticsTests
{
    [Fact]
    public void Nearest_rank_percentiles()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        Assert.Equal(50, Statistics.Percentile(values, 50));
        Assert.Equal(95, Statistics.Percentile(values, 95));
        Assert.Equal(99, Statistics.Percentile(values, 99));
        Assert.Equal(100, Statistics.Percentile(values, 100));
    }

    [Fact]
    public void Summary_of_unsorted_values()
    {
        var stats = Statistics.Summarize([5, 1, 3, 2, 4]);
        Assert.Equal(5, stats.Count);
        Assert.Equal(3, stats.AvgMs);
        Assert.Equal(3, stats.P50Ms);
        Assert.Equal(5, stats.P95Ms);
        Assert.Equal(1, stats.MinMs);
    }

    [Fact]
    public void Empty_summary_has_no_values() => Assert.Null(Statistics.Summarize([]).AvgMs);
}

/// <summary>k6 summary import (AnalyticsService), using an in-memory database.</summary>
public class K6ImportTests
{
    [Fact]
    public async Task Imports_duration_count_errors_and_payload_size()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AnalyticsDbContext(options);
        var service = new ExperimentRunService(db);

        var summary = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["metrics"] = new Dictionary<string, object>
            {
                ["grpc_req_duration"] = new Dictionary<string, double> { ["avg"] = 4.1, ["med"] = 3.6, ["max"] = 35, ["p(95)"] = 7.1, ["p(99)"] = 10.5 },
                ["iterations"] = new Dictionary<string, double> { ["count"] = 1000, ["rate"] = 200 },
                ["req_failed"] = new Dictionary<string, double> { ["passes"] = 10, ["fails"] = 990, ["value"] = 0.01 },
                ["data_received"] = new Dictionary<string, double> { ["count"] = 262_000, ["rate"] = 1 },
            },
        }, Json.Options);

        var run = await service.ImportK6Async(new K6ImportRequest("test", "gRPC", "driver-lookup (k6 client)", "10 VUs", summary, null), default);

        Assert.Equal(1000, run.Count);
        Assert.Equal(10, run.Errors);
        Assert.Equal(0.01, run.ErrorRate);
        Assert.Equal(4.1, run.AvgMs);
        Assert.Equal(7.1, run.P95Ms);
        Assert.Equal(198, run.ThroughputPerSecond); // successful calls only
        Assert.Equal("262", run.Parameters!["bytesReceivedPerCall"]);
    }
}
