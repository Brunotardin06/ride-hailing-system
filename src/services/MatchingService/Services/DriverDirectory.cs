using RideHailing.Grpc.Drivers;

namespace MatchingService.Services;

public sealed record DriverCandidate(Guid Id, string Name);

/// <summary>"Who is available?" — gRPC GetAvailableDrivers on DriverService.</summary>
public class DriverDirectory(Drivers.DriversClient client)
{
    public async Task<IReadOnlyList<DriverCandidate>> FindAvailableAsync(IEnumerable<Guid> exclude, int max, CancellationToken ct)
    {
        var request = new GetAvailableDriversRequest { MaxResults = max };
        request.ExcludeDriverIds.AddRange(exclude.Select(id => id.ToString()));

        // Deadline: never wait forever on a synchronous call. Past it, the call fails with DeadlineExceeded.
        var reply = await client.GetAvailableDriversAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        return reply.Drivers.Select(d => new DriverCandidate(Guid.Parse(d.Id), d.Name)).ToList();
    }
}
