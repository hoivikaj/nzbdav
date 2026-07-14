using NzbWebDAV.Utils;

namespace NzbWebDAV.Tasks;

public abstract class BaseTask
{
    protected abstract Task ExecuteInternal();

    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static Task? _runningTask;

    protected readonly CancellationToken CancellationToken = SigtermUtil.GetCancellationToken();

    public static bool IsRunning
    {
        get
        {
            var task = Volatile.Read(ref _runningTask);
            return task is { IsCompleted: false };
        }
    }

    public async Task<bool> Execute()
    {
        await Semaphore.WaitAsync(CancellationToken).ConfigureAwait(false);
        Task? task;
        try
        {
            // if the task is already running, return immediately.
            if (_runningTask is { IsCompleted: false })
                return false;

            // otherwise, run the task.
            _runningTask = Task.Run(ExecuteInternal, CancellationToken);
            task = _runningTask;
        }
        finally
        {
            Semaphore.Release();
        }

        // and wait for it to finish.
        await task.ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Clears the shared single-flight slot. Tests only.
    /// </summary>
    internal static async Task ResetRunningTaskForTestsAsync()
    {
        await Semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_runningTask is { IsCompleted: false })
                await _runningTask.ConfigureAwait(false);
            _runningTask = null;
        }
        finally
        {
            Semaphore.Release();
        }
    }
}
