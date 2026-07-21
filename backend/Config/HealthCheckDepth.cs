namespace NzbWebDAV.Config;

/// <summary>
/// How much of each file a health check verifies. Standard through Deep scale the
/// sampling curve. Complete skips sampling and STATs every segment.
/// </summary>
public enum HealthCheckDepth
{
    Standard,
    Enhanced,
    Deep,
    Complete,
}
