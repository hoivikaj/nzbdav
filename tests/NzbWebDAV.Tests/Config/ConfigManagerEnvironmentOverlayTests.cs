using System.Collections;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Config;

public class ConfigManagerEnvironmentOverlayTests
{
    [Fact]
    public void WithoutOverlay_BehaviorMatchesSqliteAndLegacyFallbacks()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.WebdavUser, ConfigValue = "sqlite-user" },
        ]);

        Assert.Equal("sqlite-user", config.GetWebdavUser());
        Assert.False(config.IsEnvironmentManaged(ConfigKeys.WebdavUser));
        Assert.Null(config.GetEnvironmentVariableName(ConfigKeys.WebdavUser));
        Assert.Equal("sqlite-user", config.GetPersistedConfigValue(ConfigKeys.WebdavUser));
    }

    [Fact]
    public void WithoutOverlay_LegacyEnvFallbackStillAppliesWhenSqliteEmpty()
    {
        var previous = Environment.GetEnvironmentVariable("WEBDAV_USER");
        try
        {
            Environment.SetEnvironmentVariable("WEBDAV_USER", "legacy-user");
            var config = new ConfigManager();
            Assert.Equal("legacy-user", config.GetWebdavUser());
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBDAV_USER", previous);
        }
    }

    [Fact]
    public void EnvOverlay_WinsOverSqliteAndLegacyFallbacks()
    {
        var previous = Environment.GetEnvironmentVariable("WEBDAV_USER");
        try
        {
            Environment.SetEnvironmentVariable("WEBDAV_USER", "legacy-user");
            var config = new ConfigManager();
            config.UpdateValues(
            [
                new ConfigItem { ConfigName = ConfigKeys.WebdavUser, ConfigValue = "sqlite-user" },
            ]);
            config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
            {
                ["NZBDAV_CONFIG__WEBDAV__USER"] = "env-user",
            }));

            Assert.Equal("env-user", config.GetWebdavUser());
            Assert.Equal("env-user", config.GetEffectiveConfigValue(ConfigKeys.WebdavUser));
            Assert.Equal("sqlite-user", config.GetPersistedConfigValue(ConfigKeys.WebdavUser));
            Assert.True(config.IsEnvironmentManaged(ConfigKeys.WebdavUser));
            Assert.Equal(
                "NZBDAV_CONFIG__WEBDAV__USER",
                config.GetEnvironmentVariableName(ConfigKeys.WebdavUser));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBDAV_USER", previous);
        }
    }

    [Fact]
    public void UpdateValues_DoesNotEmitChangeEventForEnvManagedKeys()
    {
        var config = new ConfigManager();
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__API__CATEGORIES"] = "tv",
            ["NZBDAV_CONFIG__WEBDAV__USER"] = "env-user",
        }));

        var changedKeys = new List<string>();
        config.OnConfigChanged += (_, args) =>
            changedKeys.AddRange(args.ChangedConfig.Keys);

        config.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.ApiCategories, ConfigValue = "movies" },
            new ConfigItem { ConfigName = ConfigKeys.WebdavShowHiddenFiles, ConfigValue = "true" },
        ]);

        Assert.Equal(["webdav.show-hidden-files"], changedKeys);
        Assert.Equal("tv", config.GetEffectiveConfigValue(ConfigKeys.ApiCategories));
        Assert.Equal("movies", config.GetPersistedConfigValue(ConfigKeys.ApiCategories));
        Assert.Equal("true", config.GetEffectiveConfigValue(ConfigKeys.WebdavShowHiddenFiles));
    }

    [Fact]
    public void UpdateValues_OnlyEnvManaged_DoesNotRaiseEvent()
    {
        var config = new ConfigManager();
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__API__CATEGORIES"] = "tv",
        }));

        var raised = false;
        config.OnConfigChanged += (_, _) => raised = true;

        config.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.ApiCategories, ConfigValue = "movies" },
        ]);

        Assert.False(raised);
    }

    [Fact]
    public void EmptyOverlay_LeavesRuntimeUnchanged()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.ApiCategories, ConfigValue = "audio" },
        ]);
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.Empty);

        Assert.Equal("audio", config.GetEffectiveConfigValue(ConfigKeys.ApiCategories));
        Assert.False(config.IsEnvironmentManaged(ConfigKeys.ApiCategories));
    }

    [Fact]
    public void EffectiveWebdavPasswordHash_UsesHashedEnvValue()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.WebdavPass,
                ConfigValue = PasswordUtil.Hash("sqlite-pass"),
            },
        ]);
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__WEBDAV__PASS"] = "env-pass",
        }));

        var hash = config.GetWebdavPasswordHash();
        Assert.NotNull(hash);
        Assert.True(PasswordUtil.Verify(hash!, "env-pass"));
        Assert.False(PasswordUtil.Verify(hash!, "sqlite-pass"));
    }

    [Fact]
    public void SecretMasker_MasksEffectiveEnvManagedIndexerSecrets()
    {
        const string envJson =
            """{"Indexers":[{"Name":"One","Url":"https://indexer.example","ApiKey":"env-indexer-secret"}]}""";
        var config = new ConfigManager();
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__INDEXERS__INSTANCES"] = envJson,
        }));

        var effective = config.GetEffectiveConfigValue(ConfigKeys.IndexersInstances)!;
        var masker = new ConfigSecretMasker("test-signing-key");
        var masked = masker.MaskForResponse(ConfigKeys.IndexersInstances, effective);

        Assert.DoesNotContain("env-indexer-secret", masked);
        Assert.Contains(ConfigSecretMasker.MaskPrefix, masked);
        Assert.Equal(
            "NZBDAV_CONFIG__INDEXERS__INSTANCES",
            config.GetEnvironmentVariableName(ConfigKeys.IndexersInstances));
    }
}
