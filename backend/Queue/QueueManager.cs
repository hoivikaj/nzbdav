using System.Collections.Concurrent;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Services;
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

    public QueueManager(
        UsenetStreamingClient usenetClient,
        ConfigManager configManager,
        WebsocketManager websocketManager,
        ProviderUsageTracker providerUsageTracker,
        WatchdogLog watchdogLog,
        QueueItemSourceTracker sourceTracker,
        BenchmarkGate benchmarkGate
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

    private async Task ProcessQueueAsync(CancellationToken ct)
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
                // get the next queue-item from the database
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                var topItem = await LockAsync(() => dbClient.GetTopQueueItem(ct)).ConfigureAwait(false);
                if (topItem.queueItem is null)
                {
                    try
                    {
                        // if we're done with the queue, wait a minute before checking again.
                        // or wait until awoken by cancellation of _sleepingQueueToken
                        await Task.Delay(TimeSpan.FromMinutes(1), _sleepingQueueToken.Token).ConfigureAwait(false);
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

                    continue;
                }

                // create an article-caching nntp-client.
                // the cache will be scoped only to this single queue-item.
                using var cachingUsenetClient = new ArticleCachingNntpClient(_usenetClient);

                // process the queue-item
                try
                {
                    using var queueItemCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    await LockAsync(() =>
                    {
                        // ReSharper disable twice AccessToDisposedClosure
                        _inProgressQueueItem = BeginProcessingQueueItem(dbClient, cachingUsenetClient,
                            topItem.queueItem, topItem.queueNzbStream, queueItemCancellationTokenSource);
                    }).ConfigureAwait(false);
                    await (_inProgressQueueItem?.ProcessingTask ?? Task.CompletedTask).ConfigureAwait(false);
                }
                finally
                {
                    if (topItem.queueNzbStream is not null)
                        await topItem.queueNzbStream!.DisposeAsync();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "An unexpected error occurred while processing the queue");
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
            lock (progressLock)
            {
                if (progress > latestProgress) latestProgress = progress;
                inProgressQueueItem.ProgressPercentage = latestProgress;
            }

            if (progress is 100 or 200) SendLatestProgress();
            else debounce(SendLatestProgress);
            providersDebounce(() => _websocketManager.SendMessage(
                WebsocketTopic.QueueItemProviders, BuildProvidersMessage(queueItem.Id)));
        };
        return inProgressQueueItem;
    }

    private string BuildProvidersMessage(Guid queueItemId)
    {
        var snapshot = _providerUsageTracker.Snapshot(queueItemId);
        var configured = _configManager.GetUsenetProviderConfig().Providers
            .Select(p => p.Host)
            .Where(h => !string.IsNullOrEmpty(h))
            .Distinct();
        var merged = new Dictionary<string, long>(snapshot);
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
