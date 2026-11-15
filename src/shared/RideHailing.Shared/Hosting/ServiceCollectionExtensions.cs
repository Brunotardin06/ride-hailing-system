using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RideHailing.Shared.Messaging;

namespace RideHailing.Shared.Hosting;

/// <summary>Small helpers so every Program.cs stays short and identical.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// CORS: the browser blocks calls from http://localhost:5173 (frontend) to http://localhost:5002 (service)
    /// unless the service explicitly allows that origin. Origins come from "Cors:AllowedOrigins" in appsettings.
    /// AllowCredentials is needed later by SignalR.
    /// </summary>
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

    /// <summary>NotFound/Conflict/Forbidden exceptions → ProblemDetails responses (use with app.UseExceptionHandler()).</summary>
    public static IServiceCollection AddApiExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    /// <summary>
    /// Runs a startup step (e.g. applying EF Core migrations) and retries while PostgreSQL is still starting.
    /// </summary>
    public static async Task RetryOnStartupAsync(this IServiceProvider services, string description, Func<IServiceProvider, Task> step)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // DbContext is "scoped" (one per request), so outside a request we create our own scope.
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

    /// <summary>
    /// RabbitMQ: one shared connection + the publisher, configured from the "RabbitMQ" section.
    /// <paramref name="serviceName"/> is the message Source and the connection name in the management UI.
    /// </summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddSingleton(new ServiceIdentity(serviceName));
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMQ"));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        return services;
    }

    /// <summary>Consume <paramref name="queue"/> in the background and pass every message to <typeparamref name="THandler"/>.</summary>
    public static IServiceCollection AddRabbitMqConsumer<THandler>(this IServiceCollection services, string queue)
        where THandler : class, IMessageHandler
    {
        services.AddScoped<THandler>();
        services.AddHostedService(sp => ActivatorUtilities.CreateInstance<RabbitMqConsumer<THandler>>(sp, queue));
        return services;
    }
}
