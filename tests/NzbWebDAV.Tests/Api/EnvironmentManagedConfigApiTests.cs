using System.Collections;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Api;

/// <summary>
/// Covers the GetConfig / UpdateConfig ENV-managed contract without spinning up
/// the full ASP.NET host: effective source metadata for reads, and the rejection
/// predicate used before any SQLite write.
/// </summary>
public class EnvironmentManagedConfigApiTests
{
    [Fact]
    public void GetConfigContract_ExposesEnvironmentVariableNameForManagedKeys()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.ApiCategories, ConfigValue = "sqlite-cats" },
        ]);
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__API__CATEGORIES"] = "env-cats",
        }));

        Assert.Equal("env-cats", config.GetEffectiveConfigValue(ConfigKeys.ApiCategories));
        Assert.Equal(
            "NZBDAV_CONFIG__API__CATEGORIES",
            config.GetEnvironmentVariableName(ConfigKeys.ApiCategories));
        Assert.Null(config.GetEnvironmentVariableName(ConfigKeys.WebdavUser));
    }

    [Fact]
    public void UpdateConfigContract_RejectsManagedKeysWithBadHttpRequestExceptionShape()
    {
        var config = new ConfigManager();
        config.ApplyEnvironmentOverlay(ConfigEnvironmentOverlay.LoadFromEnvironment(new Hashtable
        {
            ["NZBDAV_CONFIG__WEBDAV__USER"] = "env-user",
            ["NZBDAV_CONFIG__API__CATEGORIES"] = "tv",
        }));

        var requestItems = new List<ConfigItem>
        {
            new() { ConfigName = ConfigKeys.WebdavUser, ConfigValue = "attempted" },
            new() { ConfigName = ConfigKeys.ApiCategories, ConfigValue = "movies" },
            new() { ConfigName = ConfigKeys.WebdavShowHiddenFiles, ConfigValue = "true" },
        };

        var managed = requestItems
            .Where(item => config.IsEnvironmentManaged(item.ConfigName))
            .Select(item => item.ConfigName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [ConfigKeys.ApiCategories, ConfigKeys.WebdavUser],
            managed);

        var details = string.Join(", ", managed.Select(name =>
        {
            var envName = config.GetEnvironmentVariableName(name) ?? name;
            return $"`{name}` (managed by `{envName}`)";
        }));
        var message =
            $"Cannot update environment-managed setting(s): {details}. " +
            "Change the container environment and restart instead.";

        var ex = new BadHttpRequestException(message);
        Assert.Contains("NZBDAV_CONFIG__API__CATEGORIES", ex.Message);
        Assert.Contains("NZBDAV_CONFIG__WEBDAV__USER", ex.Message);
        Assert.DoesNotContain("attempted", ex.Message);
        Assert.DoesNotContain("movies", ex.Message);
    }
}
