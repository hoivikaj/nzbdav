using NzbWebDAV.Config;
using NzbWebDAV.Queue.PostProcessors;

namespace NzbWebDAV.Tests.Config;

public class BlocklistDefaultTests
{
    [Theory]
    [InlineData("thumbnail.jpg")]
    [InlineData("Movie.2020.1080p.BluRay.x264-GRP.nfo")]
    [InlineData("Movie.2020.1080p.BluRay.x264-GRP.par2")]
    public void DefaultBlocklist_BlocksNonMediaExtras(string filename)
    {
        var patterns = new ConfigManager().GetBlocklistedFiles();

        Assert.True(BlocklistedFilePostProcessor.MatchesAnyPattern(filename, patterns));
    }

    [Theory]
    [InlineData("Movie.2020.1080p.BluRay.x264-GRP.mkv")]
    [InlineData("Show.S01E01.mp4")]
    public void DefaultBlocklist_KeepsMedia(string filename)
    {
        var patterns = new ConfigManager().GetBlocklistedFiles();

        Assert.False(BlocklistedFilePostProcessor.MatchesAnyPattern(filename, patterns));
    }
}
