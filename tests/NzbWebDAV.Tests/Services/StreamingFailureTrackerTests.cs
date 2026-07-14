using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Services;

public class StreamingFailureTrackerTests
{
    [Fact]
    public void RecordFailure_IncrementsCount()
    {
        var tracker = new StreamingFailureTracker();
        var id = Guid.NewGuid();

        Assert.Equal(1, tracker.RecordFailure(id));
        Assert.Equal(2, tracker.RecordFailure(id));
        Assert.Equal(3, tracker.RecordFailure(id));
        Assert.Equal(3, tracker.GetFailureCount(id));
    }

    [Fact]
    public void GetFailureCount_ReturnsZeroForUnknownItem()
    {
        var tracker = new StreamingFailureTracker();
        Assert.Equal(0, tracker.GetFailureCount(Guid.NewGuid()));
    }

    [Fact]
    public void ClearFailure_ResetsCount()
    {
        var tracker = new StreamingFailureTracker();
        var id = Guid.NewGuid();
        tracker.RecordFailure(id);
        tracker.RecordFailure(id);

        tracker.ClearFailure(id);

        Assert.Equal(0, tracker.GetFailureCount(id));
    }

    [Fact]
    public void RecordFailure_TracksItemsIndependently()
    {
        var tracker = new StreamingFailureTracker();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        tracker.RecordFailure(a);
        tracker.RecordFailure(a);
        tracker.RecordFailure(b);

        Assert.Equal(2, tracker.GetFailureCount(a));
        Assert.Equal(1, tracker.GetFailureCount(b));
    }
}
