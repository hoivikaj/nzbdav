using Microsoft.Extensions.Hosting;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Backup;
using NzbWebDAV.Services;
using NzbWebDAV.Tasks;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Runs a database backup daily at the configured local time when scheduling is enabled.
/// </summary>
public class DatabaseBackupSchedulerService : BackgroundService
{
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private readonly DatabaseBackupStore _store;
    private CancellationTokenSource _rescheduleCts = new();

    private static readonly TimeSpan MaxSleepSlice = TimeSpan.FromMinutes(30);
    private DateTime? _lastLoggedNextRun;
    private DateTime? _lastRun;

    public DatabaseBackupSchedulerService(
        ConfigManager configManager,
        WebsocketManager websocketManager,
        DatabaseBackupStore store)
    {
        _configManager = configManager;
        _websocketManager = websocketManager;
        _store = store;

        _configManager.OnConfigChanged += (_, args) =>
        {
            if (!args.ChangedConfig.ContainsKey(ConfigKeys.BackupScheduleEnabled) &&
                !args.ChangedConfig.ContainsKey(ConfigKeys.BackupScheduleTime))
                return;

            var old = Interlocked.Exchange(ref _rescheduleCts, new CancellationTokenSource());
            old.Cancel();
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _store.EnsureInitialized();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var reschedule = Volatile.Read(ref _rescheduleCts);

                if (!_configManager.IsDatabaseBackupScheduleEnabled())
                {
                    _lastLoggedNextRun = null;
                    using var disabledLinked = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken, reschedule.Token);
                    await Task.Delay(Timeout.Infinite, disabledLinked.Token).ConfigureAwait(false);
                    continue;
                }

                var scheduleTime = _configManager.DatabaseBackupSchedule();
                var now = DateTime.Now;
                var todayRun = now.Date + scheduleTime;
                var lastRun = _lastRun ?? DateTime.MinValue;
                var nextRun = todayRun > now && todayRun > lastRun ? todayRun : todayRun.AddDays(1);
                var delay = nextRun - now;

                if (_lastLoggedNextRun != nextRun)
                {
                    Log.Information("DatabaseBackupScheduler: next run scheduled at {NextRun}", nextRun);
                    _lastLoggedNextRun = nextRun;
                }

                using var delayLinked = CancellationTokenSource
                    .CreateLinkedTokenSource(stoppingToken, reschedule.Token);
                await Task.Delay(delay < MaxSleepSlice ? delay : MaxSleepSlice, delayLinked.Token)
                    .ConfigureAwait(false);

                if (DateTime.Now < nextRun) continue;

                Log.Information("DatabaseBackupScheduler: running scheduled database backup");
                var task = new DatabaseBackupTask(
                    _configManager,
                    _websocketManager,
                    _store,
                    DatabaseBackupKinds.Scheduled);
                await task.Execute().ConfigureAwait(false);
                _lastRun = nextRun;
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // Config changed — loop and recompute the next run time
            }
            catch (Exception e)
            {
                Log.Error(e, "DatabaseBackupScheduler: error running scheduled task: {Message}", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
