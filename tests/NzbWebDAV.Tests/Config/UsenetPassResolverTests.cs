using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Models;

namespace NzbWebDAV.Tests.Config;

public class UsenetPassResolverTests
{
    [Fact]
    public void Resolve_ReturnsPlaintextUnchanged()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();

        var resolved = UsenetPassResolver.Resolve("typed-password", configManager);

        Assert.Equal("typed-password", resolved);
    }

    [Fact]
    public void Resolve_UnmasksStoredProviderPassword()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var stored = JsonSerializer.Serialize(new UsenetProviderConfig
        {
            Providers =
            [
                new UsenetProviderConfig.ConnectionDetails
                {
                    Type = ProviderType.Pooled,
                    Host = "news.example",
                    Port = 563,
                    UseSsl = true,
                    User = "user",
                    Pass = "stored-secret",
                    MaxConnections = 10,
                }
            ]
        });
        var configManager = new ConfigManager();
        configManager.UpdateValues(
        [
            new ConfigItem { ConfigName = "usenet.providers", ConfigValue = stored }
        ]);

        var masker = new ConfigSecretMasker("test-signing-key");
        var masked = masker.MaskForResponse("usenet.providers", stored);
        using var document = JsonDocument.Parse(masked);
        var token = document.RootElement
            .GetProperty("Providers")[0]
            .GetProperty("Pass")
            .GetString()!;

        var resolved = UsenetPassResolver.Resolve(token, configManager);

        Assert.Equal("stored-secret", resolved);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownMaskToken()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();
        configManager.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = "usenet.providers",
                ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig
                {
                    Providers =
                    [
                        new UsenetProviderConfig.ConnectionDetails
                        {
                            Type = ProviderType.Pooled,
                            Host = "news.example",
                            Port = 563,
                            UseSsl = true,
                            User = "user",
                            Pass = "stored-secret",
                            MaxConnections = 10,
                        }
                    ]
                })
            }
        ]);

        var forged = $"{ConfigSecretMasker.MaskPrefix}AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        Assert.Throws<BadHttpRequestException>(() =>
            UsenetPassResolver.Resolve(forged, configManager));
    }

    private static IDisposable TempEnv(string name, string value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new RestoreEnv(name, previous);
    }

    private sealed class RestoreEnv(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }
}
