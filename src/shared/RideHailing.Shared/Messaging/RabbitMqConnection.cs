using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace RideHailing.Shared.Messaging;

/// <summary>"RabbitMQ" section of appsettings.json.</summary>
public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>How often a failing message is retried before it goes to the dead-letter queue.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>How many unacknowledged messages a consumer may hold at once.</summary>
    public ushort PrefetchCount { get; set; } = 10;
}

/// <summary>Name of the running service ("ride-service", …): used as message Source and connection name.</summary>
public sealed record ServiceIdentity(string Name);

/// <summary>
/// One long-lived connection per service (a connection is a TCP socket: expensive; channels inside it are cheap).
/// Connects on first use and keeps retrying while the broker is not reachable yet.
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
                ClientProvidedName = identity.Name,   // visible in the management UI → Connections
                AutomaticRecoveryEnabled = true,      // reconnect automatically if the broker restarts
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
