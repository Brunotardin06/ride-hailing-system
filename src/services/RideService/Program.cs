// RideService — owns ride requests and the ride lifecycle.
// REST in (frontend), RabbitMQ out/in (workflow) or REST out (synchronous matching, Experiment A),
// Kafka out (ride event stream). See README.md.

using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Outbox;
using RideService.Data;
using RideService.Messaging;
using RideService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiExceptionHandling();

builder.Services.AddServiceIdentity(RideWorkflow.Source);
builder.Services.AddDbContext<RideDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<RideWorkflow>();

// Synchronous matching path (Experiment A): REST client for MatchingService. Timeouts are enforced per call.
builder.Services.Configure<MatchingClientOptions>(builder.Configuration.GetSection("Matching"));
builder.Services.AddHttpClient(MatchingRestClient.MainClient, c =>
{
    c.BaseAddress = new Uri(builder.Configuration["ServiceUrls:MatchingService"]!);
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient(MatchingRestClient.ExperimentClient, c =>
{
    c.BaseAddress = new Uri(builder.Configuration["ServiceUrls:MatchingExperiment"]!);
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddTransient<MatchingRestClient>();

// Messaging: RabbitMQ (publisher + consumer), Kafka (outbox destination), metrics to AnalyticsService.
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddRabbitMqConsumer<RideMessageHandler>(RabbitMqTopology.Queues.RideService);
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IOutboxTransport, KafkaRideEventProducer>();
builder.Services.AddOutbox<RideDbContext>();
builder.Services.AddMetricsReporter(builder.Configuration);

var app = builder.Build();

await app.Services.RetryOnStartupAsync("Apply database migrations",
    sp => sp.GetRequiredService<RideDbContext>().Database.MigrateAsync());
await app.Services.RetryOnStartupAsync("Ensure Kafka topic",
    sp => KafkaRideEventProducer.EnsureTopicAsync(sp.GetRequiredService<IOptions<KafkaOptions>>().Value));

if (app.Environment.IsDevelopment())
{
    // OpenAPI document: /openapi/v1.json
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseCors();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
