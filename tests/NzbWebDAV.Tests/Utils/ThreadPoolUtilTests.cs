using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class ThreadPoolUtilTests
{
    [Theory]
    [InlineData(1, 50, 1000)]
    [InlineData(4, 50, 1000)]
    [InlineData(25, 50, 1250)]
    [InlineData(100, 200, 5000)]
    public void ResolveLimits_UsesCurrentDefaultsWhenUnset(
        int processorCount,
        int expectedMinThreads,
        int expectedMaxThreads)
    {
        var limits = ThreadPoolUtil.ResolveLimits(processorCount, null, null);

        Assert.Equal(expectedMinThreads, limits.MinThreads);
        Assert.Equal(expectedMaxThreads, limits.MaxThreads);
    }

    [Fact]
    public void ResolveLimits_UsesConfiguredValues()
    {
        var limits = ThreadPoolUtil.ResolveLimits(4, 25, 2000);

        Assert.Equal(25, limits.MinThreads);
        Assert.Equal(2000, limits.MaxThreads);
    }

    [Fact]
    public void ResolveLimits_UsesDefaultForEachUnsetValue()
    {
        var configuredMin = ThreadPoolUtil.ResolveLimits(4, 25, null);
        var configuredMax = ThreadPoolUtil.ResolveLimits(4, null, 2000);

        Assert.Equal((25, 1000), configuredMin);
        Assert.Equal((50, 2000), configuredMax);
    }

    [Theory]
    [InlineData(4, -1L, null, 2, 1000)]
    [InlineData(4, 100L, 10L, 100, 100)]
    [InlineData(8, 2L, 2L, 2, 8)]
    [InlineData(4, long.MaxValue, long.MaxValue, 32767, 32767)]
    [InlineData(100000, null, null, 32767, 32767)]
    public void ResolveLimits_ClampsValuesToValidBounds(
        int processorCount,
        long? configuredMinThreads,
        long? configuredMaxThreads,
        int expectedMinThreads,
        int expectedMaxThreads)
    {
        var limits = ThreadPoolUtil.ResolveLimits(
            processorCount,
            configuredMinThreads,
            configuredMaxThreads);

        Assert.Equal(expectedMinThreads, limits.MinThreads);
        Assert.Equal(expectedMaxThreads, limits.MaxThreads);
    }
}
