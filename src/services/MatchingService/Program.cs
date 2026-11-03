// MatchingService — matches ride requests to available drivers. Stateless (no database).
// In: RabbitMQ (asynchronous matching) or REST POST /api/matching/requests (synchronous matching, Experiment A).
// Out: REST or gRPC driver lookup (switchable), REST offer creation, RabbitMQ results. See README.md.

using System.Text.Json.Serialization;
using MatchingService.Messaging;
using MatchingService.Services;
using RideHailing.Grpc;
using RideHailing.Shared.Hosting;
using RideHailing.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiExceptionHandling();

// Two instances run from this code: "Main" for demo rides, "Experiment" for Experiment A (see MatchingOptions.Instance).
var matchingOptions = builder.Configuration.GetSection("Matching").Get<MatchingOptions>() ?? new MatchingOptions();
builder.Services.AddServiceIdentity(matchingOptions.IsExperimentInstance ? "matching-experiment" : MatchingWorkflow.Source);
builder.Services.Configure<MatchingOptions>(builder.Configuration.GetSection("Matching"));
builder.Services.AddSingleton<MatchingService.Controllers.SimulatedOutage>();
builder.Services.AddSingleton<MatchingStats>();
builder.Services.Configure<DriverLookupOptions>(builder.Configuration.GetSection("DriverLookup"));

// Synchronous clients for DriverService: REST (HTTP/1.1) and gRPC (HTTP/2) for the same lookup.
var driverServiceUrl = new Uri(builder.Configuration["ServiceUrls:DriverService"]!);
var driverServiceGrpcUrl = new Uri(builder.Configuration["ServiceUrls:DriverServiceGrpc"]!);
builder.Services.AddHttpClient<RestDriverDirectory>(c => { c.BaseAddress = driverServiceUrl; c.Timeout = TimeSpan.FromSeconds(5); });
builder.Services.AddGrpcClient<DriverAvailability.DriverAvailabilityClient>(o => o.Address = driverServiceGrpcUrl);
builder.Services.AddTransient<GrpcDriverDirectory>();
builder.Services.AddTransient<IDriverDirectory>(sp => sp.GetRequiredService<RestDriverDirectory>());
builder.Services.AddTransient<IDriverDirectory>(sp => sp.GetRequiredService<GrpcDriverDirectory>());
builder.Services.AddTransient<DriverLookup>();
builder.Services.AddHttpClient<OfferClient>(c => { c.BaseAddress = driverServiceUrl; c.Timeout = TimeSpan.FromSeconds(5); });

builder.Services.AddTransient<MatchingWorkflow>();
builder.Services.AddTransient<DriverLookupBenchmark>();
builder.Services.AddHttpClient(DriverLookupBenchmark.AnalyticsClient, c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Metrics:AnalyticsUrl"]!);
    c.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<ProcessedMessageCache>();
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddRabbitMqConsumer<MatchingMessageHandler>(matchingOptions.IsExperimentInstance
    ? RabbitMqTopology.Queues.MatchingExperiment
    : RabbitMqTopology.Queues.MatchingService);
builder.Services.AddMetricsReporter(builder.Configuration);

var app = builder.Build();

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
