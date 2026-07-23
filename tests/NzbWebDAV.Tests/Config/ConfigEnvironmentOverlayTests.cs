using System.Collections;
using System.Text.Json;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Config;

public class ConfigEnvironmentOverlayTests
{
    [Fact]
    public void LoadFromEnvironment_WithNoPrefixedVars_ReturnsEmpty()
    {
        var overlay = ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["CONFIG_PATH"] = "/tmp",
            ["WEBDAV_USER"] = "legacy",
        });

        Assert.True(overlay.IsEmpty);
        Assert.Equal(0, overlay.Count);
    }

    [Fact]
    public void LoadFromEnvironment_TreatsEmptyValuesAsUnset()
    {
        var overlay = ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__WEBDAV__USER"] = "",
            ["NZBDAV_CONFIG__API__CATEGORIES"] = "tv,movies",
        });

        Assert.False(overlay.IsManaged(ConfigKeys.WebdavUser));
        Assert.True(overlay.TryGetValue(ConfigKeys.ApiCategories, out var categories));
        Assert.Equal("tv,movies", categories);
    }

    [Fact]
    public void LoadFromEnvironment_RejectsUnknownPrefixedVariable()
    {
        var ex = Assert.Throws<ConfigEnvironmentException>(() =>
            ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
            {
                ["NZBDAV_CONFIG__NOT__A__REAL__KEY"] = "x",
            }));

        Assert.Contains("NZBDAV_CONFIG__NOT__A__REAL__KEY", ex.Message);
        Assert.DoesNotContain("x", ex.Message);
    }

    [Fact]
    public void LoadFromEnvironment_RejectsInvalidValuesWithoutLeakingSecrets()
    {
        var secret = "super-secret-password-value";
        var ex = Assert.Throws<ConfigEnvironmentException>(() =>
            ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
            {
                ["NZBDAV_CONFIG__REPAIR__HEALTHCHECK_CONCURRENCY"] = secret,
            }));

        Assert.Equal(
            "Invalid configuration environment variable `NZBDAV_CONFIG__REPAIR__HEALTHCHECK_CONCURRENCY`.",
            ex.Message);
        Assert.DoesNotContain(secret, ex.Message);
    }

    [Fact]
    public void LoadFromEnvironment_HashesWebdavPassword()
    {
        var plaintext = "plain-webdav-pass";
        var overlay = ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__WEBDAV__PASS"] = plaintext,
        });

        Assert.True(overlay.TryGetValue(ConfigKeys.WebdavPass, out var hashed));
        Assert.NotEqual(plaintext, hashed);
        Assert.True(PasswordUtil.Verify(hashed, plaintext));
        Assert.Equal(
            "NZBDAV_CONFIG__WEBDAV__PASS",
            overlay.GetEnvironmentVariableName(ConfigKeys.WebdavPass));
    }

    [Fact]
    public void LoadFromEnvironment_PreservesExistingUsenetProviderIds()
    {
        var existingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var existing = JsonSerializer.Serialize(new UsenetProviderConfig
        {
            Providers =
            [
                new UsenetProviderConfig.ConnectionDetails
                {
                    ProviderId = existingId,
                    Type = ProviderType.Pooled,
                    Host = "news.example",
                    Port = 563,
                    UseSsl = true,
                    User = "alice",
                    Pass = "stored",
                    MaxConnections = 10,
                },
            ],
        });

        var incoming = JsonSerializer.Serialize(new UsenetProviderConfig
        {
            Providers =
            [
                new UsenetProviderConfig.ConnectionDetails
                {
                    Type = ProviderType.Pooled,
                    Host = "news.example",
                    Port = 563,
                    UseSsl = true,
                    User = "alice",
                    Pass = "env-secret",
                    MaxConnections = 20,
                },
            ],
        });

        var overlay = ConfigEnvironmentOverlay.LoadFromEnvironment(
            new Hashtable { ["NZBDAV_CONFIG__USENET__PROVIDERS"] = incoming },
            existingUsenetProvidersJson: existing);

        Assert.True(overlay.TryGetValue(ConfigKeys.UsenetProviders, out var normalized));
        var parsed = JsonSerializer.Deserialize<UsenetProviderConfig>(normalized)!;
        Assert.Single(parsed.Providers);
        Assert.Equal(existingId, parsed.Providers[0].ProviderId);
        Assert.Equal(20, parsed.Providers[0].MaxConnections);
        Assert.Equal("env-secret", parsed.Providers[0].Pass);
    }

    [Fact]
    public void LoadFromEnvironment_AssignsProviderIdsWhenMissingEverywhere()
    {
        var incoming = JsonSerializer.Serialize(new UsenetProviderConfig
        {
            Providers =
            [
                new UsenetProviderConfig.ConnectionDetails
                {
                    Type = ProviderType.Pooled,
                    Host = "news.example",
                    Port = 563,
                    UseSsl = true,
                    User = "alice",
                    Pass = "env-secret",
                    MaxConnections = 5,
                },
            ],
        });

        var overlay = ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__USENET__PROVIDERS"] = incoming,
        });

        Assert.True(overlay.TryGetValue(ConfigKeys.UsenetProviders, out var normalized));
        var parsed = JsonSerializer.Deserialize<UsenetProviderConfig>(normalized)!;
        Assert.NotEqual(Guid.Empty, parsed.Providers[0].ProviderId);
    }
}
