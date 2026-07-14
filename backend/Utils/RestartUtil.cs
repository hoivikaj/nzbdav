namespace NzbWebDAV.Utils;

/// <summary>
/// Exit codes used by the Docker/local restart loops when the backend needs to
/// re-enter the <c>--db-migration</c> maintenance phase.
/// </summary>
public static class RestartUtil
{
    /// <summary>
    /// Backend staged a database restore and needs the entrypoint to re-run
    /// maintenance so the swap can happen with WebDAV/SAB offline.
    /// </summary>
    public const int RestartForRestoreExitCode = 86;
}
