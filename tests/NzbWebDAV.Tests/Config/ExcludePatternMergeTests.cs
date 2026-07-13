using System.Text.Json;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Models;

namespace NzbWebDAV.Tests.Config;

public class ExcludePatternMergeTests
{
    [Fact]
    public void GetSearchExcludePatterns_SyncedFirstThenManual_DropsExactDupes()
    {
        var url = "https://example.com/list.json";
        var cache = new ExcludeSyncCache
        {
            Urls =
            {
                [url] = new ExcludeSyncUrlEntry
                {
                    Items = [@"\.iso$", @"\.img$"],
                    FetchedAt = 1,
                    LastChecked = 1,
                }
            }
        };

        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem { ConfigName = "search.exclude-sync-urls", ConfigValue = url },
            new ConfigItem { ConfigName = "search.exclude-sync-cache", ConfigValue = JsonSerializer.Serialize(cache) },
            new ConfigItem { ConfigName = "search.exclude-patterns", ConfigValue = "\\.iso$\n\\.mkv$" },
        ]);

        var patterns = config.GetSearchExcludePatterns();
        Assert.Equal(3, patterns.Count);
        Assert.Matches(patterns[0], "a.ISO");
        Assert.Matches(patterns[1], "a.img");
        Assert.Matches(patterns[2], "a.mkv");
        Assert.DoesNotMatch(patterns[2], "a.iso");
    }

    [Fact]
    public void GetSearchExcludePatterns_ReturnsCachedInstanceUntilInvalidate()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem { ConfigName = "search.exclude-patterns", ConfigValue = "\\.iso$" },
        ]);

        var first = config.GetSearchExcludePatterns();
        var second = config.GetSearchExcludePatterns();
        Assert.Same(first, second);

        config.UpdateValues(
        [
            new ConfigItem { ConfigName = "search.exclude-patterns", ConfigValue = "\\.mkv$" },
        ]);
        var third = config.GetSearchExcludePatterns();
        Assert.NotSame(first, third);
        Assert.Single(third);
        Assert.Matches(third[0], "x.mkv");
    }
}
