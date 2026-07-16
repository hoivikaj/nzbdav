using System.Text.Json;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Models;

namespace NzbWebDAV.Tests.Config;

public class UsenetProviderValidationTests
{
    [Fact]
    public void ValidateConfigItems_AcceptsCleanProvider()
    {
        ConfigManager.ValidateConfigItems([MakeItem(MakeProvider())]);
    }

    [Fact]
    public void ValidateConfigItems_RejectsUsernameWithCrlf()
    {
        var provider = MakeProvider();
        provider.User = "user\r\nBODY <x@y>";

        var ex = Assert.Throws<ArgumentException>(() =>
            ConfigManager.ValidateConfigItems([MakeItem(provider)]));

        Assert.Contains("username contains control characters", ex.Message);
    }

    [Fact]
    public void ValidateConfigItems_RejectsPasswordWithNewline()
    {
        var provider = MakeProvider();
        provider.Pass = "secret\n";

        var ex = Assert.Throws<ArgumentException>(() =>
            ConfigManager.ValidateConfigItems([MakeItem(provider)]));

        Assert.Contains("password contains control characters", ex.Message);
    }

    [Fact]
    public void ValidateConfigItems_RejectsHostWithSpace()
    {
        var provider = MakeProvider();
        provider.Host = "bad host.example";

        var ex = Assert.Throws<ArgumentException>(() =>
            ConfigManager.ValidateConfigItems([MakeItem(provider)]));

        Assert.Contains("host contains whitespace or control characters", ex.Message);
    }

    [Fact]
    public void ValidateConfigItems_RejectsOversizedUsername()
    {
        var provider = MakeProvider();
        provider.User = new string('a', 401);

        var ex = Assert.Throws<ArgumentException>(() =>
            ConfigManager.ValidateConfigItems([MakeItem(provider)]));

        Assert.Contains("exceeds 400 characters", ex.Message);
    }

    private static ConfigItem MakeItem(UsenetProviderConfig.ConnectionDetails provider) =>
        new()
        {
            ConfigName = ConfigKeys.UsenetProviders,
            ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig
            {
                Providers = [provider],
            }),
        };

    private static UsenetProviderConfig.ConnectionDetails MakeProvider() =>
        new()
        {
            Type = ProviderType.Pooled,
            Host = "nntp.example",
            Port = 563,
            UseSsl = true,
            User = "user",
            Pass = "pass",
            MaxConnections = 1,
        };
}
