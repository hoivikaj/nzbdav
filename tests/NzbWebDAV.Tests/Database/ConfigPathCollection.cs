namespace NzbWebDAV.Tests.Database;

/// <summary>
/// Serializes tests that mutate the process-wide <c>CONFIG_PATH</c> environment variable.
/// </summary>
[CollectionDefinition(nameof(ConfigPathCollection), DisableParallelization = true)]
public sealed class ConfigPathCollection;
