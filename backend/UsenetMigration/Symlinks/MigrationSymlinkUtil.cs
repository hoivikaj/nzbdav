using System.Diagnostics;
using System.Text;

namespace NzbWebDAV.UsenetMigration.Symlinks;

/// <summary>
/// Migration-only library traversal. This is intentionally isolated from the
/// shared symlink/STRM walker used by orphan cleanup and maintenance tasks.
/// </summary>
internal static class MigrationSymlinkUtil
{
    private const int MaxStderrChars = 4096;

    internal static IReadOnlyList<SymlinkPair> GetAllSymlinks(string directoryPath)
    {
        return OperatingSystem.IsLinux()
            ? GetAllSymlinksLinux(directoryPath)
            : GetAllSymlinksManaged(directoryPath);
    }

    private static IReadOnlyList<SymlinkPair> GetAllSymlinksLinux(string directoryPath)
    {
        var startInfo = CreateLinuxFindStartInfo(directoryPath);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start migration symlink scan.");

        // Drain stderr while stdout is consumed so a large number of traversal
        // errors cannot fill the process pipe and deadlock the scan.
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrBuilder)
            {
                if (stderrBuilder.Length >= MaxStderrChars) return;
                if (stderrBuilder.Length > 0)
                    stderrBuilder.Append('\n');

                var remaining = MaxStderrChars - stderrBuilder.Length;
                stderrBuilder.Append(e.Data.Length <= remaining ? e.Data : e.Data[..remaining]);
            }
        };
        process.BeginErrorReadLine();

        // Materialize the complete result before returning it. If find fails,
        // callers never receive a partial migration plan.
        var symlinks = new List<SymlinkPair>();
        while (ReadNullTerminated(process.StandardOutput) is { } symlinkPath)
        {
            var fullPath = Path.GetFullPath(symlinkPath);
            var target = new FileInfo(fullPath).LinkTarget;
            if (target is not null)
                symlinks.Add(new SymlinkPair(fullPath, target));
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string stderr;
            lock (stderrBuilder)
                stderr = stderrBuilder.ToString();

            throw new InvalidOperationException(
                $"Migration symlink scan failed with exit code {process.ExitCode}" +
                (string.IsNullOrWhiteSpace(stderr) ? "." : $": {stderr}"));
        }

        return symlinks;
    }

    /// <summary>
    /// Builds the Linux traversal process without a command shell. The selected
    /// root is passed to find as one opaque argv value.
    /// </summary>
    internal static ProcessStartInfo CreateLinuxFindStartInfo(string directoryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "find",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -H permits a symlink supplied as the root while still avoiding symlink
        // traversal below that root. SymlinkPathGuard rejects such roots in the
        // production planner, but retaining the behavior keeps this helper robust.
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add(Path.GetFullPath(directoryPath));
        startInfo.ArgumentList.Add("-type");
        startInfo.ArgumentList.Add("l");
        startInfo.ArgumentList.Add("-print0");
        return startInfo;
    }

    private static IReadOnlyList<SymlinkPair> GetAllSymlinksManaged(string directoryPath)
    {
        return Directory.EnumerateFileSystemEntries(directoryPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info => info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null)
            .Select(info => new SymlinkPair(info.FullName, info.LinkTarget!))
            .ToList();
    }

    private static string? ReadNullTerminated(StreamReader reader)
    {
        var value = new StringBuilder();
        while (true)
        {
            var next = reader.Read();
            if (next < 0)
                return value.Length == 0 ? null : value.ToString();
            if (next == '\0')
                return value.ToString();
            value.Append((char)next);
        }
    }
}
