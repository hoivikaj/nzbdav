using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Config;

public class HealthCheckDepthConfigTests
{
    [Theory]
    [InlineData("standard", ConfigManager.DefaultHealthCheckDepth)]
    [InlineData("enhanced", 1.0)]
    [InlineData("deep", 2.0)]
    [InlineData("complete", 0)]
    // Validation accepts any casing, so the getter has to resolve it the same way
    // rather than falling through to the default and quietly under-checking.
    [InlineData("Deep", 2.0)]
    [InlineData("COMPLETE", 0)]
    [InlineData("nonsense", ConfigManager.DefaultHealthCheckDepth)]
    public void GetHealthCheckDepth_ResolvesRegardlessOfCasing(string value, double expected)
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.RepairHealthcheckDepth,
                ConfigValue = value,
            },
        ]);

        Assert.Equal(expected, config.GetHealthCheckDepth());
    }

    [Theory]
    [InlineData("Deep")]
    [InlineData("COMPLETE")]
    [InlineData("standard")]
    public void ValidateConfigItems_AcceptsAnyCasingTheGetterResolves(string value)
    {
        var items = new[]
        {
            new ConfigItem { ConfigName = ConfigKeys.RepairHealthcheckDepth, ConfigValue = value },
        };

        ConfigManager.ValidateConfigItems(items);
    }

    [Fact]
    public void ValidateConfigItems_RejectsAnUnknownDepth()
    {
        var items = new[]
        {
            new ConfigItem { ConfigName = ConfigKeys.RepairHealthcheckDepth, ConfigValue = "thorough" },
        };

        Assert.Throws<ArgumentException>(() => ConfigManager.ValidateConfigItems(items));
    }
}
