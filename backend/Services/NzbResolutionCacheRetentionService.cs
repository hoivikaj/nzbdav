using Microsoft.Extensions.Hosting;
using NzbWebDAV.Config;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Hydrates the in-memory play-token cache from SQLite on startup, then
/// hourly purges groups older than the configured TTL.
/// </summary>
public class NzbResolutionCacheRetentionService(
    NzbResolutionCache cache, ConfigManager configManager) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    // Hydrate before the server accepts requests so pre-restart tokens
    // never 404 during startup.
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.HydrateAsync(configManager.GetPlayResolutionCacheTtl(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to hydrate play-token cache; starting empty");
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
                await cache.PurgeExpiredAsync(configManager.GetPlayResolutionCacheTtl(), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Play-token retention sweep failed");
            }
        }
    }
}
