using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Messaging;

namespace RideHailing.Shared.Kafka;

// Kafka stream of driver GPS positions: topic "driver-locations", message key = driverId.
//
//   RealtimeService (producer) ──► topic "driver-locations" (3 partitions) ──► consumer group "realtime-service"
//                                                                         └──► consumer group "matching-service"
//
// Kafka vs RabbitMQ in one sentence: RabbitMQ DELIVERS a message to a queue and deletes it after the ack;
// Kafka APPENDS it to a log that is kept (retention). Every consumer GROUP reads the whole log independently
// and remembers its own position (offset). So two services read the same positions without copies.
//
// Key = driverId → all positions of one driver go to the SAME partition → they are read IN ORDER
// (the car never "jumps back"). Different drivers spread over partitions → parallelism.

/// <summary>"Kafka" section of appsettings.json.</summary>
public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
}

public static class KafkaTopics
{
    public const string DriverLocations = "driver-locations";
    public const int DriverLocationPartitions = 3;
}

/// <summary>One GPS position of a driver (the Kafka message value, as JSON).</summary>
public sealed record DriverLocation(Guid DriverId, double Lat, double Lng, DateTimeOffset RecordedAt);

/// <summary>Each consuming service implements this: "what do I do with a position?".</summary>
public interface IDriverLocationHandler
{
    Task HandleAsync(DriverLocation location, CancellationToken ct);
}

/// <summary>Writes positions to the topic. Singleton: one producer per process (it batches internally).</summary>
public sealed class DriverLocationProducer(IOptions<KafkaOptions> options, ILogger<DriverLocationProducer> logger) : IDisposable
{
    private readonly IProducer<string, string> _producer = new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = options.Value.BootstrapServers,
        Acks = Acks.Leader,  // a position is replaced 2 s later anyway: favour speed over the strongest guarantee
        LingerMs = 5,        // wait up to 5 ms to send several positions in one batch
    }).Build();

    public async Task ProduceAsync(DriverLocation location, CancellationToken ct)
    {
        try
        {
            await _producer.ProduceAsync(KafkaTopics.DriverLocations, new Message<string, string>
            {
                Key = location.DriverId.ToString(),                         // → partition (ordering per driver)
                Value = JsonSerializer.Serialize(location, Json.Options),
            }, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            // Losing one position is acceptable (the next one arrives in 2 s); log and continue.
            logger.LogWarning("Could not produce location of {DriverId}: {Reason}", location.DriverId, ex.Error.Reason);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(2));
        _producer.Dispose();
    }
}

/// <summary>
/// Background service: reads the topic as consumer group <c>groupId</c> and passes each position to THandler.
/// Offsets are committed automatically every few seconds: after a restart the group continues where it
/// stopped (catches up on what it missed) instead of starting over.
/// </summary>
public sealed class DriverLocationConsumer<THandler>(
    string groupId,
    IOptions<KafkaOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<DriverLocationConsumer<THandler>> logger) : BackgroundService
    where THandler : IDriverLocationHandler
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() blocks a thread, so run the loop on its own thread instead of the startup thread.
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        await EnsureTopicAsync(options.Value.BootstrapServers, logger, ct);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Latest, // brand-new group: start at "now" (old positions are useless)
            EnableAutoCommit = true,
        }).Build();

        consumer.Subscribe(KafkaTopics.DriverLocations);
        logger.LogInformation("Consuming Kafka topic {Topic} as group {Group}", KafkaTopics.DriverLocations, groupId);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(ct);
                var location = JsonSerializer.Deserialize<DriverLocation>(result.Message.Value, Json.Options);
                if (location is null) continue;

                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<THandler>().HandleAsync(location, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // A bad position is skipped (no retry/DLQ like RabbitMQ: the next position replaces it).
                logger.LogWarning("Skipping driver location: {Message}", ex.Message);
            }
        }
        consumer.Close(); // leave the group cleanly → partitions are reassigned immediately
    }

    /// <summary>Create the topic with 3 partitions if it does not exist yet (idempotent, like declaring a queue).</summary>
    private static async Task EnsureTopicAsync(string servers, ILogger logger, CancellationToken ct)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = servers }).Build();
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await admin.CreateTopicsAsync([new TopicSpecification
                {
                    Name = KafkaTopics.DriverLocations,
                    NumPartitions = KafkaTopics.DriverLocationPartitions,
                    ReplicationFactor = 1,
                }]);
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Kafka not reachable (attempt {Attempt}): {Message}. Retrying in 3s", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}

public static class KafkaServiceCollectionExtensions
{
    public static IServiceCollection AddDriverLocationProducer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.AddSingleton<DriverLocationProducer>();
        return services;
    }

    /// <summary>Read the driver-location topic as consumer group <paramref name="groupId"/>.</summary>
    public static IServiceCollection AddDriverLocationConsumer<THandler>(this IServiceCollection services, IConfiguration configuration, string groupId)
        where THandler : class, IDriverLocationHandler
    {
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.AddScoped<THandler>();
        services.AddHostedService(sp => ActivatorUtilities.CreateInstance<DriverLocationConsumer<THandler>>(sp, groupId));
        return services;
    }
}
