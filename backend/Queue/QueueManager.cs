using System.Collections.Concurrent;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Queue;

public class QueueManager : IDisposable
{
    private InProgressQueueItem? _inProgressQueueItem;

    private readonly ConcurrentDictionary<Guid, int> _retryAttempts = new();

    private readonly UsenetStreamingClient _usenetClient;
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private readonly ProviderUsageTracker _providerUsageTracker;
    private readonly WatchdogLog _watchdogLog;
    private readonly QueueItemSourceTracker _sourceTracker;
    private readonly BenchmarkGate _benchmarkGate;

    private CancellationTokenSource _sleepingQueueToken = new();
    private readonly Lock _sleepingQueueLock = new();

    // Overridable in tests so persistent-failure / idle-sleep behaviour can be
    // exercised without a real database.
    internal TimeSpan ErrorBackoffDelay { get; set; } = TimeSpan.FromSeconds(5);
    internal TimeSpan IdleDelay { get; set; } = TimeSpan.FromMinutes(1);
    internal Func<CancellationToken, Task<(QueueItem? queueItem, Stream? queueNzbStream)>>?
        GetTopQueueItemOverride
    { get; set; }
    internal Func<CancellationToken, Task<DateTime?>>? GetNextPauseUntilOverride { get; set; }

    public QueueManager(
        UsenetStreamingClient usenetClient,
        ConfigManager configManager,
        WebsocketManager websocketManager,
        ProviderUsageTracker providerUsageTracker,
        WatchdogLog watchdogLog,
        QueueItemSourceTracker sourceTracker,
        BenchmarkGate benchmarkGate
    ) : this(
        usenetClient, configManager, websocketManager, providerUsageTracker,
        watchdogLog, sourceTracker, benchmarkGate, startLoop: true)
    {
    }

    internal QueueManager(
        UsenetStreamingClient usenetClient,
        ConfigManager configManager,
        WebsocketManager websocketManager,
        ProviderUsageTracker providerUsageTracker,
        WatchdogLog watchdogLog,
        QueueItemSourceTracker sourceTracker,
        BenchmarkGate benchmarkGate,
        bool startLoop
    )
    {
        _usenetClient = usenetClient;
        _configManager = configManager;
        _websocketManager = websocketManager;
        _providerUsageTracker = providerUsageTracker;
        _watchdogLog = watchdogLog;
        _sourceTracker = sourceTracker;
        _benchmarkGate = benchmarkGate;
        _cancellationTokenSource = CancellationTokenSource
            .CreateLinkedTokenSource(SigtermUtil.GetCancellationToken());
        if (startLoop)
            _ = ProcessQueueAsync(_cancellationTokenSource.Token);
    }

    public (QueueItem? queueItem, int? progress) GetInProgressQueueItem()
    {
        return (_inProgressQueueItem?.QueueItem, _inProgressQueueItem?.ProgressPercentage);
    }

    public void AwakenQueue(DateTime? dateTime = null)
    {
        TimeSpan? cancelAfter = dateTime.HasValue ? (dateTime.Value - DateTime.Now) : null;
        lock (_sleepingQueueLock)
        {
            if (cancelAfter.HasValue && cancelAfter.Value > TimeSpan.Zero)
                _sleepingQueueToken.CancelAfter(cancelAfter.Value);
            else
                _sleepingQueueToken.Cancel();
        }
    }

    public async Task RemoveQueueItemsAsync
    (
        List<Guid> queueItemIds,
        DavDatabaseClient dbClient,
        CancellationToken ct = default
    )
    {
        await LockAsync(async () =>
        {
            var inProgressId = _inProgressQueueItem?.QueueItem?.Id;
            if (inProgressId is not null && queueItemIds.Contains(inProgressId.Value))
            {
                await _inProgressQueueItem!.CancellationTokenSource.CancelAsync().ConfigureAwait(false);
                await _inProgressQueueItem.ProcessingTask.ConfigureAwait(false);
                _inProgressQueueItem = null;
            }

            await dbClient.RemoveQueueItemsAsync(queueItemIds, ct).ConfigureAwait(false);
            await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            foreach (var id in queueItemIds) _retryAttempts.TryRemove(id, out _);
        }).ConfigureAwait(false);
    }

    internal async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // While a speed-test is running, hold off starting new downloads so
            // it gets the provider's full connection budget. Any item already in
            // progress finishes naturally; this only gates new work. Resumes
            // within ~1s of the test ending.
            if (_benchmarkGate.IsPaused)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                continue;
            }

            try
            {
                // get the next queue-item from the database (or test override)
                (QueueItem? queueItem, Stream? queueNzbStream) topItem;
                DavDatabaseClient? dbClient = null;
                DavDatabaseContext? dbContext = null;
                try
                {
                    if (GetTopQueueItemOverride is not null)
                    {
                        topItem = await GetTopQueueItemOverride(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        dbContext = new DavDatabaseContext();
                        dbClient = new DavDatabaseClient(dbContext);
                        topItem = await LockAsync(() => dbClient.GetTopQueueItem(ct)).ConfigureAwait(false);
                    }

                    if (topItem.queueItem is null)
                    {
                        try
                        {
                            // if we're done with the queue, wait until the next retry pause
                            // expires (or IdleDelay, whichever is sooner). Also wake early
                            // on cancellation of _sleepingQueueToken / process shutdown (ct).
                            var idleDelay = await ComputeIdleDelayAsync(dbClient, ct)
                                .ConfigureAwait(false);
                            using var idleWait = CancellationTokenSource.CreateLinkedTokenSource(
                                ct, _sleepingQueueToken.Token);
                            await Task.Delay(idleDelay, idleWait.Token).ConfigureAwait(false);
                        }
                        catch when (_sleepingQueueToken.IsCancellationRequested)
                        {
                            lock (_sleepingQueueLock)
                            {
                                if (!_sleepingQueueToken.TryReset())
                                {
                                    _sleepingQueueToken.Dispose();
                                    _sleepingQueueToken = new CancellationTokenSource();
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // ct fired: fall through; the while condition exits the loop
                        }

                        continue;
                    }

                    // create an article-caching nntp-client.
                    // the cache will be scoped only to this single queue-item.
                    using var cachingUsenetClient = new ArticleCachingNntpClient(_usenetClient);

                    // process the queue-item
                    try
                    {
                        using var queueItemCancellationTokenSource =
                            CancellationTokenSource.CreateLinkedTokenSource(ct);
                        await LockAsync(() =>
                        {
                            // ReSharper disable twice AccessToDisposedClosure
                            _inProgressQueueItem = BeginProcessingQueueItem(
                                dbClient!, cachingUsenetClient,
                                topItem.queueItem, topItem.queueNzbStream,
                                queueItemCancellationTokenSource);
                        }).ConfigureAwait(false);
                        await (_inProgressQueueItem?.ProcessingTask ?? Task.CompletedTask)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        if (topItem.queueNzbStream is not null)
                            await topItem.queueNzbStream!.DisposeAsync();
                    }
                }
                finally
                {
                    if (dbContext is not null)
                        await dbContext.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "An unexpected error occurred while processing the queue");
                try { await Task.Delay(ErrorBackoffDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* shutting down */ }
            }
            finally
            {
                await LockAsync(() => { _inProgressQueueItem = null; }).ConfigureAwait(false);
            }
        }
    }

    private InProgressQueueItem BeginProcessingQueueItem
    (
        DavDatabaseClient dbClient,
        INntpClient usenetClient,
        QueueItem queueItem,
        Stream? queueNzbStream,
        CancellationTokenSource cts
    )
    {
        var progressHook = new Progress<int>();
        var task = new QueueItemProcessor(
            queueItem, queueNzbStream, dbClient, usenetClient,
            _configManager, _websocketManager, _providerUsageTracker,
            _watchdogLog, _sourceTracker, progressHook, _retryAttempts, cts.Token
        ).ProcessAsync();
        var inProgressQueueItem = new InProgressQueueItem()
        {
            QueueItem = queueItem,
            ProcessingTask = task,
            ProgressPercentage = 0,
            CancellationTokenSource = cts
        };
        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(200));
        var providersDebounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(500));
        var progressLock = new object();
        var latestProgress = 0;
        var lastSentProgress = -1;

        void SendLatestProgress()
        {
            int value;
            lock (progressLock)
            {
                if (latestProgress <= lastSentProgress) return;
                value = latestProgress;
                lastSentProgress = value;
            }

            _websocketManager.SendMessage(WebsocketTopic.QueueItemProgress, $"{queueItem.Id}|{value}");
        }

        progressHook.ProgressChanged += (_, progress) =>
        {
            try
            {
                lock (progressLock)
                {
                    if (progress > latestProgress) latestProgress = progress;
                    inProgressQueueItem.ProgressPercentage = latestProgress;
                }

                if (progress is 100 or 200) SendLatestProgress();
                else debounce(SendLatestProgress);
                providersDebounce(() => _websocketManager.SendMessage(
                    WebsocketTopic.QueueItemProviders, BuildProvidersMessage(queueItem.Id)));
            }
            catch (Exception e)
            {
                Log.Warning(e, "Queue progress broadcast failed for {QueueItemId}", queueItem.Id);
            }
        };
        return inProgressQueueItem;
    }

    private string BuildProvidersMessage(Guid queueItemId)
    {
        var snapshot = _providerUsageTracker.Snapshot(queueItemId);
        var providers = _configManager.GetUsenetProviderConfig().Providers;
        var displayByMetricsKey = ProviderUsageHelper.BuildDisplayByMetricsKey(providers);

        // The wire format is host-based; resolve metrics keys to display hosts so
        // Guids never reach the UI, aggregating same-host accounts into one entry.
        var merged = new Dictionary<string, long>();
        foreach (var kv in snapshot)
        {
            var host = displayByMetricsKey.TryGetValue(kv.Key, out var display) ? display.Host : kv.Key;
            merged.TryGetValue(host, out var existing);
            merged[host] = existing + kv.Value;
        }

        var configured = providers
            .Select(p => p.Host)
            .Where(h => !string.IsNullOrEmpty(h))
            .Distinct();
        foreach (var host in configured)
            if (!merged.ContainsKey(host)) merged[host] = 0;
        var payload = string.Join(",", merged.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{queueItemId}|{payload}";
    }

    private async Task LockAsync(Func<Task> actionAsync)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await actionAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<T> LockAsync<T>(Func<Task<T>> actionAsync)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await actionAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<TimeSpan> ComputeIdleDelayAsync(
        DavDatabaseClient? dbClient, CancellationToken ct)
    {
        try
        {
            DateTime? nextPause;
            if (GetNextPauseUntilOverride is not null)
                nextPause = await GetNextPauseUntilOverride(ct).ConfigureAwait(false);
            else if (dbClient is not null)
                nextPause = await dbClient.GetNextQueueItemPauseUntil(ct).ConfigureAwait(false);
            else
                return IdleDelay;

            if (nextPause is null) return IdleDelay;

            // Small buffer so we wake just AFTER the pause expires; waking a hair
            // early would find no eligible item and sleep a full IdleDelay again.
            var untilNextPause = nextPause.Value - DateTime.Now + TimeSpan.FromMilliseconds(250);
            if (untilNextPause <= TimeSpan.Zero) return TimeSpan.FromMilliseconds(250);
            return untilNextPause < IdleDelay ? untilNextPause : IdleDelay;
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            Log.Debug(e, "Failed to compute next queue pause; falling back to idle delay");
            return IdleDelay;
        }
    }

    private async Task LockAsync(Action action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private class InProgressQueueItem
    {
        public QueueItem QueueItem { get; init; } = null!;
        public int ProgressPercentage { get; set; }
        public Task ProcessingTask { get; init; } = null!;
        public CancellationTokenSource CancellationTokenSource { get; init; } = null!;
    }
}
