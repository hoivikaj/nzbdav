namespace NzbWebDAV.Api.Controllers.GetConfig;

public class ConfigItemDto
{
    public string ConfigName { get; init; } = null!;
    public string ConfigValue { get; init; } = null!;

    /// <summary>
    /// When set, this setting is managed by an authoritative
    /// <c>NZBDAV_CONFIG__...</c> environment variable and is read-only in the UI.
    /// </summary>
    public string? EnvironmentVariableName { get; init; }
}
