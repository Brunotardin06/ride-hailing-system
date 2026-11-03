using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RideHailing.Shared.Messaging;
using RideHailing.Shared.Metrics;

namespace RideHailing.Shared.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the service name used in logs, messages and metrics.</summary>
    public static IServiceCollection AddServiceIdentity(this IServiceCollection services, string name) =>
        services.AddSingleton(new ServiceIdentity(name));

    /// <summary>RabbitMQ connection + publisher, configured from the "RabbitMQ" section.</summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMQ"));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<FailureInjection>();
        services.AddSingleton<ConsumerPauseSwitch>();
        services.TryAddSingleton<IMetricsReporter, NullMetricsReporter>(); // replaced by AddMetricsReporter
        return services;
    }

    /// <summary>Consume <paramref name="queue"/> and dispatch every message to <typeparamref name="THandler"/>.</summary>
    public static IServiceCollection AddRabbitMqConsumer<THandler>(this IServiceCollection services, string queue)
        where THandler : class, IMessageHandler
    {
        services.AddScoped<THandler>();
        services.AddHostedService(sp => ActivatorUtilities.CreateInstance<RabbitMqConsumer<THandler>>(sp, queue));
        return services;
    }

    /// <summary>Batching metrics reporter that sends measurements to AnalyticsService ("Metrics" section).</summary>
    public static IServiceCollection AddMetricsReporter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetricsOptions>(configuration.GetSection("Metrics"));
        services.AddHttpClient(MetricsReporter.HttpClientName, (sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MetricsOptions>>().Value.AnalyticsUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<MetricsReporter>();
        services.RemoveAll<IMetricsReporter>();
        services.AddSingleton<IMetricsReporter>(sp => sp.GetRequiredService<MetricsReporter>());
        services.AddHostedService(sp => sp.GetRequiredService<MetricsReporter>());
        return services;
    }

    /// <summary>
    /// Runs a startup step (e.g. applying EF Core migrations) and retries while a dependency such as
    /// PostgreSQL is still starting.
    /// </summary>
    public static async Task RetryOnStartupAsync(this IServiceProvider services, string description, Func<IServiceProvider, Task> step)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await step(scope.ServiceProvider);
                logger.LogInformation("{Description}: done", description);
                return;
            }
            catch (Exception ex) when (attempt < 15)
            {
                logger.LogWarning("{Description} failed (attempt {Attempt}): {Message}. Retrying in 2s", description, attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>CORS policy for the React dev server (origins from "Cors:AllowedOrigins"). Credentials are needed by SignalR.</summary>
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
        return services;
    }
}

public static class ApiExceptionHandlingExtensions
{
    /// <summary>Maps NotFound/Conflict/Gone/Forbidden exceptions to ProblemDetails responses (use with app.UseExceptionHandler()).</summary>
    public static IServiceCollection AddApiExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }
}
