namespace NzbWebDAV.Api.Controllers.GetConfig;

public class GetConfigResponse : BaseApiResponse
{
    public List<ConfigItemDto> ConfigItems { get; init; } = new();
}
