namespace NzbWebDAV.Utils;

public static class DebounceUtil
{
    public static Action<Action> CreateDebounce(TimeSpan timespan)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timespan, TimeSpan.Zero);
        var synchronizationLock = new object();
        DateTime lastInvocationTime = default;
        var isFlushScheduled = false;
        Action? pendingAction = null;
        Timer? flushTimer = null;

        return actionToInvoke =>
        {
            Action? invokeNow = null;
            lock (synchronizationLock)
            {
                var now = DateTime.Now;
                var elapsed = now - lastInvocationTime;
                if (elapsed >= timespan && !isFlushScheduled)
                {
                    lastInvocationTime = now;
                    invokeNow = actionToInvoke;
                }
                else
                {
                    pendingAction = actionToInvoke;
                    if (!isFlushScheduled)
                    {
                        isFlushScheduled = true;
                        var delay = timespan - elapsed;
                        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                        flushTimer ??= new Timer(_ =>
                        {
                            Action? trailingAction;
                            lock (synchronizationLock)
                            {
                                isFlushScheduled = false;
                                lastInvocationTime = DateTime.Now;
                                trailingAction = pendingAction;
                                pendingAction = null;
                            }

                            trailingAction?.Invoke();
                        });
                        flushTimer.Change(delay, Timeout.InfiniteTimeSpan);
                    }
                }
            }

            invokeNow?.Invoke();
        };
    }

    public static Action<Action> RunOnlyOnce()
    {
        var isAlreadyRan = false;
        return actionToMaybeInvoke =>
        {
            if (isAlreadyRan) return;
            isAlreadyRan = true;
            actionToMaybeInvoke?.Invoke();
        };
    }
}
