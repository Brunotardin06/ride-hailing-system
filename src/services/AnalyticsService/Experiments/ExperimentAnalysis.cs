using AnalyticsService.Services;

namespace AnalyticsService.Experiments;

/// <summary>
/// Pure functions that turn raw experiment data into outcomes and statistics (unit-tested, no I/O).
/// </summary>
public static class ExperimentAnalysis
{
    /// <summary>
    /// Business outcome of one Experiment A ride. The comparison is about this outcome (was a ride offer created?),
    /// not about the HTTP response alone.
    /// </summary>
    public static string ClassifyRide(bool accepted, string? finalStatus, bool offerCreated) => (accepted, offerCreated) switch
    {
        (true, true) => "offer-created",
        (true, false) when finalStatus == "NoDriverAvailable" => "no-driver",
        (true, false) => "pending",          // accepted for processing, but not matched before the run ended
        (false, true) => "late-offer",       // request failed (e.g. timeout) but MatchingService still created an offer
        (false, false) => "failed",
    };

    /// <summary>Per-ride ordering check: receipts of one ride must arrive with increasing sequence numbers.</summary>
    public static int CountOutOfOrder(IEnumerable<(Guid RideId, int Seq)> receiptsInArrivalOrder)
    {
        var last = new Dictionary<Guid, int>();
        var outOfOrder = 0;
        foreach (var (rideId, seq) in receiptsInArrivalOrder)
        {
            if (last.TryGetValue(rideId, out var previous) && seq < previous) outOfOrder++;
            last[rideId] = Math.Max(previous, seq);
        }
        return outOfOrder;
    }

    /// <summary>Received / unique / duplicates for one consumer, given the ids it received (in order).</summary>
    public static (int Received, int Unique, int Duplicates) CountDeliveries(IReadOnlyCollection<Guid> receivedEventIds)
    {
        var unique = receivedEventIds.Distinct().Count();
        return (receivedEventIds.Count, unique, receivedEventIds.Count - unique);
    }

    public static double? P(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : Statistics.Round(Statistics.Percentile(sorted, percentile));
    }

    public static double? Max(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Statistics.Round(list.Max());
    }

    public static string Ms(double? value) => value is null ? "n/a" : value >= 1000 ? $"{value / 1000:F2} s" : $"{value:F0} ms";
}
