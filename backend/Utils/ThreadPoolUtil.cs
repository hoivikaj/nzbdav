namespace NzbWebDAV.Utils;

public static class ThreadPoolUtil
{
    public const int MinimumThreadCount = 2;
    public const int MaximumThreadCount = 32767;

    public static (int MinThreads, int MaxThreads) ResolveLimits(
        int processorCount,
        long? configuredMinThreads,
        long? configuredMaxThreads)
    {
        var effectiveProcessorCount = Math.Clamp(processorCount, 1, MaximumThreadCount);
        var defaultMinThreads = Math.Max((long)effectiveProcessorCount * 2, 50);
        var defaultMaxThreads = Math.Max((long)effectiveProcessorCount * 50, 1000);

        var minThreads = (int)Math.Clamp(
            configuredMinThreads ?? defaultMinThreads,
            MinimumThreadCount,
            MaximumThreadCount);
        var maxThreads = (int)Math.Clamp(
            configuredMaxThreads ?? defaultMaxThreads,
            MinimumThreadCount,
            MaximumThreadCount);

        // ThreadPool rejects a maximum below either its minimum or the processor count.
        maxThreads = Math.Max(maxThreads, Math.Max(minThreads, effectiveProcessorCount));

        return (minThreads, maxThreads);
    }
}
