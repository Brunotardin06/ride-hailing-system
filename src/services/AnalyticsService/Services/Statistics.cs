namespace AnalyticsService.Services;

public sealed record LatencyStats(int Count, double? AvgMs, double? P50Ms, double? P95Ms, double? P99Ms, double? MinMs, double? MaxMs);

public static class Statistics
{
    /// <summary>Average and percentiles of durations. Percentiles use the nearest-rank method.</summary>
    public static LatencyStats Summarize(IEnumerable<double> durations)
    {
        var sorted = durations.Order().ToArray();
        if (sorted.Length == 0) return new LatencyStats(0, null, null, null, null, null, null);
        return new LatencyStats(
            sorted.Length,
            Round(sorted.Average()),
            Round(Percentile(sorted, 50)),
            Round(Percentile(sorted, 95)),
            Round(Percentile(sorted, 99)),
            Round(sorted[0]),
            Round(sorted[^1]));
    }

    /// <summary>Nearest-rank percentile of an ascending array: the smallest value ≥ p% of all values.</summary>
    public static double Percentile(double[] sortedAscending, double p)
    {
        if (sortedAscending.Length == 0) throw new ArgumentException("No values", nameof(sortedAscending));
        var rank = (int)Math.Ceiling(p / 100.0 * sortedAscending.Length);
        return sortedAscending[Math.Clamp(rank - 1, 0, sortedAscending.Length - 1)];
    }

    public static double Round(double value) => Math.Round(value, 3);
}
