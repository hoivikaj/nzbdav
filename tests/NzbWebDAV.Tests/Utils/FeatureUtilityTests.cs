using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class FeatureUtilityTests
{
    [Fact]
    public void WardenFingerprint_NormalizesPosterAndBucketsDateByDay()
    {
        var first = WardenFingerprint.Compute(
            1_000,
            " Poster@Example.COM ",
            DateTimeOffset.Parse("2026-07-10T01:00:00Z"));
        var second = WardenFingerprint.Compute(
            1_000,
            "poster@example.com",
            DateTimeOffset.Parse("2026-07-10T23:59:59Z"));

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.StartsWith("wd1:", first);
    }

    [Theory]
    [InlineData("news.eu.example.co.uk:563", "example.co.uk")]
    [InlineData("secure.provider.com.", "provider.com")]
    [InlineData("127.0.0.1:119", "127.0.0.1")]
    public void WardenFingerprint_RootDomainHandlesHosts(string host, string expected)
    {
        Assert.Equal(expected, WardenFingerprint.RootDomain(host));
    }

    [Theory]
    [InlineData("Amélie", "Amelie.2001.1080p", true)]
    [InlineData("The Bridge", "The.Bridge.S02E03.2160p", true)]
    [InlineData("The Bridge", "Different.Show.S02E03", false)]
    public void FilenameMatcher_MatchesNormalizedTitleHeads(
        string query,
        string candidate,
        bool expected)
    {
        Assert.Equal(expected, FilenameMatcher.Matches(query, candidate));
    }

    [Theory]
    [InlineData("Show.S02E03-E05.1080p", 2, 4, true)]
    [InlineData("Show.S02E03-E05.1080p", 2, 6, false)]
    [InlineData("Show.1x09.720p", 1, 9, true)]
    public void FilenameMatcher_ValidatesEpisodeRanges(
        string title,
        int season,
        int episode,
        bool expected)
    {
        Assert.Equal(expected, FilenameMatcher.EpisodeCompatible(title, season, episode));
    }
}
