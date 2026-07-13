using System.Text.Json;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Models;

namespace NzbWebDAV.Tests.Config;

public class MaxDownloadConnectionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-3")]
    public void GetMaxDownloadConnections_AutoUsesPooledTotal(string? configured)
    {
        var config = CreateConfig(configured, perStream: null, preset: null, pooled: 20, backup: 50);
        Assert.Equal(20, config.GetMaxDownloadConnections());
    }

    [Fact]
    public void GetMaxDownloadConnections_ManualOverride()
    {
        var config = CreateConfig("7", perStream: null, preset: null, pooled: 20, backup: 50);
        Assert.Equal(7, config.GetMaxDownloadConnections());
    }

    [Fact]
    public void IsMaxDownloadConnectionsPerStream_DefaultsFalse()
    {
        var config = CreateConfig("0", perStream: null, preset: null, pooled: 20, backup: 50);
        Assert.False(config.IsMaxDownloadConnectionsPerStream());
    }

    [Fact]
    public void IsMaxDownloadConnectionsPerStream_TrueWhenEnabled()
    {
        var config = CreateConfig("0", perStream: "true", preset: null, pooled: 20, backup: 50);
        Assert.True(config.IsMaxDownloadConnectionsPerStream());
    }

    [Theory]
    [InlineData(null, 15)]
    [InlineData("high", 15)]
    [InlineData("low", 5)]
    [InlineData("medium", 10)]
    [InlineData("max", 20)]
    public void GetMaxDownloadConnectionsPerStreamCount_AppliesPresetFraction(string? preset, int expected)
    {
        var config = CreateConfig("0", perStream: "true", preset: preset, pooled: 20, backup: 50);
        Assert.Equal(expected, config.GetMaxDownloadConnectionsPerStreamCount());
    }

    [Fact]
    public void GetMaxDownloadConnectionsPerStreamCount_FloorsAtOne()
    {
        var config = CreateConfig("0", perStream: "true", preset: "low", pooled: 1, backup: 50);
        Assert.Equal(1, config.GetMaxDownloadConnectionsPerStreamCount());
    }

    private static ConfigManager CreateConfig(
        string? maxConnections,
        string? perStream,
        string? preset,
        int pooled,
        int backup)
    {
        var providers = JsonSerializer.Serialize(new UsenetProviderConfig
        {
            Providers =
            [
                new UsenetProviderConfig.ConnectionDetails
                {
                    Type = ProviderType.Pooled,
                    Host = "pool.example",
                    Port = 563,
                    UseSsl = true,
                    User = "u",
                    Pass = "p",
                    MaxConnections = pooled,
                },
                new UsenetProviderConfig.ConnectionDetails
                {
                    Type = ProviderType.BackupOnly,
                    Host = "backup.example",
                    Port = 563,
                    UseSsl = true,
                    User = "u",
                    Pass = "p",
                    MaxConnections = backup,
                },
            ]
        });

        var items = new List<ConfigItem>
        {
            new() { ConfigName = "usenet.providers", ConfigValue = providers },
        };
        if (maxConnections is not null)
            items.Add(new ConfigItem { ConfigName = "usenet.max-download-connections", ConfigValue = maxConnections });
        if (perStream is not null)
            items.Add(new ConfigItem { ConfigName = "usenet.max-download-connections-per-stream", ConfigValue = perStream });
        if (preset is not null)
            items.Add(new ConfigItem { ConfigName = "usenet.max-download-connections-per-stream-preset", ConfigValue = preset });

        var config = new ConfigManager();
        config.UpdateValues(items);
        return config;
    }
}
