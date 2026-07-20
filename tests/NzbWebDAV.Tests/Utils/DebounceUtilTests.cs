using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class DebounceUtilTests
{
    [Fact]
    public void CreateDebounce_LeadingEdgeThrow_DoesNotPropagate()
    {
        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(200));

        var exception = Record.Exception(() =>
            debounce(() => throw new InvalidOperationException("leading boom")));

        Assert.Null(exception);
    }

    [Fact]
    public async Task CreateDebounce_TrailingEdgeThrow_DoesNotPropagate_AndSubsequentActionsStillRun()
    {
        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(50));
        var subsequentRan = false;

        // First call runs immediately (leading edge).
        debounce(() => { });
        // Second call within the window is scheduled on the timer.
        debounce(() => throw new InvalidOperationException("trailing boom"));

        await Task.Delay(150);

        // After the window, a new leading-edge call should still work.
        debounce(() => subsequentRan = true);
        Assert.True(subsequentRan);
    }
}
