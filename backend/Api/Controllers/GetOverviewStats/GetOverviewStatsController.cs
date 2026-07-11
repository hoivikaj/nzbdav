using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Api.Controllers.GetOverviewStats;

[ApiController]
[Route("api/get-overview-stats")]
public class GetOverviewStatsController(
    DavDatabaseClient davDb,
    ActiveReadRegistry registry,
    LiveStatsBroadcaster liveStats,
    ConfigManager configManager,
    IndexerHitTracker hitTracker
) : BaseApiController
{
    private const long OneMinute = 60_000;
    private const long OneHour = 60 * OneMinute;
    private const long OneDay = 24 * OneHour;

    // Log-scale latency buckets in milliseconds. Last bucket is a catch-all up to int.MaxValue.
    private static readonly int[] LatencyBucketEdges =
    {
        0, 10, 25, 50, 100, 200, 400, 800, 1500, 3000, 6000, 12000, 30000, int.MaxValue
    };

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetOverviewStatsRequest(HttpContext);
        var response = await BuildAsync(request).ConfigureAwait(false);
        return Ok(response);
    }

    private async Task<GetOverviewStatsResponse> BuildAsync(GetOverviewStatsRequest request)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var window = request.Window;
        var (windowMs, bucketSize, label) = ResolveWindow(window, nowMs);
        var windowStart = window == GetOverviewStatsRequest.OverviewWindow.AllTime
            ? 0
            : nowMs - windowMs;

        var nicknamesByHost = configManager.GetUsenetProviderConfig().Providers
            .Where(p => !string.IsNullOrWhiteSpace(p.Nickname))
            .GroupBy(p => p.Host, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Nickname, StringComparer.OrdinalIgnoreCase);

        await using var metrics = new MetricsDbContext();
        var useRollups =
            window == GetOverviewStatsRequest.OverviewWindow.Last7Days ||
            window == GetOverviewStatsRequest.OverviewWindow.Last30Days ||
            window == GetOverviewStatsRequest.OverviewWindow.AllTime;

        // Sessions live up to 90 d, so they work fine for every window. We keep using
        // the raw ReadSessions table for sessions stats regardless of `useRollups`.
        var sessions = await metrics.ReadSessions
            .Where(x => x.EndedAt >= windowStart)
            .Select(x => new { x.StartedAt, x.EndedAt, x.DurationMs, x.BytesServed, x.FailoverSaves })
            .ToListAsync().ConfigureAwait(false);

        GetOverviewStatsResponse.LiveTiles liveTiles;
        List<GetOverviewStatsResponse.ThroughputPoint> throughput;
        List<GetOverviewStatsResponse.ProviderRow> providers;
        GetOverviewStatsResponse.LatencyBlock latency;
        List<GetOverviewStatsResponse.ErrorSlice> errors;
        long totalArticles, totalErrors, totalBytesFetched;
        GetOverviewStatsResponse.FailoverBlock failover;

        var readsSaved = sessions.LongCount(s => s.FailoverSaves > 0);
        var failoverBucket = ResolveFailoverBucket(window);

        long? previousSaves = null;
        if (window != GetOverviewStatsRequest.OverviewWindow.AllTime)
        {
            var prevStart = windowStart - windowMs;
            previousSaves = await metrics.ProviderHourly
                .Where(h => h.Hour >= prevStart && h.Hour < windowStart)
                .SumAsync(h => (long?)h.FailoverSaves).ConfigureAwait(false) ?? 0L;
        }

        var heatmap = await BuildHeatmapAsync(metrics, window, nowMs).ConfigureAwait(false);

        if (useRollups)
        {
            var hours = await metrics.ProviderHourly
                .Where(h => h.Hour >= windowStart)
                .Select(h => new { h.Hour, h.Provider, h.Articles, h.BytesFetched, h.Errors, h.Retries, h.FailoverSaves, h.SumDurationMs })
                .ToListAsync().ConfigureAwait(false);

            liveTiles = BuildLiveTiles(articlesLastMinute: 0, errorsLastMinute: 0);
            throughput = BuildThroughputFromHourly(hours.Select(h => (h.Hour, h.Articles, h.Errors, h.BytesFetched)), sessions.Select(s => (s.EndedAt, s.BytesServed)), bucketSize);
            providers = BuildProvidersFromHourly(hours, windowStart, bucketSize, nowMs, nicknamesByHost);
            latency = new GetOverviewStatsResponse.LatencyBlock();
            errors = new List<GetOverviewStatsResponse.ErrorSlice>();
            totalArticles = hours.Sum(h => h.Articles);
            totalErrors = hours.Sum(h => h.Errors);
            totalBytesFetched = hours.Sum(h => h.BytesFetched);
            var failoverEdges = await metrics.FailoverHourly
                .Where(f => f.Hour >= windowStart)
                .Select(f => new { f.FromProvider, f.Reason, f.Count })
                .ToListAsync().ConfigureAwait(false);
            failover = BuildFailover(
                hours.Where(h => h.FailoverSaves > 0).Select(h => (h.Hour, h.Provider, h.FailoverSaves)),
                failoverEdges.Select(e => (e.FromProvider, e.Reason, e.Count)),
                totalArticles, sessions.Count, readsSaved, previousSaves, failoverBucket, nicknamesByHost);
        }
        else
        {
            // Short windows: raw fetches give us per-event detail (heatmap, latency, errors).
            var fetches = await metrics.SegmentFetches
                .Where(x => x.At >= windowStart)
                .Select(x => new { x.At, x.Provider, x.Status, x.DurationMs, x.Retries })
                .ToListAsync().ConfigureAwait(false);

            var perMinuteBytes = await metrics.ProviderMinutes
                .Where(p => p.Minute >= windowStart)
                .GroupBy(p => p.Provider)
                .Select(g => new { Provider = g.Key, Bytes = g.Sum(x => x.BytesFetched) })
                .ToDictionaryAsync(x => x.Provider, x => x.Bytes).ConfigureAwait(false);

            var sinceMinute = nowMs - OneMinute;
            var articlesLastMinute = fetches.Count(f => f.At >= sinceMinute);
            var errorsLastMinute = fetches.Count(f => f.At >= sinceMinute && f.Status != SegmentFetch.FetchStatus.Ok);

            liveTiles = BuildLiveTiles(articlesLastMinute, errorsLastMinute);
            throughput = BuildThroughput(fetches.Select(f => (f.At, f.Status)), sessions.Select(s => (s.EndedAt, s.BytesServed)), bucketSize);
            providers = BuildProviders(fetches, perMinuteBytes, windowStart, bucketSize, window, nicknamesByHost);
            latency = BuildLatency(fetches.Where(f => f.Status == SegmentFetch.FetchStatus.Ok).Select(f => f.DurationMs));
            errors = BuildErrors(fetches.Select(f => f.Status));
            totalArticles = throughput.Sum(p => p.Articles);
            totalErrors = throughput.Sum(p => p.Errors);
            totalBytesFetched = perMinuteBytes.Values.Sum();
            var failoverEdges = await metrics.FailoverMisses
                .Where(f => f.At >= windowStart)
                .Select(f => new { f.FromProvider, f.Reason })
                .ToListAsync().ConfigureAwait(false);
            failover = BuildFailover(
                fetches.Where(f => f.Status == SegmentFetch.FetchStatus.Ok && f.Retries > 0)
                    .Select(f => (f.At, f.Provider, 1L)),
                failoverEdges.Select(e => (e.FromProvider, e.Reason, 1L)),
                totalArticles, sessions.Count, readsSaved, previousSaves, failoverBucket, nicknamesByHost);
        }

        var catalogue = await BuildCatalogueAsync().ConfigureAwait(false);
        var sessionsBlock = BuildSessionsBlock(sessions.Select(s => (s.DurationMs, s.BytesServed)));
        var indexers = await BuildIndexersAsync().ConfigureAwait(false);
        var indexerApiUsage = await BuildIndexerApiUsageAsync().ConfigureAwait(false);
        var lifetime = await BuildLifetimeAsync(metrics).ConfigureAwait(false);
        var records = await BuildRecordsAsync(metrics).ConfigureAwait(false);

        return new GetOverviewStatsResponse
        {
            Window = label,
            Tiles = liveTiles,
            Throughput = throughput,
            TotalArticles = totalArticles,
            TotalErrors = totalErrors,
            TotalBytesFetched = totalBytesFetched,
            Providers = providers,
            Catalogue = catalogue,
            Sessions = sessionsBlock,
            Heatmap = heatmap,
            Latency = latency,
            Errors = errors,
            Indexers = indexers,
            IndexerApiUsage = indexerApiUsage,
            Lifetime = lifetime,
            Records = records,
            Failover = failover,
        };
    }

    private async Task<List<GetOverviewStatsResponse.IndexerApiUsageRow>> BuildIndexerApiUsageAsync()
    {
        var configured = configManager.GetIndexerConfig().Indexers
            .Where(x => x.Enabled)
            .Select(x => (x.Name, x.HitLimit, x.DownloadLimit, ResetHourUtc: x.HitLimitResetTime))
            .ToList();
        if (configured.Count == 0) return new List<GetOverviewStatsResponse.IndexerApiUsageRow>();

        var snapshots = await hitTracker.GetUsageAsync(configured, HttpContext.RequestAborted).ConfigureAwait(false);
        var resetHourByName = configured.ToDictionary(x => x.Name, x => x.ResetHourUtc);

        return snapshots
            .Select(s => new GetOverviewStatsResponse.IndexerApiUsageRow
            {
                Name = s.IndexerName,
                ApiHits = s.ApiHits,
                ApiHitLimit = s.ApiHitLimit,
                DownloadHits = s.DownloadHits,
                DownloadHitLimit = s.DownloadHitLimit,
                ResetAtMs = s.ResetAt.ToUnixTimeMilliseconds(),
                ResetHourUtc = resetHourByName.GetValueOrDefault(s.IndexerName),
            })
            // Surface indexers with the most usage first, then by name for stability.
            .OrderByDescending(r => r.ApiHits + r.DownloadHits)
            .ThenBy(r => r.Name)
            .ToList();
    }

    private static (long WindowMs, long BucketSize, string Label) ResolveWindow(
        GetOverviewStatsRequest.OverviewWindow window, long nowMs) => window switch
    {
        GetOverviewStatsRequest.OverviewWindow.Last24Hours => (OneDay, OneMinute, "24h"),
        GetOverviewStatsRequest.OverviewWindow.Last7Days => (7 * OneDay, OneHour, "7d"),
        GetOverviewStatsRequest.OverviewWindow.Last30Days => (30 * OneDay, OneHour, "30d"),
        GetOverviewStatsRequest.OverviewWindow.AllTime => (nowMs, OneDay, "all"),
        _ => (OneDay, OneMinute, "24h"),
    };

    private static long ResolveFailoverBucket(GetOverviewStatsRequest.OverviewWindow window) => window switch
    {
        GetOverviewStatsRequest.OverviewWindow.Last24Hours => OneHour,
        GetOverviewStatsRequest.OverviewWindow.Last7Days => OneDay,
        GetOverviewStatsRequest.OverviewWindow.Last30Days => OneDay,
        GetOverviewStatsRequest.OverviewWindow.AllTime => 7 * OneDay,
        _ => OneHour,
    };

    private static GetOverviewStatsResponse.FailoverBlock BuildFailover(
        IEnumerable<(long At, string Provider, long Saves)> rescues,
        IEnumerable<(string From, SegmentFetch.FetchStatus Reason, long Count)> misses,
        long totalArticles,
        long readSessions,
        long readsSaved,
        long? previousSaves,
        long chartBucketSize,
        IReadOnlyDictionary<string, string?> nicknamesByHost)
    {
        var totalsByProvider = new Dictionary<string, long>();
        var byBucket = new SortedDictionary<long, Dictionary<string, long>>();
        foreach (var (at, provider, saves) in rescues)
        {
            if (saves <= 0) continue;
            totalsByProvider.TryGetValue(provider, out var t);
            totalsByProvider[provider] = t + saves;

            var bucket = at - (at % chartBucketSize);
            if (!byBucket.TryGetValue(bucket, out var perProvider))
                byBucket[bucket] = perProvider = new Dictionary<string, long>();
            perProvider.TryGetValue(provider, out var c);
            perProvider[provider] = c + saves;
        }

        var orderedProviders = totalsByProvider
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();
        var indexOf = orderedProviders
            .Select((p, i) => (p, i))
            .ToDictionary(x => x.p, x => x.i);

        var missesByProvider = new Dictionary<string, long>();
        var missesByReason = new Dictionary<SegmentFetch.FetchStatus, long>();
        long segmentsCovered = 0;
        foreach (var (from, reason, count) in misses)
        {
            if (count <= 0) continue;
            segmentsCovered += count;
            missesByProvider.TryGetValue(from, out var m);
            missesByProvider[from] = m + count;
            missesByReason.TryGetValue(reason, out var r);
            missesByReason[reason] = r + count;
        }

        return new GetOverviewStatsResponse.FailoverBlock
        {
            ArticlesRecovered = totalsByProvider.Values.Sum(),
            PreviousArticlesRecovered = previousSaves,
            SegmentsCovered = segmentsCovered,
            ReadsSaved = readsSaved,
            ReadSessions = readSessions,
            TotalArticles = totalArticles,
            BucketSizeMs = chartBucketSize,
            RescuedBy = orderedProviders
                .Select(p => new GetOverviewStatsResponse.FailoverProvider
                {
                    Provider = p,
                    Nickname = nicknamesByHost.GetValueOrDefault(p),
                    Saves = totalsByProvider[p],
                })
                .ToList(),
            RescuedFrom = missesByProvider
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new GetOverviewStatsResponse.FailoverFrom
                {
                    Provider = kv.Key,
                    Nickname = nicknamesByHost.GetValueOrDefault(kv.Key),
                    Misses = kv.Value,
                })
                .ToList(),
            Reasons = missesByReason
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new GetOverviewStatsResponse.FailoverReason
                {
                    Status = kv.Key.ToString(),
                    Count = kv.Value,
                })
                .ToList(),
            Buckets = byBucket
                .Select(kv =>
                {
                    var counts = new long[orderedProviders.Count];
                    foreach (var (provider, c) in kv.Value)
                        counts[indexOf[provider]] = c;
                    return new GetOverviewStatsResponse.FailoverBucket
                    {
                        Bucket = kv.Key,
                        Counts = counts.ToList(),
                    };
                })
                .ToList(),
        };
    }

    private GetOverviewStatsResponse.LiveTiles BuildLiveTiles(long articlesLastMinute, long errorsLastMinute)
    {
        return new GetOverviewStatsResponse.LiveTiles
        {
            ActiveReads = registry.Count,
            ArticlesPerMinute = articlesLastMinute,
            ErrorsPerMinute = errorsLastMinute,
            BytesServedPerMinute = liveStats.BytesServedLastMinute,
        };
    }

    private static List<GetOverviewStatsResponse.ThroughputPoint> BuildThroughput(
        IEnumerable<(long At, SegmentFetch.FetchStatus Status)> fetches,
        IEnumerable<(long EndedAt, long BytesServed)> sessions,
        long bucketSize)
    {
        var byBucket = new Dictionary<long, (long Articles, long Errors, long BytesServed)>();
        foreach (var (at, status) in fetches)
        {
            var b = at - (at % bucketSize);
            byBucket.TryGetValue(b, out var cur);
            byBucket[b] = (cur.Articles + 1, cur.Errors + (status != SegmentFetch.FetchStatus.Ok ? 1 : 0), cur.BytesServed);
        }
        foreach (var (endedAt, bytes) in sessions)
        {
            var b = endedAt - (endedAt % bucketSize);
            byBucket.TryGetValue(b, out var cur);
            byBucket[b] = (cur.Articles, cur.Errors, cur.BytesServed + bytes);
        }

        return byBucket
            .OrderBy(kv => kv.Key)
            .Select(kv => new GetOverviewStatsResponse.ThroughputPoint
            {
                Bucket = kv.Key,
                Articles = kv.Value.Articles,
                Errors = kv.Value.Errors,
                BytesServed = kv.Value.BytesServed,
            })
            .ToList();
    }

    private static List<GetOverviewStatsResponse.ThroughputPoint> BuildThroughputFromHourly(
        IEnumerable<(long Hour, long Articles, long Errors, long BytesFetched)> hours,
        IEnumerable<(long EndedAt, long BytesServed)> sessions,
        long bucketSize)
    {
        var byBucket = new Dictionary<long, (long Articles, long Errors, long BytesServed, long BytesFetched)>();
        foreach (var h in hours)
        {
            var b = h.Hour - (h.Hour % bucketSize);
            byBucket.TryGetValue(b, out var cur);
            byBucket[b] = (cur.Articles + h.Articles, cur.Errors + h.Errors, cur.BytesServed, cur.BytesFetched + h.BytesFetched);
        }
        foreach (var (endedAt, bytes) in sessions)
        {
            var b = endedAt - (endedAt % bucketSize);
            byBucket.TryGetValue(b, out var cur);
            byBucket[b] = (cur.Articles, cur.Errors, cur.BytesServed + bytes, cur.BytesFetched);
        }

        return byBucket
            .OrderBy(kv => kv.Key)
            .Select(kv => new GetOverviewStatsResponse.ThroughputPoint
            {
                Bucket = kv.Key,
                Articles = kv.Value.Articles,
                Errors = kv.Value.Errors,
                BytesServed = kv.Value.BytesServed,
            })
            .ToList();
    }

    private static List<GetOverviewStatsResponse.ProviderRow> BuildProviders<T>(
        List<T> fetches,
        IReadOnlyDictionary<string, long> bytesByProvider,
        long windowStart,
        long bucketSize,
        GetOverviewStatsRequest.OverviewWindow window,
        IReadOnlyDictionary<string, string?> nicknamesByHost) where T : class
    {
        var is7d = window == GetOverviewStatsRequest.OverviewWindow.Last7Days;
        var sparkBuckets = is7d ? 168 : 24;
        var sparkSize = OneHour;
        var sparkStart = windowStart - (windowStart % sparkSize);

        var byProvider = new Dictionary<string, ProviderAccumulator>();
        foreach (var f in fetches.Cast<dynamic>())
        {
            string host = f.Provider;
            if (!byProvider.TryGetValue(host, out var acc))
                acc = new ProviderAccumulator(sparkBuckets);
            acc.Articles++;
            acc.SumDurationMs += f.DurationMs;
            if (f.Status != SegmentFetch.FetchStatus.Ok) acc.Errors++;
            acc.Retries += f.Retries;
            var idx = (int)(((long)f.At - sparkStart) / sparkSize);
            if (idx >= 0 && idx < sparkBuckets) acc.Spark[idx]++;
            byProvider[host] = acc;
        }

        return byProvider
            .Select(kv => new GetOverviewStatsResponse.ProviderRow
            {
                Provider = kv.Key,
                Nickname = nicknamesByHost.GetValueOrDefault(kv.Key),
                Articles = kv.Value.Articles,
                BytesFetched = bytesByProvider.GetValueOrDefault(kv.Key, 0L),
                Errors = kv.Value.Errors,
                Retries = kv.Value.Retries,
                AvgDurationMs = kv.Value.Articles > 0 ? (double)kv.Value.SumDurationMs / kv.Value.Articles : 0,
                ErrorRate = kv.Value.Articles > 0 ? (double)kv.Value.Errors / kv.Value.Articles : 0,
                Spark = kv.Value.Spark.ToList(),
            })
            .OrderByDescending(r => r.Articles)
            .ToList();
    }

    private static List<GetOverviewStatsResponse.ProviderRow> BuildProvidersFromHourly(
        IEnumerable<dynamic> hours,
        long windowStart,
        long bucketSize,
        long nowMs,
        IReadOnlyDictionary<string, string?> nicknamesByHost)
    {
        // Spark for 30d/all-time rolls up to daily.
        var totalSpan = nowMs - windowStart;
        var sparkSize = OneDay;
        var sparkBuckets = Math.Max(1, (int)Math.Min(60, totalSpan / sparkSize + 1));
        var sparkStart = windowStart - (windowStart % sparkSize);

        var byProvider = new Dictionary<string, ProviderAccumulator>();
        foreach (var h in hours)
        {
            string host = h.Provider;
            if (!byProvider.TryGetValue(host, out var acc))
                acc = new ProviderAccumulator(sparkBuckets);
            acc.Articles += (long)h.Articles;
            acc.Errors += (long)h.Errors;
            acc.Retries += (long)h.Retries;
            acc.SumDurationMs += (long)h.SumDurationMs;
            acc.Bytes += (long)h.BytesFetched;
            var idx = (int)(((long)h.Hour - sparkStart) / sparkSize);
            if (idx >= 0 && idx < sparkBuckets) acc.Spark[idx] += (long)h.Articles;
            byProvider[host] = acc;
        }

        return byProvider
            .Select(kv => new GetOverviewStatsResponse.ProviderRow
            {
                Provider = kv.Key,
                Nickname = nicknamesByHost.GetValueOrDefault(kv.Key),
                Articles = kv.Value.Articles,
                BytesFetched = kv.Value.Bytes,
                Errors = kv.Value.Errors,
                Retries = kv.Value.Retries,
                AvgDurationMs = kv.Value.Articles > 0 ? (double)kv.Value.SumDurationMs / kv.Value.Articles : 0,
                ErrorRate = kv.Value.Articles > 0 ? (double)kv.Value.Errors / kv.Value.Articles : 0,
                Spark = kv.Value.Spark.ToList(),
            })
            .OrderByDescending(r => r.Articles)
            .ToList();
    }

    private sealed class ProviderAccumulator
    {
        public long Articles, Errors, Retries, SumDurationMs, Bytes;
        public readonly long[] Spark;
        public ProviderAccumulator(int n) { Spark = new long[n]; }
    }

    private static async Task<GetOverviewStatsResponse.HeatmapBlock> BuildHeatmapAsync(
        MetricsDbContext metrics,
        GetOverviewStatsRequest.OverviewWindow window,
        long nowMs)
    {
        var (mode, bucketSize, windowStart, windowEnd) = ResolveHeatmapWindow(window, nowMs);

        var hourly = await metrics.ProviderHourly
            .Where(h => h.Hour >= windowStart)
            .GroupBy(h => h.Hour)
            .Select(g => new { Hour = g.Key, Articles = g.Sum(x => x.Articles) })
            .ToListAsync().ConfigureAwait(false);

        var byBucket = new Dictionary<long, long>();
        long max = 0;
        foreach (var h in hourly)
        {
            var bucket = h.Hour - (h.Hour % bucketSize);
            byBucket.TryGetValue(bucket, out var c);
            c += h.Articles;
            byBucket[bucket] = c;
            if (c > max) max = c;
        }

        return new GetOverviewStatsResponse.HeatmapBlock
        {
            MaxCell = max,
            Mode = mode,
            WindowStartMs = windowStart,
            WindowEndMs = windowEnd,
            BucketSizeMs = bucketSize,
            Cells = byBucket
                .Select(kv => new GetOverviewStatsResponse.HeatmapCell
                {
                    Bucket = kv.Key,
                    Count = kv.Value,
                })
                .OrderBy(c => c.Bucket)
                .ToList(),
        };
    }

    private static (string Mode, long BucketSize, long WindowStart, long WindowEnd) ResolveHeatmapWindow(
        GetOverviewStatsRequest.OverviewWindow window, long nowMs)
    {
        var hourEnd = nowMs - (nowMs % OneHour);
        var dayEnd = nowMs - (nowMs % OneDay);

        return window switch
        {
            GetOverviewStatsRequest.OverviewWindow.Last24Hours
                => ("day", OneHour, hourEnd - 23 * OneHour, hourEnd),

            GetOverviewStatsRequest.OverviewWindow.Last7Days
                => ("week", OneHour, dayEnd - 6 * OneDay, hourEnd),

            GetOverviewStatsRequest.OverviewWindow.Last30Days
                => ("month", OneHour, dayEnd - 29 * OneDay, hourEnd),

            GetOverviewStatsRequest.OverviewWindow.AllTime
                => ("year", OneDay, AlignYearStart(dayEnd), dayEnd),

            _ => ("week", OneHour, dayEnd - 6 * OneDay, hourEnd),
        };
    }

    private static long AlignYearStart(long todayDayStart)
    {
        var todayDow = ((int)DateTimeOffset.FromUnixTimeMilliseconds(todayDayStart).UtcDateTime.DayOfWeek + 6) % 7;
        var thisWeekMonday = todayDayStart - todayDow * OneDay;
        return thisWeekMonday - 52 * 7 * OneDay;
    }

    private static GetOverviewStatsResponse.LatencyBlock BuildLatency(IEnumerable<int> okDurationsMs)
    {
        var samples = okDurationsMs.ToList();
        if (samples.Count == 0) return new GetOverviewStatsResponse.LatencyBlock();

        samples.Sort();
        int Pct(double p)
        {
            var idx = (int)Math.Ceiling(p * samples.Count) - 1;
            return samples[Math.Clamp(idx, 0, samples.Count - 1)];
        }

        var buckets = new List<GetOverviewStatsResponse.LatencyBucket>();
        for (var i = 0; i < LatencyBucketEdges.Length - 1; i++)
        {
            var lo = LatencyBucketEdges[i];
            var hi = LatencyBucketEdges[i + 1];
            var count = samples.Count(d => d >= lo && d < hi);
            if (count == 0 && lo > 0) continue;
            buckets.Add(new GetOverviewStatsResponse.LatencyBucket { LoMs = lo, HiMs = hi, Count = count });
        }

        return new GetOverviewStatsResponse.LatencyBlock
        {
            P50Ms = Pct(0.50),
            P95Ms = Pct(0.95),
            P99Ms = Pct(0.99),
            Samples = samples.Count,
            Buckets = buckets,
        };
    }

    private static List<GetOverviewStatsResponse.ErrorSlice> BuildErrors(IEnumerable<SegmentFetch.FetchStatus> statuses)
    {
        var counts = new Dictionary<SegmentFetch.FetchStatus, long>();
        foreach (var s in statuses)
        {
            if (s == SegmentFetch.FetchStatus.Ok) continue;
            counts.TryGetValue(s, out var c);
            counts[s] = c + 1;
        }

        return counts
            .Select(kv => new GetOverviewStatsResponse.ErrorSlice
            {
                Status = kv.Key.ToString(),
                Count = kv.Value,
            })
            .OrderByDescending(s => s.Count)
            .ToList();
    }

    private async Task<GetOverviewStatsResponse.CatalogueBlock> BuildCatalogueAsync()
    {
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

        var files = davDb.Ctx.Items.Where(i => i.Type == DavItem.ItemType.UsenetFile);
        var fileCount = await files.CountAsync().ConfigureAwait(false);
        var totalBytes = await files.SumAsync(i => (long?)i.FileSize).ConfigureAwait(false) ?? 0L;
        var largest = await files.MaxAsync(i => (long?)i.FileSize).ConfigureAwait(false) ?? 0L;
        var addedRecently = await files
            .Where(i => i.CreatedAt >= sevenDaysAgo)
            .CountAsync().ConfigureAwait(false);

        return new GetOverviewStatsResponse.CatalogueBlock
        {
            FileCount = fileCount,
            TotalBytes = totalBytes,
            LargestFileBytes = largest,
            AddedLast7Days = addedRecently,
        };
    }

    private static GetOverviewStatsResponse.SessionsBlock BuildSessionsBlock(
        IEnumerable<(int DurationMs, long BytesServed)> sessions)
    {
        var list = sessions.ToList();
        if (list.Count == 0) return new GetOverviewStatsResponse.SessionsBlock();

        return new GetOverviewStatsResponse.SessionsBlock
        {
            Count = list.Count,
            TotalBytesServed = list.Sum(x => x.BytesServed),
            AvgDurationMs = (long)list.Average(x => (double)x.DurationMs),
            LongestDurationMs = list.Max(x => x.DurationMs),
            BiggestReadBytes = list.Max(x => x.BytesServed),
        };
    }

    private async Task<List<GetOverviewStatsResponse.IndexerRow>> BuildIndexersAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var rows = await davDb.Ctx.HistoryItems
            .Where(h => h.CreatedAt >= cutoff && h.IndexerName != null)
            .GroupBy(h => h.IndexerName!)
            .Select(g => new
            {
                Name = g.Key,
                Completed = (long)g.Count(x => x.DownloadStatus == HistoryItem.DownloadStatusOption.Completed),
                Failed = (long)g.Count(x => x.DownloadStatus == HistoryItem.DownloadStatusOption.Failed),
                BytesCompleted = g
                    .Where(x => x.DownloadStatus == HistoryItem.DownloadStatusOption.Completed)
                    .Sum(x => (long?)x.TotalSegmentBytes) ?? 0L,
                AvgSecondsRaw = g
                    .Where(x => x.DownloadStatus == HistoryItem.DownloadStatusOption.Completed)
                    .Average(x => (double?)x.DownloadTimeSeconds),
            })
            .ToListAsync().ConfigureAwait(false);

        return rows
            .Select(r => new GetOverviewStatsResponse.IndexerRow
            {
                Name = r.Name,
                Completed = r.Completed,
                Failed = r.Failed,
                BytesCompleted = r.BytesCompleted,
                AvgSeconds = (int)(r.AvgSecondsRaw ?? 0),
                SuccessRate = r.Completed + r.Failed > 0 ? (double)r.Completed / (r.Completed + r.Failed) : 0,
            })
            .OrderByDescending(r => r.Completed + r.Failed)
            .ToList();
    }

    private static async Task<GetOverviewStatsResponse.LifetimeBlock> BuildLifetimeAsync(MetricsDbContext metrics)
    {
        // ProviderHourly is the long-retention truth for fetched bytes & articles (365 d).
        // ReadSessions retains 90 d, so "read" lifetime is approximate beyond that window.
        var bytesFetched = await metrics.ProviderHourly
            .SumAsync(x => (long?)x.BytesFetched).ConfigureAwait(false) ?? 0L;
        var articles = await metrics.ProviderHourly
            .SumAsync(x => (long?)x.Articles).ConfigureAwait(false) ?? 0L;
        var firstHour = await metrics.ProviderHourly
            .OrderBy(x => x.Hour)
            .Select(x => (long?)x.Hour)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        var sessionCount = await metrics.ReadSessions.CountAsync().ConfigureAwait(false);
        var bytesRead = await metrics.ReadSessions
            .SumAsync(x => (long?)x.BytesServed).ConfigureAwait(false) ?? 0L;
        var readMs = await metrics.ReadSessions
            .SumAsync(x => (long?)x.DurationMs).ConfigureAwait(false) ?? 0L;

        return new GetOverviewStatsResponse.LifetimeBlock
        {
            BytesFetched = bytesFetched,
            BytesRead = bytesRead,
            Articles = articles,
            ReadSessions = sessionCount,
            ReadSeconds = readMs / 1000,
            FirstSeenAt = firstHour,
        };
    }

    private static async Task<GetOverviewStatsResponse.RecordsBlock> BuildRecordsAsync(MetricsDbContext metrics)
    {
        // Busiest day = sum bytes-fetched per UTC day across the entire hourly history.
        // SQLite returns Hour as ms; integer-divide by OneDay to bucket by day.
        var dayRow = await metrics.ProviderHourly
            .GroupBy(x => x.Hour / OneDay)
            .Select(g => new { DayBucket = g.Key, Bytes = g.Sum(x => x.BytesFetched) })
            .OrderByDescending(x => x.Bytes)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        var hourRow = await metrics.ProviderHourly
            .GroupBy(x => x.Hour)
            .Select(g => new { Hour = g.Key, Bytes = g.Sum(x => x.BytesFetched) })
            .OrderByDescending(x => x.Bytes)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        return new GetOverviewStatsResponse.RecordsBlock
        {
            BestDayBytes = dayRow?.Bytes ?? 0,
            BestDayAt = dayRow != null ? dayRow.DayBucket * OneDay : null,
            BestHourBytes = hourRow?.Bytes ?? 0,
            BestHourAt = hourRow?.Hour,
        };
    }
}
