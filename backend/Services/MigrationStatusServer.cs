using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// A minimal HTTP server that runs for the duration of the blocking
/// <c>--db-migration</c> phase. It binds the same port the real backend will
/// later use (via <c>ASPNETCORE_URLS</c>) and serves migration progress at
/// <c>/api/migration-status</c> so the frontend can render a live status page.
/// Every other route (including <c>/health</c>) returns 503 so the entrypoint
/// health poll and frontend loaders keep retrying until the real backend is up.
/// </summary>
public sealed class MigrationStatusServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private MigrationStatusServer(WebApplication app) => _app = app;

    public static async Task<MigrationStatusServer?> StartAsync(MigrationProgress progress, CancellationToken ct)
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            // Keep this process quiet; migration progress is logged via Serilog.
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.None);

            var app = builder.Build();

            app.MapGet("/api/migration-status", (HttpContext context) =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(progress.ToJson());
            });

            app.MapFallback((HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"status\":\"migrating\"}");
            });

            await app.StartAsync(ct).ConfigureAwait(false);
            return new MigrationStatusServer(app);
        }
        catch (Exception ex)
        {
            // A missing status page must never block the actual migration.
            Log.Warning(ex, "Could not start migration status server; migration progress UI is unavailable");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _app.StopAsync(stopCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping migration status server");
        }

        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
