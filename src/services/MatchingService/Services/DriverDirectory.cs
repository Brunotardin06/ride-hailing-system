using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RideHailing.Grpc;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace MatchingService.Services;

public sealed record AvailableDriver(Guid Id, string Name, string Vehicle, string LicensePlate, double Rating, DateTimeOffset? AvailableSince);

public enum DriverLookupMode { Rest, Grpc }

public sealed class DriverLookupOptions
{
    /// <summary>Which transport MatchingService uses for the driver-availability lookup: Rest or Grpc.</summary>
    public DriverLookupMode Mode { get; set; } = DriverLookupMode.Rest;
}

/// <summary>
/// "Give me available drivers" — the synchronous call to DriverService.
/// Two implementations with identical input/output: REST (HTTP/1.1 + JSON) and gRPC (HTTP/2 + Protobuf).
/// </summary>
public interface IDriverDirectory
{
    DriverLookupMode Mode { get; }
    Task<IReadOnlyList<AvailableDriver>> GetAvailableDriversAsync(IReadOnlyCollection<Guid> excludeDriverIds, int maxResults, CancellationToken ct, bool experimentPool = false);
}

/// <summary>GET {DriverService}/api/drivers/available?exclude=..&amp;max=..</summary>
public sealed class RestDriverDirectory(HttpClient http) : IDriverDirectory
{
    public DriverLookupMode Mode => DriverLookupMode.Rest;

    public async Task<IReadOnlyList<AvailableDriver>> GetAvailableDriversAsync(IReadOnlyCollection<Guid> excludeDriverIds, int maxResults, CancellationToken ct, bool experimentPool = false)
    {
        var query = string.Join("", excludeDriverIds.Select(id => $"&exclude={id}")) + (experimentPool ? "&pool=experiment" : "");
        var drivers = await http.GetFromJsonAsync<List<AvailableDriver>>($"/api/drivers/available?max={maxResults}{query}", Json.Options, ct);
        return drivers ?? [];
    }
}

/// <summary>DriverAvailability.GetAvailableDrivers over gRPC.</summary>
public sealed class GrpcDriverDirectory(DriverAvailability.DriverAvailabilityClient client) : IDriverDirectory
{
    public DriverLookupMode Mode => DriverLookupMode.Grpc;

    public async Task<IReadOnlyList<AvailableDriver>> GetAvailableDriversAsync(IReadOnlyCollection<Guid> excludeDriverIds, int maxResults, CancellationToken ct, bool experimentPool = false)
    {
        var request = new GetAvailableDriversRequest { MaxResults = maxResults, ExperimentPool = experimentPool };
        request.ExcludeDriverIds.AddRange(excludeDriverIds.Select(id => id.ToString()));

        var reply = await client.GetAvailableDriversAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        return reply.Drivers.Select(d => new AvailableDriver(
            Guid.Parse(d.Id), d.Name, d.Vehicle, d.LicensePlate, d.Rating,
            d.AvailableSinceUnixMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(d.AvailableSinceUnixMs) : null)).ToList();
    }
}

/// <summary>
/// Used by the matching workflow: picks the configured transport and measures every call
/// (metric: REST/gRPC · call · driver-lookup). The mode can be overridden at runtime for experiments.
/// </summary>
public sealed class DriverLookup(
    IEnumerable<IDriverDirectory> directories,
    IOptionsMonitor<DriverLookupOptions> options,
    IMetricsReporter metrics,
    ILogger<DriverLookup> logger)
{
    private static DriverLookupMode? _override;

    public DriverLookupMode CurrentMode => _override ?? options.CurrentValue.Mode;
    public DriverLookupMode ConfiguredMode => options.CurrentValue.Mode;

    public static void OverrideMode(DriverLookupMode? mode) => _override = mode;

    public IDriverDirectory Get(DriverLookupMode mode) => directories.Single(d => d.Mode == mode);

    public async Task<IReadOnlyList<AvailableDriver>> GetAvailableDriversAsync(IReadOnlyCollection<Guid> exclude, int max, Guid correlationId, CancellationToken ct, bool experimentPool = false)
    {
        var mode = CurrentMode;
        var mechanism = mode == DriverLookupMode.Grpc ? Mechanisms.Grpc : Mechanisms.Rest;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var drivers = await Get(mode).GetAvailableDriversAsync(exclude, max, ct, experimentPool);
            metrics.Record(mechanism, MetricKinds.Call, "driver-lookup", stopwatch.Elapsed.TotalMilliseconds, true, correlationId);
            logger.LogInformation("Driver lookup via {Mode}: {Count} available ({Elapsed:F1} ms)", mode, drivers.Count, stopwatch.Elapsed.TotalMilliseconds);
            return drivers;
        }
        catch (Exception ex)
        {
            metrics.Record(mechanism, MetricKinds.Call, "driver-lookup", stopwatch.Elapsed.TotalMilliseconds, false, correlationId, ex.Message);
            throw;
        }
    }
}
