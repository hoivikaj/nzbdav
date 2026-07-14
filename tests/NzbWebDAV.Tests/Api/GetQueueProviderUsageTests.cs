using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Api;

public class GetQueueProviderUsageTests
{
    [Fact]
    public void GetProviderUsageForSlot_ReturnsEmptyWhenNotInProgress()
    {
        var tracker = new ProviderUsageTracker();
        var id = Guid.NewGuid();
        using (tracker.BeginScope(id))
            tracker.RecordSuccess("news.example");

        var usage = GetQueueController.GetProviderUsageForSlot(
            isInProgress: false, id, tracker);

        Assert.Empty(usage);
        // Tracker still holds the recorded data — we simply did not snapshot it.
        Assert.Equal(1, tracker.Snapshot(id)["news.example"]);
    }

    [Fact]
    public void GetProviderUsageForSlot_ReturnsSnapshotWhenInProgress()
    {
        var tracker = new ProviderUsageTracker();
        var id = Guid.NewGuid();
        using (tracker.BeginScope(id))
            tracker.RecordSuccess("news.example");

        var usage = GetQueueController.GetProviderUsageForSlot(
            isInProgress: true, id, tracker);

        Assert.Equal(1, usage["news.example"]);
    }
}
