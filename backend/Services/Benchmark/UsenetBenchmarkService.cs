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
        long? dataBudgetBytes,
        int? verifyConnections,
        CancellationToken ct)
    {
        var profile = BenchmarkProfile.For(intensity);
        var budget = Math.Max(50_000_000, dataBudgetBytes ?? profile.HardTotalBytes);
        var result = new BenchmarkResult { PipeliningOnly = pipeliningOnly, DataBudgetBytes = budget };
        long Remaining() => Math.Max(0, budget - result.DataUsedBytes);

        using var ladder = new BenchmarkConnectionLadder(provider);

        // 1) Latency — also doubles as a connectivity/credentials check.
        Report("latency", "Measuring latency…", 5, result, null);
        await ladder.EnsureAsync(1, ct).ConfigureAwait(false);
        result.Latency = await MeasureLatencyAsync(
            ladder.Connections[0], profile.LatencySamples, ct).ConfigureAwait(false);

        // 2) Corpus — real message-ids to download.
        Report("corpus", "Gathering test articles…", 12, result, null);
        var ids = await corpus.GetSegmentPoolAsync(profile.MaxCorpusSegments, ct).ConfigureAwait(false);
        if (ids.Count == 0)
        {
            result.ThroughputTested = false;
            result.Warnings.Add(
                "No downloaded articles were available to measure speed, so only latency was tested. " +
                "Download something first, then re-run to get a connection recommendation.");
            Report("done", "Done — latency only.", 100, result, null);
            return result;
        }

        var pool = new BenchmarkSegmentPool(ids);
        result.ThroughputTested = true;
        if (ids.Count < 200)
            result.Warnings.Add("Only a small pool of test articles was available, so speed numbers may be a little noisy.");

        // Pipelining-only mode: leave the connection count alone and just find
        // the best pipelining depth at the count the user already runs.
        if (pipeliningOnly)
        {
            var conns = Math.Clamp(configuredMaxConnections, 1, HardConnectionCeiling);
            Report("pipelining", $"Testing pipelining at {conns} connection{(conns == 1 ? "" : "s")}…", 30, result, conns);
            await ladder.EnsureAsync(conns, ct).ConfigureAwait(false);
            result.Pipelining = await MeasurePipeliningAsync(
                ladder, pool, profile, FocusedPipelineDepths(intensity), Remaining,
                bytes => result.DataUsedBytes += bytes, result.Warnings, ct).ConfigureAwait(false);

            if (conns >= 24)
                result.Warnings.Add(
                    "At high connection counts, pipelining usually adds little — running many connections in " +
                    "parallel already hides most of the per-request latency it would otherwise save.");
        }
        else if (verifyConnections is int vc0)
        {
            var vc = Math.Clamp(vc0, 1, HardConnectionCeiling);
            Report("sweep", $"Verifying {vc} connection{(vc == 1 ? "" : "s")}…", 40, result, vc);
            var have = await ladder.EnsureAsync(vc, ct).ConfigureAwait(false);

            var bootstrap = await MeasureThroughputAsync(
                ladder, pool, Math.Min(profile.PerLevelBytes, Remaining()),
                profile.WarmupDuration, TimeSpan.FromSeconds(1.5), profile.PerLevelMaxDuration,
                pipeliningDepth: 0, ct).ConfigureAwait(false);
            result.DataUsedBytes += bootstrap.Bytes;

            Report("sweep", $"Measuring {have} connection{(have == 1 ? "" : "s")}…", 65, result, have);
            var sample = await MeasureThroughputAsync(
                ladder, pool, AdaptiveTargetBytes(bootstrap.MbPerSec, profile, Remaining()),
                profile.WarmupDuration, profile.MeasureWindow, profile.PerLevelMaxDuration,
                pipeliningDepth: 0, ct).ConfigureAwait(false);
            result.DataUsedBytes += sample.Bytes;

            result.Sweep.Add(new BenchmarkSweepPoint
            {
                Connections = have,
                MbPerSec = Math.Round(sample.MbPerSec, 2),
                Cv = Math.Round(sample.Cv, 3),
            });
            result.RecommendedConnections = have;
            result.VerificationRun = true;
            if (have < vc)
                result.Warnings.Add($"Only {have} of {vc} connections could be opened for the verification run.");
        }
        else
        {
            // 3) Throughput sweep — climb connection counts until the knee or the cap.
            var levels = BuildLevels(configuredMaxConnections, profile);
            int? providerCap = null;
            double lastMbPerSec = 0;
            for (var i = 0; i < levels.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var level = levels[i];

                if (Remaining() < MinUsefulBytes(lastMbPerSec, profile))
                {
                    result.BudgetLimited = true;
                    result.Warnings.Add(
                        "Reached the data budget before testing every connection level. Raise the budget for a fuller picture.");
                    break;
                }

                Report("sweep", $"Testing {level} connection{(level == 1 ? "" : "s")}…",
                    ProgressPercent(15, 75, i, levels.Count), result, level);

                var have = await ladder.EnsureAsync(level, ct).ConfigureAwait(false);
                if (have == 0)
                {
                    providerCap ??= Math.Max(1, i > 0 ? levels[i - 1] : 1);
                    break;
                }

                var sample = await MeasureThroughputAsync(
                    ladder, pool, AdaptiveTargetBytes(lastMbPerSec, profile, Remaining()),
                    profile.WarmupDuration, profile.MeasureWindow, profile.PerLevelMaxDuration,
                    pipeliningDepth: 0, ct).ConfigureAwait(false);
                result.DataUsedBytes += sample.Bytes;
                lastMbPerSec = Math.Max(lastMbPerSec, sample.MbPerSec);

                result.Sweep.Add(new BenchmarkSweepPoint
                {
                    Connections = have,
                    MbPerSec = Math.Round(sample.MbPerSec, 2),
                    Cv = Math.Round(sample.Cv, 3),
                });
                Report("sweep", $"{have} conn → {sample.MbPerSec:0.0} MB/s",
                    ProgressPercent(15, 75, i + 1, levels.Count), result, have);

                if (have < level)
                {
                    providerCap = have;
                    result.Warnings.Add(
                        $"Your provider wouldn't allow more than {have} connections at once, so the test stopped there.");
                    break;
                }
            }

            result.ProviderConnectionCap = providerCap;
            result.RecommendedConnections = DetectKnee(
                result.Sweep, providerCap, result.Warnings, out var stillClimbing);
            result.StillClimbing = stillClimbing;

            // Thorough only: confirm the pick with a second independent window and blend.
            if (intensity == BenchmarkIntensity.Thorough
                && result.RecommendedConnections is int knee
                && Remaining() > MinUsefulBytes(lastMbPerSec, profile))
            {
                Report("sweep", $"Confirming {knee} connection{(knee == 1 ? "" : "s")}…", 80, result, knee);
                ladder.ShrinkTo(knee);
                if (ladder.Count > 0)
                {
                    var confirm = await MeasureThroughputAsync(
                        ladder, pool, AdaptiveTargetBytes(lastMbPerSec, profile, Remaining()),
                        profile.WarmupDuration, profile.MeasureWindow, profile.PerLevelMaxDuration,
                        pipeliningDepth: 0, ct).ConfigureAwait(false);
                    result.DataUsedBytes += confirm.Bytes;
                    var point = result.Sweep.FirstOrDefault(p => p.Connections == knee);
                    if (point != null && confirm.MbPerSec > 0)
                    {
                        if (point.MbPerSec > 0)
                        {
                            result.ConfirmDeltaPct = Math.Round(
                                Math.Abs(confirm.MbPerSec - point.MbPerSec) / point.MbPerSec * 100, 1);
                            if (result.ConfirmDeltaPct > 20)
                                result.Warnings.Add(
                                    "The confirmation run didn't reproduce the measured speed closely, " +
                                    "so the recommendation may shift slightly between runs.");
                        }

                        point.MbPerSec = Math.Round((point.MbPerSec + confirm.MbPerSec) / 2, 2);
                        point.Cv = Math.Round(Math.Max(point.Cv, confirm.Cv), 3);
                        result.RecommendedConnections = DetectKnee(
                            result.Sweep, providerCap, [], out stillClimbing);
                        result.StillClimbing = stillClimbing;
                    }
                }
            }

            // 4) Pipelining — compare off vs. a few depths at a moderate concurrency.
            if (result.Sweep.Count > 0 && Remaining() > MinUsefulBytes(lastMbPerSec, profile))
            {
                var pipeConns = Math.Min(result.RecommendedConnections ?? 1, profile.PipelineTestConnections);
                if (providerCap.HasValue) pipeConns = Math.Min(pipeConns, providerCap.Value);
                pipeConns = Math.Max(1, pipeConns);

                Report("pipelining", "Testing NNTP pipelining…", 88, result, pipeConns);
                ladder.ShrinkTo(pipeConns);
                await ladder.EnsureAsync(pipeConns, ct).ConfigureAwait(false);
                result.Pipelining = await MeasurePipeliningAsync(
                    ladder, pool, profile, profile.PipelineDepths, Remaining,
                    bytes => result.DataUsedBytes += bytes, result.Warnings, ct).ConfigureAwait(false);
            }
        }

        if (pool.WrappedAround)
        {
            result.WrappedPool = true;
            result.Warnings.Add(
                "The test re-downloaded some articles more than once, so provider caching may make speeds read high. " +
                "A larger library of completed downloads gives the test more unique data.");
        }
        if (pool.DeadCount > 0 && pool.DeadCount * 10 > pool.Count)
            result.Warnings.Add(
                $"{pool.DeadCount} test articles were no longer available on the provider, which can bias speeds low. " +
                "Downloading something recent refreshes the test pool.");

        result.Confidence = ComputeConfidence(result);
        Report("done", "Done.", 100, result, null);
        return result;
    }

    // ---- Phases ----------------------------------------------------------

    private static async Task<BenchmarkLatency> MeasureLatencyAsync(
        INntpClient conn, int samples, CancellationToken ct)
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

    private async Task<BenchmarkPipelining> MeasurePipeliningAsync(
        BenchmarkConnectionLadder ladder, BenchmarkSegmentPool pool, BenchmarkProfile profile,
        int[] depths, Func<long> remainingBudget, Action<long> addData, List<string> warnings,
        CancellationToken ct)
    {
        var result = new BenchmarkPipelining { TestedAtConnections = ladder.Count };
        double last = 0;

        // Baseline: pipelining off.
        var baseline = await MeasureThroughputAsync(
            ladder, pool, AdaptiveTargetBytes(last, profile, remainingBudget()),
            profile.WarmupDuration, profile.MeasureWindow, profile.PerLevelMaxDuration,
            pipeliningDepth: 0, ct).ConfigureAwait(false);
        addData(baseline.Bytes);
        last = baseline.MbPerSec;
        result.BaselineMbPerSec = Math.Round(baseline.MbPerSec, 2);
        if (baseline.OpenedConnections == 0) return result;

        var bestMbps = baseline.MbPerSec;
        var bestDepth = 0;
        foreach (var depth in depths)
        {
            ct.ThrowIfCancellationRequested();
            if (remainingBudget() < MinUsefulBytes(last, profile))
            {
                warnings.Add("Reached the data budget before testing every pipelining depth.");
                break;
            }

            var sample = await MeasureThroughputAsync(
                ladder, pool, AdaptiveTargetBytes(last, profile, remainingBudget()),
                profile.WarmupDuration, profile.MeasureWindow, profile.PerLevelMaxDuration,
                depth, ct).ConfigureAwait(false);
            addData(sample.Bytes);
            last = Math.Max(last, sample.MbPerSec);
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

    private readonly record struct ThroughputSample(
        double MbPerSec, double Cv, long Bytes, int OpenedConnections, double WindowSeconds);

    private static async Task<ThroughputSample> MeasureThroughputAsync(
        BenchmarkConnectionLadder ladder, BenchmarkSegmentPool pool, long targetBytes,
        TimeSpan warmup, TimeSpan window, TimeSpan maxDuration, int pipeliningDepth,
        CancellationToken ct)
    {
        var opened = ladder.Count;
        if (opened == 0) return new ThroughputSample(0, 0, 0, 0, 0);

        var counter = new StrongBox<long>(0);
        var dead = new System.Collections.Concurrent.ConcurrentBag<INntpClient>();
        using var levelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        levelCts.CancelAfter(maxDuration);
        var token = levelCts.Token;

        var workers = ladder.Connections
            .Select(conn => Task.Run(async () =>
            {
                var healthy = await DownloadWorkerAsync(
                    conn, pool, targetBytes, counter, pipeliningDepth, token).ConfigureAwait(false);
                if (!healthy) dead.Add(conn);
            }, token))
            .ToList();

        // Warm-up: let TCP windows open and the first-article latency pass, unmeasured.
        await SafeDelay(warmup, token).ConfigureAwait(false);
        var startBytes = Interlocked.Read(ref counter.Value);
        var sw = Stopwatch.StartNew();

        // Steady window: snapshot the counter into ~500ms buckets with real timestamps.
        var buckets = new List<(long Bytes, double Seconds)>();
        var prevBytes = startBytes;
        var prevTime = 0.0;
        while (sw.Elapsed < window && !token.IsCancellationRequested && !workers.All(w => w.IsCompleted))
        {
            await SafeDelay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
            var nowBytes = Interlocked.Read(ref counter.Value);
            var nowTime = sw.Elapsed.TotalSeconds;
            buckets.Add((nowBytes - prevBytes, nowTime - prevTime));
            (prevBytes, prevTime) = (nowBytes, nowTime);
        }

        var endBytes = Interlocked.Read(ref counter.Value);
        var elapsed = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        levelCts.Cancel();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        ladder.Prune(dead.ToArray());

        var totalBytes = Interlocked.Read(ref counter.Value);
        var (steady, cv) = ComputeSteadyRate(buckets, endBytes - startBytes, elapsed);
        return new ThroughputSample(steady, cv, totalBytes, opened, elapsed);
    }

    private static async Task<bool> DownloadWorkerAsync(
        INntpClient conn, BenchmarkSegmentPool pool, long targetBytes,
        StrongBox<long> counter, int depth, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            if (depth <= 1)
            {
                while (!ct.IsCancellationRequested && Interlocked.Read(ref counter.Value) < targetBytes)
                {
                    var id = pool.Next();
                    try
                    {
                        var response = await conn.DecodedBodyAsync(id, ct).ConfigureAwait(false);
                        await DrainAsync(response.Stream!, buffer, counter, ct).ConfigureAwait(false);
                    }
                    catch (UsenetArticleNotFoundException)
                    {
                        pool.MarkDead(id);
                    }
                }
            }
            else
            {
                while (!ct.IsCancellationRequested && Interlocked.Read(ref counter.Value) < targetBytes)
                {
                    var batch = pool.NextBatch(depth * 4);
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

            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            Log.Debug(e, "Benchmark download worker stopped early.");
            return false;
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

    // Bytes needed to keep the pipe busy through warm-up plus a minimally-useful (~1.5s) window.
    internal static long MinUsefulBytes(double lastMbPerSec, BenchmarkProfile profile)
    {
        var seconds = profile.WarmupDuration.TotalSeconds + 1.5;
        var bytesPerSec = lastMbPerSec > 0 ? lastMbPerSec * 1_000_000 : 2_000_000;
        return Math.Max(4_000_000, (long)(bytesPerSec * seconds));
    }

    // Target enough bytes that workers never run dry before the wall clock stops the
    // window, even if this level doubles the previous level's throughput.
    internal static long AdaptiveTargetBytes(
        double lastMbPerSec, BenchmarkProfile profile, long remainingBudget)
    {
        var seconds = (profile.WarmupDuration + profile.MeasureWindow).TotalSeconds;
        var est = lastMbPerSec > 0
            ? (long)(lastMbPerSec * 1_000_000 * 2.0 * seconds)
            : profile.PerLevelBytes;
        var max = Math.Max(1_000_000, remainingBudget);
        var min = Math.Min(profile.PerLevelBytes, max);
        return Math.Clamp(est, min, max);
    }

    // Median-of-buckets rate + coefficient of variation. Falls back to the whole-window
    // mean (with a pessimistic CV) when the window produced too few buckets to judge.
    internal static (double MbPerSec, double Cv) ComputeSteadyRate(
        IReadOnlyList<(long Bytes, double Seconds)> buckets,
        long fallbackBytes,
        double fallbackSeconds)
    {
        var rates = buckets
            .Where(b => b.Seconds > 0.05)
            .Select(b => b.Bytes / b.Seconds / 1_000_000.0)
            .ToList();

        if (rates.Count < 3)
        {
            var mean = fallbackSeconds > 0.05
                ? fallbackBytes / fallbackSeconds / 1_000_000.0
                : 0;
            return (mean, rates.Count == 0 ? 1.0 : 0.5);
        }

        var sorted = rates.OrderBy(r => r).ToList();
        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;

        var avg = rates.Average();
        var cv = avg > 0
            ? Math.Sqrt(rates.Sum(r => (r - avg) * (r - avg)) / rates.Count) / avg
            : 0;
        return (median, cv);
    }

    internal static string ComputeConfidence(BenchmarkResult result)
    {
        if (!result.ThroughputTested) return "low";

        var maxCv = KneeRegionMaxCv(result);

        // Budget exhaustion only tanks confidence when the curve was still climbing —
        // i.e. the untested upper levels actually mattered.
        if (result.BudgetLimited && result.StillClimbing) return "low";
        if (maxCv > 0.30) return "low";

        // A tight confirm-run agreement can override the wrap-pool medium cap.
        var confirmTight = result.ConfirmDeltaPct is <= 5;
        var confirmLoose = result.ConfirmDeltaPct is > 20;

        string confidence;
        if ((!confirmTight && result.WrappedPool) || maxCv > 0.15)
            confidence = "medium";
        else
            confidence = "high";

        // Budget ran out after a found plateau: downgrade high → medium at most.
        if (result.BudgetLimited && confidence == "high")
            confidence = "medium";

        // Confirm window disagreed with the original knee measurement.
        if (confirmLoose)
            confidence = confidence == "high" ? "medium" : "low";

        return confidence;
    }

    // CV near the recommended level (or the upper half of the sweep) matters;
    // jitter at 1–2 connections shouldn't poison an otherwise clean run.
    internal static double KneeRegionMaxCv(BenchmarkResult result)
    {
        if (result.Sweep.Count == 0) return 0;
        var threshold = result.RecommendedConnections is int rec and > 0
            ? Math.Max(1, rec / 2)
            : result.Sweep.Max(p => p.Connections) / 2;
        var region = result.Sweep.Where(p => p.Connections >= threshold).ToList();
        if (region.Count == 0) region = result.Sweep;
        return region.Max(p => p.Cv);
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

    internal static int? DetectKnee(
        List<BenchmarkSweepPoint> sweep, int? providerCap, List<string> warnings)
        => DetectKnee(sweep, providerCap, warnings, out _);

    internal static int? DetectKnee(
        List<BenchmarkSweepPoint> sweep, int? providerCap, List<string> warnings,
        out bool stillClimbing)
    {
        stillClimbing = false;
        if (sweep.Count == 0) return null;
        var ordered = sweep.OrderBy(p => p.Connections).ToList();

        // Reference peak = mean of the two best points so a single lucky spike can't
        // drag the recommendation around between runs.
        var peakRef = ordered.OrderByDescending(p => p.MbPerSec).Take(2).Average(p => p.MbPerSec);
        if (peakRef <= 0) return ordered[0].Connections;

        var knee = ordered.First(p => p.MbPerSec >= 0.92 * peakRef).Connections;
        if (providerCap.HasValue) knee = Math.Min(knee, providerCap.Value);

        var best = ordered.Max(p => p.MbPerSec);
        var peak = ordered[^1];
        if (peak.MbPerSec >= best - 1e-9 && ordered.Count >= 2)
        {
            var prev = ordered[^2];
            if (prev.MbPerSec > 0 && (peak.MbPerSec - prev.MbPerSec) / prev.MbPerSec > 0.08)
            {
                stillClimbing = true;
                warnings.Add("Speed was still climbing at the highest level tested — a faster line or even more connections may help.");
            }
        }

        if (ordered.Any(p => p.Cv > 0.25))
            warnings.Add(
                "Some measurements were noisy (throughput fluctuated during the window), " +
                "so the recommendation may shift slightly between runs.");

        return Math.Max(1, knee);
    }

    // A wider depth spread for pipelining-only runs, where the depth is the whole point.
    private static int[] FocusedPipelineDepths(BenchmarkIntensity intensity) =>
        intensity == BenchmarkIntensity.Thorough ? [4, 8, 16, 32] : [4, 8, 16];

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
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
            DataBudgetBytes = result.DataBudgetBytes,
            Sweep = result.Sweep.Select(p => new BenchmarkSweepPoint
            {
                Connections = p.Connections,
                MbPerSec = p.MbPerSec,
                Cv = p.Cv,
            }).ToList(),
        };
        // Fire-and-forget: progress is best-effort and must not block the run.
        _ = websocketManager.SendMessage(WebsocketTopic.BenchmarkProgress, JsonSerializer.Serialize(update, JsonOptions));
    }
}
