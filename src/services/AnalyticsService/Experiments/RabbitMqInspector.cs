using System.Net.Http.Json;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RideHailing.Shared.Messaging;

namespace AnalyticsService.Experiments;

/// <summary>
/// Reads queue state for the experiments.
/// - Queue depth and consumer count via AMQP passive declare: exact and immediate (the management API's
///   statistics lag several seconds behind).
/// - Durability configuration via the management HTTP API: the queue's "durable" flag and the delivery mode
///   of a waiting message, so durability is verified, not assumed.
/// </summary>
public sealed class RabbitMqInspector(RabbitMqConnection connection, IHttpClientFactory httpClientFactory)
{
    public const string ManagementClient = "rabbitmq-management";

    public async Task<(uint Messages, uint Consumers)?> GetQueueStateAsync(string queue, CancellationToken ct)
    {
        var conn = await connection.GetAsync(ct);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);
        try
        {
            var ok = await channel.QueueDeclarePassiveAsync(queue, ct);
            return (ok.MessageCount, ok.ConsumerCount);
        }
        catch (OperationInterruptedException)
        {
            return null; // queue does not exist (e.g. auto-deleted)
        }
    }

    public sealed record DurabilityCheck(bool? QueueDurable, int? MessageDeliveryMode, string Note);

    /// <summary>Queue "durable" flag and the delivery_mode (2 = persistent) of the first waiting message.</summary>
    public async Task<DurabilityCheck> CheckDurabilityAsync(string queue, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(ManagementClient);
        try
        {
            var info = await client.GetFromJsonAsync<JsonElement>($"/api/queues/%2F/{Uri.EscapeDataString(queue)}", ct);
            bool? durable = info.TryGetProperty("durable", out var d) ? d.GetBoolean() : null;

            // Peek one message and put it back (ack_requeue_true) to read its properties.
            var response = await client.PostAsJsonAsync($"/api/queues/%2F/{Uri.EscapeDataString(queue)}/get",
                new { count = 1, ackmode = "ack_requeue_true", encoding = "auto", truncate = 200 }, ct);
            var messages = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            int? deliveryMode = messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0
                && messages[0].GetProperty("properties").TryGetProperty("delivery_mode", out var m) ? m.GetInt32() : null;

            return new DurabilityCheck(durable, deliveryMode,
                deliveryMode is null ? "No message was waiting to inspect." : "Read via the RabbitMQ management API (message requeued).");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new DurabilityCheck(null, null, $"Management API not reachable: {ex.Message}");
        }
    }
}
