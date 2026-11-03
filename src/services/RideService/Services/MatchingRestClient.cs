using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Contracts;
using RideHailing.Shared.Messaging;
using RideService.Models;

namespace RideService.Services;

public sealed class MatchingClientOptions
{
    /// <summary>Default matching path for normal rides (Experiment A compares both): RabbitMq or Rest.</summary>
    public MatchingMode Mode { get; set; } = MatchingMode.RabbitMq;

    /// <summary>How long RideService waits for a synchronous matching answer before giving up.</summary>
    public int RestTimeoutMs { get; set; } = 3000;
}

/// <summary>The synchronous matching call failed: no answer (timeout), unreachable, or an HTTP error.</summary>
public sealed class MatchingCallFailedException(string kind, string message) : Exception(message)
{
    /// <summary>timeout · unreachable · http-503 · …</summary>
    public string Kind { get; } = kind;
}

/// <summary>
/// REST client for POST {MatchingService}/api/matching/requests (Experiment A, approach "REST").
/// Experiment rides go to the dedicated experiment instance; normal rides to the main instance.
/// </summary>
public sealed class MatchingRestClient(IHttpClientFactory httpClientFactory, IOptions<MatchingClientOptions> options)
{
    public const string MainClient = "matching-main";
    public const string ExperimentClient = "matching-experiment";

    public async Task<MatchingResponse> RequestMatchingAsync(MatchingRequest request, bool experiment, int? timeoutMs, CancellationToken ct)
    {
        var timeout = timeoutMs ?? options.Value.RestTimeoutMs;
        var client = httpClientFactory.CreateClient(experiment ? ExperimentClient : MainClient);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await client.PostAsJsonAsync("/api/matching/requests", request, Json.Options, cts.Token);
            if (!response.IsSuccessStatusCode)
                throw new MatchingCallFailedException($"http-{(int)response.StatusCode}", $"MatchingService answered HTTP {(int)response.StatusCode}.");
            return (await response.Content.ReadFromJsonAsync<MatchingResponse>(Json.Options, cts.Token))!;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MatchingCallFailedException("timeout", $"No answer from MatchingService within {timeout} ms.");
        }
        catch (HttpRequestException ex)
        {
            throw new MatchingCallFailedException("unreachable", $"MatchingService unreachable: {ex.Message}");
        }
    }
}
