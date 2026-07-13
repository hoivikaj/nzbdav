using NzbWebDAV.Config;

namespace NzbWebDAV.Tests.Config;

public class ProfileConfigFindByTokenTests
{
    [Fact]
    public void FindByToken_ReturnsMatchingProfile()
    {
        var config = new ProfileConfig
        {
            Profiles =
            [
                new ProfileConfig.Profile { Token = "aaaaaaaaaaaaaaaaaaaaaaaa", Name = "A" },
                new ProfileConfig.Profile { Token = "bbbbbbbbbbbbbbbbbbbbbbbb", Name = "B" },
            ],
        };

        var match = config.FindByToken("bbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.NotNull(match);
        Assert.Equal("B", match.Name);
    }

    [Fact]
    public void FindByToken_ReturnsNullOnMiss()
    {
        var config = new ProfileConfig
        {
            Profiles =
            [
                new ProfileConfig.Profile { Token = "aaaaaaaaaaaaaaaaaaaaaaaa", Name = "A" },
            ],
        };

        Assert.Null(config.FindByToken("cccccccccccccccccccccccc"));
    }

    [Fact]
    public void FindByToken_ReturnsFirstMatchWhenDuplicatesExist()
    {
        var config = new ProfileConfig
        {
            Profiles =
            [
                new ProfileConfig.Profile { Token = "dddddddddddddddddddddddd", Name = "First" },
                new ProfileConfig.Profile { Token = "dddddddddddddddddddddddd", Name = "Second" },
            ],
        };

        var match = config.FindByToken("dddddddddddddddddddddddd");

        Assert.NotNull(match);
        Assert.Equal("First", match.Name);
    }
}
