using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Services;

public class HealthCheckSampleSegmentsTests
{
    [Fact]
    public void SampleSegments_ReturnsUnchangedWhenAtOrBelowThreshold()
    {
        var segments = Enumerable.Range(0, 4000).Select(i => $"seg-{i}").ToList();

        var sampled = HealthCheckService.SampleSegments(segments);

        Assert.Same(segments, sampled);
        Assert.Equal(4000, sampled.Count);
    }

    [Fact]
    public void SampleSegments_ReturnsUnchangedForSmallFiles()
    {
        var segments = Enumerable.Range(0, 100).Select(i => $"seg-{i}").ToList();

        var sampled = HealthCheckService.SampleSegments(segments);

        Assert.Equal(100, sampled.Count);
        Assert.Equal(segments, sampled);
    }

    [Fact]
    public void SampleSegments_StratifiesLargeFilesAndPreservesOrder()
    {
        var segments = Enumerable.Range(0, 50_000).Select(i => $"seg-{i}").ToList();

        var sampled = HealthCheckService.SampleSegments(segments);

        Assert.True(sampled.Count < segments.Count);
        Assert.InRange(sampled.Count, 4000, 4500);
        Assert.Equal("seg-0", sampled[0]);
        Assert.Equal("seg-49999", sampled[^1]);
        Assert.Equal(sampled, sampled.OrderBy(s => int.Parse(s["seg-".Length..])).ToList());
    }

    [Fact]
    public void SampleSegments_IncludesHeadAndTail()
    {
        var segments = Enumerable.Range(0, 10_000).Select(i => $"seg-{i}").ToList();

        var sampled = HealthCheckService.SampleSegments(segments);
        var indices = sampled.Select(s => int.Parse(s["seg-".Length..])).ToHashSet();

        for (var i = 0; i < 100; i++)
            Assert.Contains(i, indices);
        for (var i = 9900; i < 10_000; i++)
            Assert.Contains(i, indices);
    }
}
