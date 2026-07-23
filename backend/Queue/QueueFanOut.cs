using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Queue;

/// <summary>
/// Resolves per-phase NNTP task fan-out for queue processing.
/// </summary>
public static class QueueFanOut
{
    /// <summary>
    /// Fan-out used by a lone / primary queue item today:
    /// <c>min(maxQueueConnections + 5, 50)</c>.
    /// </summary>
    public static int PrimaryFanOut(int maxQueueConnections) =>
        Math.Min(maxQueueConnections + 5, 50);

    /// <summary>
    /// Secondary workers collectively borrow the full queue budget when the
    /// primary has no demand: <c>max(1, ceil(maxQueue / secondaryCount))</c>.
    /// </summary>
    public static int SecondaryFanOut(int maxQueueConnections, int activeSecondaryCount) =>
        Math.Max(1, (int)Math.Ceiling(maxQueueConnections / (double)Math.Max(1, activeSecondaryCount)));

    /// <summary>
    /// Reads <see cref="QueueDownloadContext"/> from <paramref name="ct"/> when present;
    /// otherwise falls back to primary fan-out (single-item / test paths).
    /// </summary>
    public static int GetConcurrency(CancellationToken ct, ConfigManager configManager)
    {
        var ctx = ct.GetContext<QueueDownloadContext>();
        if (ctx is not null)
            return Math.Max(1, ctx.GetFanOutConcurrency());
        return PrimaryFanOut(configManager.GetMaxQueueConnections());
    }

    /// <summary>
    /// Fan-out for SevenZip size population (historically uncapped +5).
    /// </summary>
    public static int GetExactQueueConcurrency(CancellationToken ct, ConfigManager configManager)
    {
        var ctx = ct.GetContext<QueueDownloadContext>();
        if (ctx is not null)
            return Math.Max(1, ctx.GetFanOutConcurrency());
        return Math.Max(1, configManager.GetMaxQueueConnections());
    }
}
