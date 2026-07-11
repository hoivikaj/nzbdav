using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Api.Controllers.GetProviderUsage;

[ApiController]
[Route("api/get-provider-usage")]
public class GetProviderUsageController(
    ConfigManager configManager,
    ProviderBytesTracker bytesTracker
) : BaseApiController
{
    private async Task<GetProviderUsageResponse> GetUsageAsync()
    {
        var providerConfig = configManager.GetUsenetProviderConfig();
        var recentHoursByHost = await ProviderUsageHelper
            .ReadRecentHoursAsync(providerConfig.Providers.Select(p => p.Host))
            .ConfigureAwait(false);

        var items = providerConfig.Providers
            .Select((provider, index) =>
            {
                var used = ProviderUsageHelper.ComputeUsage(bytesTracker, provider);
                recentHoursByHost.TryGetValue(provider.Host, out var recentHours);
                var (bytesPerDay, daysRemaining) = ProviderUsageHelper.ComputeBurnRate(provider, used, recentHours);
                return new GetProviderUsageResponse.ProviderUsageItem
                {
                    Index = index,
                    Host = provider.Host,
                    Nickname = provider.Nickname,
                    BytesUsed = used,
                    ByteLimit = provider.ByteLimit,
                    OverLimit = ProviderUsageHelper.IsOverLimit(bytesTracker, provider),
                    BytesPerDay = bytesPerDay,
                    DaysRemaining = daysRemaining,
                };
            })
            .ToList();

        return new GetProviderUsageResponse
        {
            Status = true,
            Providers = items,
        };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var response = await GetUsageAsync().ConfigureAwait(false);
        return Ok(response);
    }
}
