using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Tests.Services.Metrics;

public class CircuitOutageSparkBuilderTests
{
    private const long Minute = 60_000;
    private const long Hour = 60 * Minute;

    [Fact]
    public void Build_SplitsOpenIntervalAcrossBuckets()
    {
        var result = CircuitOutageSparkBuilder.Build(
            [
                new CircuitOutageSparkBuilder.Event(
                    At: 30_000,
                    Provider: "provider-a",
                    State: "open",
                    CooldownMs: Minute),
            ],
            ["provider-a"],
            sparkStart: 0,
            bucketSize: Minute,
            bucketCount: 3,
            nowMs: 3 * Minute);

        Assert.Equal([50, 50, 0], result["provider-a"]);
    }

    [Fact]
    public void Build_ClosesIntervalBeforeCooldownDeadline()
    {
        var result = CircuitOutageSparkBuilder.Build(
            [
                new CircuitOutageSparkBuilder.Event(0, "provider-a", "open", Minute),
                new CircuitOutageSparkBuilder.Event(15_000, "provider-a", "closed", null),
            ],
            ["provider-a"],
            sparkStart: 0,
            bucketSize: Minute,
            bucketCount: 1,
            nowMs: Minute);

        Assert.Equal([25], result["provider-a"]);
    }

    [Fact]
    public void Build_CapsUnclosedIntervalAtPersistedCooldown()
    {
        var result = CircuitOutageSparkBuilder.Build(
            [
                new CircuitOutageSparkBuilder.Event(0, "provider-a", "open", Minute),
            ],
            ["provider-a"],
            sparkStart: 0,
            bucketSize: Minute,
            bucketCount: 3,
            nowMs: 3 * Minute);

        Assert.Equal([100, 0, 0], result["provider-a"]);
    }

    [Fact]
    public void Build_ReturnsAlignedZerosForProvidersWithoutEvents()
    {
        var result = CircuitOutageSparkBuilder.Build(
            [],
            ["provider-a", "provider-b"],
            sparkStart: 0,
            bucketSize: Minute,
            bucketCount: 2,
            nowMs: Minute);

        Assert.Equal([0, 0], result["provider-a"]);
        Assert.Equal([0, 0], result["provider-b"]);
    }

    [Fact]
    public void Build_PreservesOnePercentSentinelForSubHalfPercentTrip()
    {
        // 200ms open in a 1h bucket ≈ 0.0056% → rounds to 0 without a sentinel.
        var result = CircuitOutageSparkBuilder.Build(
            [
                new CircuitOutageSparkBuilder.Event(0, "provider-a", "open", Hour),
                new CircuitOutageSparkBuilder.Event(200, "provider-a", "closed", null),
            ],
            ["provider-a"],
            sparkStart: 0,
            bucketSize: Hour,
            bucketCount: 1,
            nowMs: Hour);

        Assert.Equal([1], result["provider-a"]);
    }

    [Fact]
    public void Build_PreservesOnePercentSentinelForSameMillisecondOpenClosed()
    {
        var result = CircuitOutageSparkBuilder.Build(
            [
                new CircuitOutageSparkBuilder.Event(10_000, "provider-a", "open", Minute),
                new CircuitOutageSparkBuilder.Event(10_000, "provider-a", "closed", null),
            ],
            ["provider-a"],
            sparkStart: 0,
            bucketSize: Minute,
            bucketCount: 1,
            nowMs: Minute);

        Assert.Equal([1], result["provider-a"]);
    }

    [Fact]
    public void Build_KeepsCalculatedPercentForNormalIntervals()
    {
        var result = CircuitOutageSparkBuilder.Build(
            [
                new CircuitOutageSparkBuilder.Event(0, "provider-a", "open", Minute),
                new CircuitOutageSparkBuilder.Event(30_000, "provider-a", "closed", null),
            ],
            ["provider-a"],
            sparkStart: 0,
            bucketSize: Minute,
            bucketCount: 1,
            nowMs: Minute);

        Assert.Equal([50], result["provider-a"]);
    }
}
