using System.Collections.Concurrent;
using RideHailing.Shared.Contracts;

namespace MatchingService.Services;

/// <summary>
/// Counts finished matching operations per outcome (Experiment A). Lets the experiment see work that
/// MatchingService completed even though the caller had already given up (outcome RideClosed after a timeout).
/// </summary>
public sealed class MatchingStats
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public void Record(MatchingOutcome outcome) => Record(outcome.ToString());

    /// <summary>The caller disconnected (e.g. its timeout expired) and the work was cancelled before it finished.</summary>
    public void RecordAborted() => Record("Aborted");

    private void Record(string key) => _counts.AddOrUpdate(key, 1, (_, n) => n + 1);

    public Dictionary<string, int> Snapshot() => new(_counts);

    public void Reset() => _counts.Clear();
}
