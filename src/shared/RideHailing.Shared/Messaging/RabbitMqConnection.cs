using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace RideHailing.Shared.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>How often a failing message is re-delivered before it goes to the dead-letter queue.</summary>
    public int MaxRetries { get; set; } = 3;

    public ushort PrefetchCount { get; set; } = 10;
}

/// <summary>
/// One long-lived RabbitMQ connection per service (connections are expensive; channels are cheap).
/// Connects lazily and keeps retrying while the broker is not reachable yet.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ServiceIdentity identity, ILogger<RabbitMqConnection> logger)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct = default)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.Host,
                Port = o.Port,
                UserName = o.Username,
                Password = o.Password,
                ClientProvidedName = identity.Name,
                AutomaticRecoveryEnabled = true, // reconnect automatically if the broker restarts
                TopologyRecoveryEnabled = true,
            };

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync(ct);
                    await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
                    await RabbitMqTopology.DeclareAsync(channel, ct);
                    logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", o.Host, o.Port);
                    return _connection;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
                    logger.LogWarning("RabbitMQ not reachable (attempt {Attempt}): {Message}. Retrying in {Delay}s",
                        attempt, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}

/// <summary>The name of the running service, used in logs, messages and metrics.</summary>
public sealed record ServiceIdentity(string Name);
