namespace NICE.Platform.Collaboration.Infrastructure.Session;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Hosted background service that periodically deletes expired rows from
/// <c>Collaboration.SessionCache</c>.  Runs every 5 minutes; sweep is
/// a single bulk DELETE so it is fast even at high volume.
/// </summary>
public sealed class SessionCacheCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SessionCacheCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SessionCacheCleanupService started (interval: {Interval})", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during SessionCache sweep");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollaborationDbContext>();

        var now = DateTime.UtcNow;
        var deleted = await db.SessionCache
            .Where(r => r.ExpiresAt != null && r.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("SessionCache sweep removed {Count} expired entries", deleted);
    }
}
