namespace NzbWebDAV.Tests.TestUtils;

/// <summary>
/// Tests that swap the global <c>Log.Logger</c> to capture output belong to this
/// collection. xUnit runs collections in parallel, so without it two such classes
/// race on the same static and observe each other's events.
/// </summary>
[CollectionDefinition(nameof(GlobalLoggerCollection))]
public class GlobalLoggerCollection;
