// AnalyticsService — two responsibilities:
//   A. Business event analytics: consumes ride lifecycle events from Kafka and stores them.
//   B. Experiment metrics: stores communication measurements and experiment runs.
// Exposes REST endpoints for the Experiment Dashboard. See README.md.

using System.Text.Json.Serialization;
using AnalyticsService.Data;
using AnalyticsService.Experiments;
using AnalyticsService.Messaging;
using AnalyticsService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiExceptionHandling();

builder.Services.AddServiceIdentity(KafkaRideEventConsumer.Source);
builder.Services.AddDbContext<AnalyticsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<BusinessAnalyticsService>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddScoped<ExperimentRunService>();

// Scenario experiments (A: REST vs RabbitMQ matching, B: RabbitMQ vs Kafka distribution) run in this service,
// because it owns the experiment results. They call RideService/DriverService/matching-experiment over REST
// and use RabbitMQ + Kafka directly (dedicated experiment exchange/queues/topic).
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddHttpClient(ExperimentARunner.RideClient, c => { c.BaseAddress = new Uri(builder.Configuration["ServiceUrls:RideService"]!); c.Timeout = TimeSpan.FromSeconds(60); });
builder.Services.AddHttpClient(ExperimentARunner.DriverClient, c => { c.BaseAddress = new Uri(builder.Configuration["ServiceUrls:DriverService"]!); c.Timeout = TimeSpan.FromSeconds(30); });
builder.Services.AddHttpClient(ExperimentARunner.MatchingExperimentClient, c => { c.BaseAddress = new Uri(builder.Configuration["ServiceUrls:MatchingExperiment"]!); c.Timeout = TimeSpan.FromSeconds(10); });
builder.Services.AddHttpClient(RabbitMqInspector.ManagementClient, c =>
{
    c.BaseAddress = new Uri(builder.Configuration["RabbitMQ:ManagementUrl"]!);
    c.Timeout = TimeSpan.FromSeconds(10);
    var credentials = $"{builder.Configuration["RabbitMQ:Username"]}:{builder.Configuration["RabbitMQ:Password"]}";
    c.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credentials)));
});
builder.Services.AddSingleton<RabbitMqInspector>();
builder.Services.AddSingleton<ExperimentCoordinator>();
builder.Services.AddScoped<ExperimentARunner>();
builder.Services.AddScoped<ExperimentBRunner>();

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<KafkaInspector>();
builder.Services.AddHostedService<KafkaRideEventConsumer>();

var app = builder.Build();

await app.Services.RetryOnStartupAsync("Apply database migrations",
    sp => sp.GetRequiredService<AnalyticsDbContext>().Database.MigrateAsync());
await app.Services.RetryOnStartupAsync("Seed experiment scenarios",
    sp => Findings.EnsureSeededAsync(sp.GetRequiredService<AnalyticsDbContext>()));
await app.Services.RetryOnStartupAsync("Ensure Kafka topic",
    sp => KafkaRideEventConsumer.EnsureTopicAsync(sp.GetRequiredService<IOptions<KafkaOptions>>().Value));

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
