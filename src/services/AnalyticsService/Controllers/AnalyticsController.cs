using AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers;

/// <summary>Responsibility A — business event analytics from the Kafka "ride-events" stream.</summary>
[ApiController]
[Route("api/analytics")]
public class AnalyticsController(BusinessAnalyticsService analytics, KafkaInspector kafka) : ControllerBase
{
    /// <summary>Ride counts, rates, durations and an events-per-minute timeline.</summary>
    [HttpGet("summary")]
    public Task<BusinessSummaryDto> GetSummary(int timelineMinutes = 30, CancellationToken ct = default) =>
        analytics.GetSummaryAsync(Math.Clamp(timelineMinutes, 1, 24 * 60), ct);

    /// <summary>Stored events, newest first (as consumed by the analytics consumer group).</summary>
    [HttpGet("events")]
    public Task<List<RideEventDto>> GetEvents(int limit = 100, Guid? rideId = null, string? eventType = null, CancellationToken ct = default) =>
        analytics.GetEventsAsync(limit, rideId, eventType, ct);

    /// <summary>Reads the latest records directly from Kafka with a new, independent consumer group.</summary>
    [HttpGet("kafka/stream")]
    public StreamReadDto ReadStream(int limit = 25) => kafka.ReadLatest(Math.Clamp(limit, 1, 200));

    /// <summary>Committed offsets and lag of the "analytics-service" consumer group per partition.</summary>
    [HttpGet("kafka/consumer-group")]
    public ConsumerGroupStatusDto GetConsumerGroup() => kafka.GetConsumerGroupStatus();
}
