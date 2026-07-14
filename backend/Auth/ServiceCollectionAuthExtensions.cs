using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NWebDav.Server;
using NWebDav.Server.Authentication;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Auth;

public static class ServiceCollectionAuthExtensions
{
    private static readonly byte[] CredentialCacheKey = RandomNumberGenerator.GetBytes(32);
    private static readonly MemoryCache VerifiedCredentials = new(new MemoryCacheOptions
    {
        SizeLimit = 16
    });

    public static IServiceCollection AddWebdavBasicAuthentication
    (
        this IServiceCollection services,
        ConfigManager configManager
    )
    {
        // no-op when webdav auth is disabled
        if (WebApplicationAuthExtensions.IsWebdavAuthDisabled())
            return services;

        // Invalidate in-memory credential cache when WebDAV credentials change.
        // Basic-auth clients resend Authorization each request; the memory cache
        // (HMAC-keyed including password hash) already makes stale entries unreachable,
        // but clearing keeps the invariant explicit and frees memory.
        configManager.OnConfigChanged += OnWebdavCredentialsChanged;

        // otherwise configure basic auth
        services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Join(DavDatabaseContext.ConfigPath, "data-protection")));
        services
            .AddAuthentication(opts => opts.DefaultScheme = BasicAuthenticationDefaults.AuthenticationScheme)
            .AddBasicAuthentication(opts =>
            {
                opts.AllowInsecureProtocol = true;
                // Disable NWebDav's cookie-based auth cache. Once issued, that cookie
                // authenticates without re-running OnValidateCredentials until expiry,
                // so password changes would otherwise leave a residual session window.
                // The VerifiedCredentials memory cache already skips PBKDF2 on repeats.
                opts.CacheCookieName = string.Empty;
                opts.CacheCookieExpiration = TimeSpan.Zero;
                opts.Events.OnValidateCredentials = (ValidateCredentialsContext context) =>
                    ValidateCredentials(context, configManager);
            });

        return services;
    }

    internal static void OnWebdavCredentialsChanged(object? sender, ConfigManager.ConfigEventArgs e)
    {
        if (e.ChangedConfig.ContainsKey(ConfigKeys.WebdavUser)
            || e.ChangedConfig.ContainsKey(ConfigKeys.WebdavPass))
        {
            ClearVerifiedCredentials();
        }
    }

    internal static void ClearVerifiedCredentials() => VerifiedCredentials.Clear();

    internal static bool HasCachedCredential(string cacheKey) =>
        VerifiedCredentials.TryGetValue(cacheKey, out _);

    /// <summary>
    /// Test helper: insert a fake verified-credential entry for a known cache key.
    /// </summary>
    internal static void SeedVerifiedCredential(string cacheKey)
    {
        VerifiedCredentials.Set(cacheKey, true, new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(5)));
    }

    private static Task ValidateCredentials(ValidateCredentialsContext context, ConfigManager configManager)
    {
        var user = configManager.GetWebdavUser();
        var passwordHash = configManager.GetWebdavPasswordHash();

        if (user == null || passwordHash == null)
        {
            context.Fail("webdav user and password are not yet configured.");
            return Task.CompletedTask;
        }

        // Always run hash verification (dummy when username mismatches) so timing
        // does not enumerate the configured WebDAV username.
        var usernameMatches = context.Username.FixedTimeEquals(user);
        var passwordOk = usernameMatches
            ? VerifyPasswordWithCache(context.Username, context.Password, passwordHash)
            : RunDummyPasswordVerify(context.Password);

        if (usernameMatches && passwordOk)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, context.Username, ClaimValueTypes.String,
                    context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, context.Username, ClaimValueTypes.String,
                    context.Options.ClaimsIssuer)
            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            context.Success();
        }
        else
        {
            context.Fail("invalid credentials");
        }

        return Task.CompletedTask;
    }

    private static bool VerifyPasswordWithCache(string username, string password, string passwordHash)
    {
        var credentialBytes = Encoding.UTF8.GetBytes(
            string.Concat(username, "\0", passwordHash, "\0", password));
        try
        {
            var cacheKey = Convert.ToHexString(
                HMACSHA256.HashData(CredentialCacheKey, credentialBytes));
            if (VerifiedCredentials.TryGetValue(cacheKey, out _)) return true;
            if (!PasswordUtil.Verify(passwordHash, password)) return false;

            VerifiedCredentials.Set(cacheKey, true, new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetSlidingExpiration(TimeSpan.FromMinutes(5)));
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
        }
    }

    private static bool RunDummyPasswordVerify(string password)
    {
        PasswordUtil.VerifyDummy(password);
        return false;
    }
}
