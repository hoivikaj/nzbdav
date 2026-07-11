using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.Warden;

[ApiController]
[Route("api/get-warden")]
public class GetWardenController(WardenStore warden) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        return Task.FromResult<IActionResult>(Ok(new GetWardenResponse
        {
            Status = true,
            Count = warden.Count,
        }));
    }
}

public class GetWardenResponse : BaseApiResponse
{
    [JsonPropertyName("count")] public required int Count { get; init; }
}
