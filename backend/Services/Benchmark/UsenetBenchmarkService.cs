using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services.Benchmark;

/// <summary>
/// Measures real download speed and latency against a single provider and
/// recommends the smallest connection count that nearly maxes out throughput
/// (the diminishing-returns "knee"), plus whether NNTP pipelining helps and at
/// what depth.
///
/// Safety model — the test never disrupts normal operation or usage accounting:
///   • It opens its own ad-hoc connections via <see cref="UsenetStreamingClient.CreateNewConnection"/>,
///     bypassing the shared connection pool, byte tracker and metrics writer.
///   • It probes a few steps above the configured max but stops the instant the
///     provider refuses another connection (the classic 502 "too many connections"),
///     treating that as the real ceiling.
///   • Every level is byte- and time-bounded, and the whole run honours the
///     caller's cancellation token (closing the modal aborts it cleanly).
/// </summary>
public sealed class UsenetBenchmarkService(WebsocketManager websocketManager, BenchmarkCorpusProvider corpus)
{
    // Rough average decoded article size; used to scale how much data each level
    // transfers so even high connection counts move enough bytes to measure.
    private const long ArticleBytesEstimate = 750_000;

    // Never open more than this many sockets at once, regardless of provider/config.
    private const int HardConnectionCeiling = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<BenchmarkResult> RunAsync(
        UsenetProviderConfig.ConnectionDetails provider,
        int configuredMaxConnections,
        BenchmarkIntensity intensity,
        bool pipeliningOnly,
        CancellationToken ct)
    {
        var profile = BenchmarkProfile.For(intensity);
        var result = new BenchmarkResult { PipeliningOnly = pipeliningOnly };
        var cursor = new int[1]; // shared round-robin index into the segment pool

        // 1) Latency — also doubles as a connectivity/credentials check.
        Report("latency", "Measuring latency…", 5, result, null);
        result.Latency = await MeasureLatencyAsync(provider, profile.LatencySamples, ct).ConfigureAwait(false);

        // 2) Corpus — real message-ids to download.
        Report("corpus", "Gathering test articles…", 12, result, null);
        var pool = await corpus.GetSegmentPoolAsync(profile.MaxCorpusSegments, ct).ConfigureAwait(false);
        if (pool.Count == 0)
        {
            result.ThroughputTested = false;
            result.Warnings.Add(
                "No downloaded articles were available to measure speed, so only latency was tested. " +
                "Download something first, then re-run to get a connection recommendation.");
            Report("done", "Done — latency only.", 100, result, null);
            return result;
        }

        result.ThroughputTested = true;
        if (pool.Count < 200)
            result.Warnings.Add("Only a small pool of test articles was available, so speed numbers may be a little noisy.");

        // Pipelining-only mode: leave the connection count alone and just find
        // the best pipelining depth at the count the user already runs.
        if (pipeliningOnly)
        {
            var conns = Math.Clamp(configuredMaxConnections, 1, HardConnectionCeiling);
            Report("pipelining", $"Testing pipelining at {conns} connection{(conns == 1 ? "" : "s")}…", 30, result, conns);
            result.Pipelining = await MeasurePipeliningAsync(
                provider, conns, pool, cursor, profile, FocusedPipelineDepths(intensity),
                bytes => result.DataUsedBytes += bytes, ct).ConfigureAwait(false);

            if (conns >= 24)
                result.Warnings.Add(
                    "At high connection counts, pipelining usually adds little — running many connections in " +
                    "parallel already hides most of the per-request latency it would otherwise save.");

            Report("done", "Done.", 100, result, null);
            return result;
        }

        // 3) Throughput sweep — climb connection counts until the knee or the cap.
        var levels = BuildLevels(configuredMaxConnections, profile);
        int? providerCap = null;
        for (var i = 0; i < levels.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var level = levels[i];

            if (result.DataUsedBytes >= profile.HardTotalBytes)
            {
                result.Warnings.Add("Reached the data budget before testing every connection level.");
                break;
            }

            Report("sweep", $"Testing {level} connection{(level == 1 ? "" : "s")}…",
                ProgressPercent(15, 75, i, levels.Count), result, level);

            var sample = await MeasureThroughputAsync(
                provider, level, pool, cursor, TargetBytes(level, profile),
                profile.PerLevelMaxDuration, pipeliningDepth: 0, ct).ConfigureAwait(false);
            result.DataUsedBytes += sample.Bytes;

            if (sample.OpenedConnections == 0)
            {
                // Couldn't open a single socket at this level — treat the prior
                // level as the ceiling and stop climbing.
                providerCap ??= Math.Max(1, level - 1);
                break;
            }

            result.Sweep.Add(new BenchmarkSweepPoint
            {
                Connections = sample.OpenedConnections,
                MbPerSec = Math.Round(sample.MbPerSec, 2),
            });
            Report("sweep", $"{sample.OpenedConnections} conn → {sample.MbPerSec:0.0} MB/s",
                ProgressPercent(15, 75, i + 1, levels.Count), result, sample.OpenedConnections);

            if (sample.OpenedConnections < level)
            {
                // Provider refused the full count — that's the real limit.
                providerCap = sample.OpenedConnections;
                result.Warnings.Add(
                    $"Your provider wouldn't allow more than {sample.OpenedConnections} connections at once, " +
                    "so the test stopped there.");
                break;
            }
        }

        result.ProviderConnectionCap = providerCap;
        result.RecommendedConnections = DetectKnee(result.Sweep, providerCap, result.Warnings);

        // 4) Pipelining — compare off vs. a few depths at a moderate concurrency.
        if (result.Sweep.Count > 0 && result.DataUsedBytes < profile.HardTotalBytes)
        {
            var pipeConns = Math.Min(result.RecommendedConnections ?? 1, profile.PipelineTestConnections);
            if (providerCap.HasValue) pipeConns = Math.Min(pipeConns, providerCap.Value);
            pipeConns = Math.Max(1, pipeConns);

            Report("pipelining", "Testing NNTP pipelining…", 88, result, pipeConns);
            result.Pipelining = await MeasurePipeliningAsync(
                provider, pipeConns, pool, cursor, profile, profile.PipelineDepths,
                bytes => result.DataUsedBytes += bytes, ct).ConfigureAwait(false);
        }

        Report("done", "Done.", 100, result, null);
        return result;
    }

    // ---- Phases ----------------------------------------------------------

    private async Task<BenchmarkLatency> MeasureLatencyAsync(
        UsenetProviderConfig.ConnectionDetails provider, int samples, CancellationToken ct)
    {
        var conn = await UsenetStreamingClient.CreateNewConnection(provider, ct).ConfigureAwait(false);
        try
        {
            await conn.DateAsync(ct).ConfigureAwait(false); // warm-up; excludes TLS/first-command setup
            var measured = new List<double>(samples);
            for (var i = 0; i < samples; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                await conn.DateAsync(ct).ConfigureAwait(false);
                sw.Stop();
                measured.Add(sw.Elapsed.TotalMilliseconds);
            }

            return new BenchmarkLatency
            {
                MinMs = Math.Round(measured.Min(), 1),
                AvgMs = Math.Round(measured.Average(), 1),
                Samples = measured.Count,
            };
        }
        finally
        {
            SafeDispose(conn);
        }
    }

    private async Task<BenchmarkPipelining> MeasurePipeliningAsync(
        UsenetProviderConfig.ConnectionDetails provider, int connections, IReadOnlyList<string> pool,
        int[] cursor, BenchmarkProfile profile, int[] depths, Action<long> addData, CancellationToken ct)
    {
        var result = new BenchmarkPipelining { TestedAtConnections = connections };
        var target = TargetBytes(connections, profile);

        // Baseline: pipelining off.
        var baseline = await MeasureThroughputAsync(
            provider, connections, pool, cursor, target, profile.PerLevelMaxDuration, pipeliningDepth: 0, ct)
            .ConfigureAwait(false);
        addData(baseline.Bytes);
        result.BaselineMbPerSec = Math.Round(baseline.MbPerSec, 2);
        if (baseline.OpenedConnections == 0) return result;

        var bestMbps = baseline.MbPerSec;
        var bestDepth = 0;
        foreach (var depth in depths)
        {
            ct.ThrowIfCancellationRequested();
            var sample = await MeasureThroughputAsync(
                provider, connections, pool, cursor, target, profile.PerLevelMaxDuration, depth, ct)
                .ConfigureAwait(false);
            addData(sample.Bytes);
            result.Tested.Add(new BenchmarkPipeliningPoint { Depth = depth, MbPerSec = Math.Round(sample.MbPerSec, 2) });
            if (sample.MbPerSec > bestMbps) { bestMbps = sample.MbPerSec; bestDepth = depth; }
        }

        // Only recommend turning it on if it's a clear (>10%) win over the baseline.
        if (bestDepth > 0 && baseline.MbPerSec > 0 && bestMbps >= baseline.MbPerSec * 1.10)
        {
            result.RecommendEnabled = true;
            result.RecommendedDepth = bestDepth;
        }
        else
        {
            result.RecommendEnabled = false;
            result.RecommendedDepth = bestDepth > 0 ? bestDepth : 8;
        }

        return result;
    }

    // ---- Throughput core -------------------------------------------------

    private readonly record struct ThroughputSample(double MbPerSec, long Bytes, int OpenedConnections);

    private async Task<ThroughputSample> MeasureThroughputAsync(
        UsenetProviderConfig.ConnectionDetails provider, int requestedConnections, IReadOnlyList<string> pool,
        int[] cursor, long targetBytes, TimeSpan maxDuration, int pipeliningDepth, CancellationToken ct)
    {
        var connections = new List<INntpClient>(requestedConnections);
        try
        {
            for (var i = 0; i < requestedConnections; i++)
            {
                ct.ThrowIfCancellationRequested();
                var conn = await TryOpenConnectionAsync(provider, ct).ConfigureAwait(false);
                if (conn == null) break; // hit the provider's ceiling
                connections.Add(conn);
            }

            if (connections.Count == 0)
                return new ThroughputSample(0, 0, 0);

            var counter = new StrongBox<long>(0);
            using var levelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            levelCts.CancelAfter(maxDuration);
            var token = levelCts.Token;

            var sw = Stopwatch.StartNew();
            var workers = connections
                .Select(conn => Task.Run(() =>
                    DownloadWorkerAsync(conn, pool, cursor, targetBytes, counter, pipeliningDepth, token), token))
                .ToList();
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the measurement window elapsed.
            }
            sw.Stop();

            var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var megabytes = counter.Value / 1_000_000.0;
            return new ThroughputSample(megabytes / seconds, counter.Value, connections.Count);
        }
        finally
        {
            foreach (var conn in connections) SafeDispose(conn);
        }
    }

    private static async Task DownloadWorkerAsync(
        INntpClient conn, IReadOnlyList<string> pool, int[] cursor, long targetBytes,
        StrongBox<long> counter, int depth, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            if (depth <= 1)
            {
                while (!ct.IsCancellationRequested && Interlocked.Read(ref counter.Value) < targetBytes)
                {
                    var id = NextId(pool, cursor);
                    try
                    {
                        var response = await conn.DecodedBodyAsync(id, ct).ConfigureAwait(false);
                        await DrainAsync(response.Stream!, buffer, counter, ct).ConfigureAwait(false);
                    }
                    catch (UsenetArticleNotFoundException)
                    {
                        // Expired/removed article — just move on to the next id.
                    }
                }
            }
            else
            {
                while (!ct.IsCancellationRequested && Interlocked.Read(ref counter.Value) < targetBytes)
                {
                    var batch = NextBatch(pool, cursor, depth * 4);
                    await foreach (var r in conn.DecodedBodiesPipelinedAsync(batch, depth, ct)
                                       .WithCancellation(ct).ConfigureAwait(false))
                    {
                        if (r is { Found: true, Stream: not null })
                            await DrainAsync(r.Stream, buffer, counter, ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested || Interlocked.Read(ref counter.Value) >= targetBytes)
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Window elapsed mid-fetch — fine.
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            // A dead/poisoned connection just drops out for the rest of this
            // window; the remaining workers carry the measurement.
            Log.Debug(e, "Benchmark download worker stopped early.");
        }
    }

    private static async Task DrainAsync(Stream stream, byte[] buffer, StrongBox<long> counter, CancellationToken ct)
    {
        await using (stream)
        {
            int n;
            while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                Interlocked.Add(ref counter.Value, n);
        }
    }

    // ---- Helpers ---------------------------------------------------------

    private static async Task<INntpClient?> TryOpenConnectionAsync(
        UsenetProviderConfig.ConnectionDetails provider, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await UsenetStreamingClient.CreateNewConnection(provider, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // CouldNotConnect / CouldNotLogin here almost always means we've
                // hit the provider's simultaneous-connection limit (502). Retry
                // once to rule out a transient blip, then give up gracefully.
                if (attempt == 1) Log.Debug(e, "Benchmark could not open an additional connection.");
            }
        }

        return null;
    }

    private static List<int> BuildLevels(int configuredMaxConnections, BenchmarkProfile profile)
    {
        // Probe a few steps above the configured max to discover the real sweet
        // spot, but never beyond a safe hard ceiling.
        var ceiling = Math.Clamp(
            Math.Max(configuredMaxConnections + 10, configuredMaxConnections * 2),
            8, HardConnectionCeiling);

        return profile.SweepLevels
            .Where(l => l > 0 && l <= ceiling)
            .Distinct()
            .OrderBy(l => l)
            .ToList();
    }

    private static int? DetectKnee(List<BenchmarkSweepPoint> sweep, int? providerCap, List<string> warnings)
    {
        if (sweep.Count == 0) return null;

        var ordered = sweep.OrderBy(p => p.Connections).ToList();
        var best = ordered.Max(p => p.MbPerSec);
        if (best <= 0) return ordered[0].Connections;

        // Smallest connection count that reaches ~92% of peak throughput.
        var knee = ordered.First(p => p.MbPerSec >= 0.92 * best).Connections;
        if (providerCap.HasValue) knee = Math.Min(knee, providerCap.Value);

        // If we were still climbing at the top of the sweep, say so.
        var peak = ordered[^1];
        if (peak.MbPerSec >= best - 1e-9 && ordered.Count >= 2)
        {
            var prev = ordered[^2];
            if (prev.MbPerSec > 0 && (peak.MbPerSec - prev.MbPerSec) / prev.MbPerSec > 0.08)
                warnings.Add("Speed was still climbing at the highest level tested — a faster line or even more connections may help.");
        }

        return Math.Max(1, knee);
    }

    // Scale how much data a level transfers with its connection count so even
    // high counts move enough bytes per connection to measure meaningfully,
    // clamped between the profile's base and 3× that base.
    private static long TargetBytes(int connections, BenchmarkProfile profile) =>
        Math.Clamp((long)connections * ArticleBytesEstimate * 2, profile.PerLevelBytes, profile.PerLevelBytes * 3);

    // A wider depth spread for pipelining-only runs, where the depth is the whole point.
    private static int[] FocusedPipelineDepths(BenchmarkIntensity intensity) =>
        intensity == BenchmarkIntensity.Thorough ? [4, 8, 16, 32] : [4, 8, 16];

    private static string NextId(IReadOnlyList<string> pool, int[] cursor)
    {
        var i = Interlocked.Increment(ref cursor[0]) - 1;
        return pool[((i % pool.Count) + pool.Count) % pool.Count];
    }

    private static List<string> NextBatch(IReadOnlyList<string> pool, int[] cursor, int count)
    {
        var batch = new List<string>(count);
        for (var i = 0; i < count; i++) batch.Add(NextId(pool, cursor));
        return batch;
    }

    private static int ProgressPercent(int start, int end, int step, int totalSteps) =>
        start + (int)((end - start) * (double)step / Math.Max(1, totalSteps));

    private void Report(string phase, string status, int percent, BenchmarkResult result, int? currentConnections)
    {
        var update = new BenchmarkProgressUpdate
        {
            Phase = phase,
            Status = status,
            Percent = percent,
            CurrentConnections = currentConnections,
            DataUsedBytes = result.DataUsedBytes,
            Sweep = result.Sweep.Select(p => new BenchmarkSweepPoint { Connections = p.Connections, MbPerSec = p.MbPerSec }).ToList(),
        };
        // Fire-and-forget: progress is best-effort and must not block the run.
        _ = websocketManager.SendMessage(WebsocketTopic.BenchmarkProgress, JsonSerializer.Serialize(update, JsonOptions));
    }

    private static void SafeDispose(INntpClient conn)
    {
        try { conn.Dispose(); }
        catch (Exception e) { Log.Debug(e, "Failed to dispose benchmark connection."); }
    }
}
