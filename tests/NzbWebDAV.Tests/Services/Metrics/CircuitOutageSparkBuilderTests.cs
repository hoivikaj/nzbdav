using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Tests.Services.Metrics;

public class CircuitOutageSparkBuilderTests
{
    private const long Minute = 60_000;

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
}
