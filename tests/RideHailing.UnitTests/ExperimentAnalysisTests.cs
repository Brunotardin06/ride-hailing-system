using AnalyticsService.Experiments;
using RideService.Models;

namespace RideHailing.UnitTests;

/// <summary>Outcome classification and delivery analysis used by Experiments A and B.</summary>
public class ExperimentAnalysisTests
{
    [Theory]
    [InlineData(true, "Requested", true, "offer-created")]
    [InlineData(true, "NoDriverAvailable", false, "no-driver")]
    [InlineData(true, "Requested", false, "pending")]
    [InlineData(false, "Cancelled", true, "late-offer")]
    [InlineData(false, "Cancelled", false, "failed")]
    [InlineData(false, null, false, "failed")]
    public void Ride_outcome_is_the_business_outcome_not_the_http_status(bool accepted, string? status, bool offer, string expected) =>
        Assert.Equal(expected, ExperimentAnalysis.ClassifyRide(accepted, status, offer));

    [Fact]
    public void Out_of_order_is_counted_per_ride()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        // Ride a: 1,2,3 in order. Ride b: 1,3,2 → one out-of-order receipt. Interleaving between rides is fine.
        var receipts = new[] { (a, 1), (b, 1), (a, 2), (b, 3), (a, 3), (b, 2) };
        Assert.Equal(1, ExperimentAnalysis.CountOutOfOrder(receipts));
    }

    [Fact]
    public void Duplicates_are_received_minus_unique()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var (received, unique, duplicates) = ExperimentAnalysis.CountDeliveries([x, y, x, x]);
        Assert.Equal((4, 2, 2), (received, unique, duplicates));
    }

    [Fact]
    public void Percentiles_of_empty_input_are_null()
    {
        Assert.Null(ExperimentAnalysis.P([], 95));
        Assert.Null(ExperimentAnalysis.Max([]));
    }
}

/// <summary>Experiment A additions to the ride: matching mode and pending matching work.</summary>
public class RideMatchingStateTests
{
    [Fact]
    public void First_offer_time_is_kept_and_driver_name_updated()
    {
        var ride = Ride.Request(Guid.NewGuid(), "Alice", "A", "B", Guid.NewGuid(), MatchingMode.Rest, Guid.NewGuid());
        var first = DateTimeOffset.UtcNow;
        ride.RecordOfferCreated("Emma", first);
        ride.RecordOfferCreated("Lucas", first.AddSeconds(5)); // re-match after a rejection
        Assert.Equal(first, ride.OfferCreatedAt);
        Assert.Equal("Lucas", ride.OfferedDriverName);
        Assert.Equal(MatchingMode.Rest, ride.MatchingMode);
        Assert.NotNull(ride.ExperimentRunId);
    }

    [Fact]
    public void Offer_can_be_recorded_on_a_cancelled_ride_to_reveal_late_work()
    {
        var ride = Ride.Request(Guid.NewGuid(), "Alice", "A", "B", Guid.NewGuid());
        ride.Cancel("Matching unavailable (timeout)");
        ride.RecordOfferCreated("Emma", DateTimeOffset.UtcNow);
        Assert.Equal(RideStatus.Cancelled, ride.Status);
        Assert.NotNull(ride.OfferCreatedAt);
    }
}
