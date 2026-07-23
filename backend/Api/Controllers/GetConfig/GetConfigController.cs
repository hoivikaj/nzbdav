using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.Controllers.GetConfig;

[ApiController]
[Route("api/get-config")]
public class GetConfigController(DavDatabaseClient dbClient, ConfigManager configManager) : BaseApiController
{
    private async Task<GetConfigResponse> GetConfig(GetConfigRequest request)
    {
        var storedConfigItems = await dbClient.Ctx.ConfigItems
            .AsNoTracking()
            .Where(x => request.ConfigKeys.Contains(x.ConfigName))
            .ToListAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var storedByName = storedConfigItems.ToDictionary(x => x.ConfigName);

        var secretMasker = new ConfigSecretMasker(
            EnvironmentUtil.GetRequiredVariable("FRONTEND_BACKEND_API_KEY"));

        var configItems = new List<ConfigItemDto>();
        foreach (var key in request.ConfigKeys)
        {
            // Prefer the effective (ENV overlay) value so the Settings UI matches
            // what the process is running. Fall back to the stored SQLite row.
            var effectiveValue = configManager.GetEffectiveConfigValue(key)
                                 ?? storedByName.GetValueOrDefault(key)?.ConfigValue;
            if (effectiveValue is null) continue;

            configItems.Add(new ConfigItemDto
            {
                ConfigName = key,
                ConfigValue = secretMasker.MaskForResponse(key, effectiveValue),
                EnvironmentVariableName = configManager.GetEnvironmentVariableName(key),
            });
        }

        return new GetConfigResponse { ConfigItems = configItems };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetConfigRequest(HttpContext);
        var response = await GetConfig(request).ConfigureAwait(false);
        return Ok(response);
    }
}
