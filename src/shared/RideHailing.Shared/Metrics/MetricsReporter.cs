using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Messaging;

namespace RideHailing.Shared.Metrics;

/// <summary>Communication mechanisms being compared.</summary>
public static class Mechanisms
{
    public const string Rest = "REST";
    public const string Grpc = "gRPC";
    public const string RabbitMq = "RabbitMQ";
    public const string Kafka = "Kafka";
    public const string SignalR = "SignalR";
}

/// <summary>
/// What kind of interval a measurement covers. Only measurements with the same kind (and operation)
/// are comparable. The exact start/end of each kind is documented in docs/experiments.md.
/// </summary>
public static class MetricKinds
{
    /// <summary>Synchronous call: client starts the call → client has the deserialised response.</summary>
    public const string Call = "call";
    /// <summary>Asynchronous message: publisher hands message to broker → consumer receives it.</summary>
    public const string Delivery = "delivery";
    /// <summary>Consumer receives message → consumer finished handling it (before ack/commit).</summary>
    public const string Processing = "processing";
    /// <summary>Server sends a push notification → server receives the client's acknowledgement.</summary>
    public const string AckRoundTrip = "ack-rtt";
    /// <summary>Publisher calls BasicPublish → broker confirms it has stored the message.</summary>
    public const string PublishConfirm = "publish-confirm";
}

/// <summary>One measured interval, as sent to AnalyticsService.</summary>
public sealed record MeasurementDto(
    string Mechanism,
    string Kind,
    string Operation,
    double DurationMs,
    bool Success,
    DateTimeOffset RecordedAt,
    string Source,
    Guid? CorrelationId = null,
    string? Error = null);

public interface IMetricsReporter
{
    void Record(string mechanism, string kind, string operation, double durationMs, bool success,
        Guid? correlationId = null, string? error = null);
}

/// <summary>Used when a service does not report metrics (e.g. AnalyticsService stores its own directly).</summary>
public sealed class NullMetricsReporter : IMetricsReporter
{
    public void Record(string mechanism, string kind, string operation, double durationMs, bool success,
        Guid? correlationId = null, string? error = null) { }
}

public sealed class MetricsOptions
{
    public bool Enabled { get; set; } = true;
    public string AnalyticsUrl { get; set; } = "http://localhost:5005";
    public int FlushIntervalMs { get; set; } = 2000;
}

/// <summary>
/// Collects measurements in memory and sends them to AnalyticsService in batches every few seconds.
/// Recording is non-blocking, so measuring does not slow down the code being measured.
/// If AnalyticsService is unreachable the batch is dropped (and logged): metrics must never break the ride flow.
/// </summary>
public sealed class MetricsReporter(
    IHttpClientFactory httpClientFactory,
    IOptions<MetricsOptions> options,
    ServiceIdentity identity,
    ILogger<MetricsReporter> logger) : BackgroundService, IMetricsReporter
{
    public const string HttpClientName = "analytics-metrics";

    private readonly Channel<MeasurementDto> _buffer = Channel.CreateBounded<MeasurementDto>(
        new BoundedChannelOptions(20_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Record(string mechanism, string kind, string operation, double durationMs, bool success,
        Guid? correlationId = null, string? error = null)
    {
        if (!options.Value.Enabled) return;
        _buffer.Writer.TryWrite(new MeasurementDto(mechanism, kind, operation, Math.Round(durationMs, 3), success,
            DateTimeOffset.UtcNow, identity.Name, correlationId, error));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.FlushIntervalMs));
        while (await WaitAsync(timer, stoppingToken))
            await FlushAsync(stoppingToken);
        await FlushAsync(CancellationToken.None);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var batch = new List<MeasurementDto>();
        while (batch.Count < 1000 && _buffer.Reader.TryRead(out var item)) batch.Add(item);
        if (batch.Count == 0) return;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync("/api/metrics/measurements", batch, Json.Options, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Dropped {Count} measurements, AnalyticsService unreachable: {Message}", batch.Count, ex.Message);
        }
    }
}
