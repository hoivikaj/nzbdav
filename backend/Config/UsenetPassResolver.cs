using System.Text.Json;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Config;

/// <summary>
/// Resolves a usenet provider password that may be a UI mask token back to the
/// stored plaintext, so ad-hoc endpoints (benchmark / test-connection) can auth
/// without forcing the user to re-type the password.
/// </summary>
public static class UsenetPassResolver
{
    public const string ProvidersConfigName = ConfigKeys.UsenetProviders;

    public static string Resolve(string submittedPass, ConfigManager configManager)
    {
        if (!ConfigSecretMasker.IsMaskToken(submittedPass))
            return submittedPass;

        var masker = new ConfigSecretMasker(
            EnvironmentUtil.GetRequiredVariable("FRONTEND_BACKEND_API_KEY"));
        var existingJson = JsonSerializer.Serialize(configManager.GetUsenetProviderConfig());
        return masker.ResolveMaskedJsonSecret(ProvidersConfigName, submittedPass, existingJson);
    }
}
