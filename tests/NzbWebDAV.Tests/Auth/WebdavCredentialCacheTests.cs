using NzbWebDAV.Auth;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Auth;

public class WebdavCredentialCacheTests
{
    [Fact]
    public void ClearVerifiedCredentials_RemovesCachedEntries()
    {
        const string key = "unit-test-credential-cache-key";
        ServiceCollectionAuthExtensions.SeedVerifiedCredential(key);
        Assert.True(ServiceCollectionAuthExtensions.HasCachedCredential(key));

        ServiceCollectionAuthExtensions.ClearVerifiedCredentials();

        Assert.False(ServiceCollectionAuthExtensions.HasCachedCredential(key));
    }

    [Fact]
    public void OnWebdavCredentialsChanged_ClearsCacheWhenPasswordChanges()
    {
        const string key = "unit-test-credential-cache-key-pass";
        ServiceCollectionAuthExtensions.SeedVerifiedCredential(key);
        Assert.True(ServiceCollectionAuthExtensions.HasCachedCredential(key));

        var config = new ConfigManager();
        config.OnConfigChanged += ServiceCollectionAuthExtensions.OnWebdavCredentialsChanged;
        config.UpdateValues([new ConfigItem { ConfigName = ConfigKeys.WebdavPass, ConfigValue = "new-hash" }]);

        Assert.False(ServiceCollectionAuthExtensions.HasCachedCredential(key));
    }

    [Fact]
    public void OnWebdavCredentialsChanged_IgnoresUnrelatedConfigChanges()
    {
        const string key = "unit-test-credential-cache-key-unrelated";
        ServiceCollectionAuthExtensions.SeedVerifiedCredential(key);

        var config = new ConfigManager();
        config.OnConfigChanged += ServiceCollectionAuthExtensions.OnWebdavCredentialsChanged;
        config.UpdateValues([new ConfigItem { ConfigName = ConfigKeys.UsenetMaxDownloadConnections, ConfigValue = "10" }]);

        Assert.True(ServiceCollectionAuthExtensions.HasCachedCredential(key));
        ServiceCollectionAuthExtensions.ClearVerifiedCredentials();
    }
}
