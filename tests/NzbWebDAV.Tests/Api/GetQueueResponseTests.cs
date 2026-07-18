using System.Globalization;
using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Api;

public class GetQueueResponseTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(50, "50")]
    [InlineData(100, "100")]
    [InlineData(150, "100")]
    [InlineData(200, "100")]
    public void FromQueueItem_ClampsSabPercentage(int progress, string expected)
    {
        var slot = GetQueueResponse.QueueSlot.FromQueueItem(
            CreateQueueItem(),
            progressPercentage: progress);

        Assert.Equal(expected, slot.Percentage);
        Assert.Equal(progress.ToString(CultureInfo.InvariantCulture), slot.TruePercentage);
    }

    [Fact]
    public void FromQueueItem_ReportsNoBytesLeftAfterDownloadPhase()
    {
        var slot = GetQueueResponse.QueueSlot.FromQueueItem(
            CreateQueueItem(),
            progressPercentage: 150);

        Assert.Equal("0.00", slot.SizeLeftInMB);
    }

    private static QueueItem CreateQueueItem() =>
        new()
        {
            Id = Guid.NewGuid(),
            FileName = "release.nzb",
            JobName = "release",
            Category = "movies",
            TotalSegmentBytes = 2 * 1024 * 1024,
        };
}
