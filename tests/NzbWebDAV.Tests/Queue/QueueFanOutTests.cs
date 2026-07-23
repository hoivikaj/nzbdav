using System.Text.Json;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;

namespace NzbWebDAV.Tests.Queue;

public class QueueFanOutTests
{
    [Fact]
    public void PrimaryFanOut_MatchesHistoricalSingleItemBudget()
    {
        Assert.Equal(15, QueueFanOut.PrimaryFanOut(10));
        Assert.Equal(50, QueueFanOut.PrimaryFanOut(100));
        Assert.Equal(6, QueueFanOut.PrimaryFanOut(1));
    }

    [Theory]
    [InlineData(10, 1, 10)]
    [InlineData(10, 2, 5)]
    [InlineData(10, 3, 4)]
    [InlineData(1, 4, 1)]
    public void SecondaryFanOut_DividesBudgetAcrossSecondaries(
        int maxQueue, int secondaryCount, int expected)
    {
        Assert.Equal(expected, QueueFanOut.SecondaryFanOut(maxQueue, secondaryCount));
    }

    [Fact]
    public void GetConcurrency_WithoutContext_UsesPrimaryFanOut()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetProviders,
                ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig
                {
                    Providers =
                    [
                        new UsenetProviderConfig.ConnectionDetails
                        {
                            ProviderId = Guid.NewGuid(),
                            Type = NzbWebDAV.Models.ProviderType.Pooled,
                            Host = "nntp.example",
                            Port = 563,
                            UseSsl = true,
                            User = "u",
                            Pass = "p",
                            MaxConnections = 20,
                        },
                    ],
                }),
            },
            new ConfigItem { ConfigName = ConfigKeys.UsenetMaxQueueConnections, ConfigValue = "8" },
        ]);

        Assert.Equal(13, QueueFanOut.GetConcurrency(CancellationToken.None, config));
    }
}

public class QueueWorkerCountConfigTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData("8", 8)]
    [InlineData("0", 1)]
    [InlineData("99", 8)]
    [InlineData("abc", 1)]
    public void GetQueueWorkerCount_ClampsToOneThroughEight(string? configured, int expected)
    {
        var config = new ConfigManager();
        if (configured is not null)
        {
            config.UpdateValues(
            [
                new ConfigItem { ConfigName = ConfigKeys.QueueWorkerCount, ConfigValue = configured },
            ]);
        }

        Assert.Equal(expected, config.GetQueueWorkerCount());
    }
}
