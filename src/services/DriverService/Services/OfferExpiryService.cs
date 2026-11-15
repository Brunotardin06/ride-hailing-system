using Microsoft.EntityFrameworkCore;

namespace DriverService.Services;

/// <summary>
/// Background timer: every few seconds, offers nobody answered within Offers:TimeoutSeconds are expired.
/// Without this, a driver who ignores an offer would stay "Offered" forever and the ride would never be matched.
/// </summary>
public class OfferExpiryService(IServiceScopeFactory scopeFactory, ILogger<OfferExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // BackgroundService is a singleton, OfferService/DbContext are scoped → create a scope per round.
                await using var scope = scopeFactory.CreateAsyncScope();
                var offers = scope.ServiceProvider.GetRequiredService<OfferService>();
                var expired = await offers.ExpireOverdueOffersAsync(stoppingToken);
                if (expired > 0) logger.LogInformation("Expired {Count} offer(s)", expired);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The driver answered at the same moment → their answer wins; try again next round.
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Offer expiry failed: {Message}", ex.Message);
            }
        }
    }
}
