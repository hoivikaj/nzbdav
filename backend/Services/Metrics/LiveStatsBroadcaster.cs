using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services.Metrics;

/// <summary>
/// Publishes the "live" Overview tile snapshot every five seconds: active read
/// count, articles fetched per minute, error count, and bytes served.
///
/// Bytes-served per minute is computed from a rolling 60 s window of samples
/// of <see cref="ActiveReadRegistry.TotalBytesServed"/> (a monotonic
/// process-lifetime counter), so it correctly reflects throughput of long
/// active read sessions that have not yet been pruned. The controller's
/// initial loader queries <see cref="BytesServedLastMinute"/> for the same
/// number on page load.
/// </summary>
public class LiveStatsBroadcaster(
    ActiveReadRegistry registry,
    WebsocketManager websocketManager
) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);
    private const long WindowMs = 60_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Queue<(long TsMs, long Total)> _byteSamples = new();
    private long _bytesServedLastMinute;
    private string? _lastPayload;

    /// <summary>Bytes served downstream in the last 60 s (rolling window).</summary>
    public long BytesServedLastMinute => Interlocked.Read(ref _bytesServedLastMinute);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
                await BroadcastAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "LiveStatsBroadcaster tick failed");
            }
        }
    }

    private async Task BroadcastAsync()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sinceMs = nowMs - WindowMs;

        await using var db = new MetricsDbContext();
        var articles = await db.SegmentFetches
            .Where(x => x.At >= sinceMs)
            .CountAsync().ConfigureAwait(false);
        var errors = await db.SegmentFetches
            .Where(x => x.At >= sinceMs && x.Status != SegmentFetch.FetchStatus.Ok)
            .CountAsync().ConfigureAwait(false);

        // Roll the bytes-served window forward. First sample after startup
        // yields a delta of 0 (nothing to compare to), which is correct.
        var totalBytes = registry.TotalBytesServed;
        _byteSamples.Enqueue((nowMs, totalBytes));
        while (_byteSamples.Count > 0 && _byteSamples.Peek().TsMs < sinceMs)
            _byteSamples.Dequeue();
        var bytesPerMinute = _byteSamples.Count > 0 ? totalBytes - _byteSamples.Peek().Total : 0;
        Interlocked.Exchange(ref _bytesServedLastMinute, bytesPerMinute);

        var snapshot = new
        {
            activeReads = registry.Count,
            articlesPerMinute = articles,
            errorsPerMinute = errors,
            bytesServedPerMinute = bytesPerMinute,
            ts = nowMs,
        };

        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
        if (PayloadBodyEquals(payload, _lastPayload)) return;
        _lastPayload = payload;
        await websocketManager.SendMessage(WebsocketTopic.LiveStats, payload).ConfigureAwait(false);
    }

    private static bool PayloadBodyEquals(string a, string? b)
    {
        if (b == null) return false;
        var aEnd = a.LastIndexOf(",\"ts\":", StringComparison.Ordinal);
        var bEnd = b.LastIndexOf(",\"ts\":", StringComparison.Ordinal);
        if (aEnd < 0 || bEnd < 0) return a == b;
        return a.AsSpan(0, aEnd).SequenceEqual(b.AsSpan(0, bEnd));
    }
}
