using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NzbWebDAV.Utils;

public static class SymlinkAndStrmUtil
{
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private const int MaxStderrChars = 4096;

    public static IEnumerable<ISymlinkOrStrmInfo> GetAllSymlinksAndStrms(string directoryPath)
    {
        return IsLinux
            ? GetAllSymlinksAndStrmsLinux(directoryPath)
            : GetAllSymlinksAndStrmsWindows(directoryPath);
    }

    private static IEnumerable<ISymlinkOrStrmInfo> GetAllSymlinksAndStrmsLinux(string directoryPath)
    {
        // find -exec (instead of piping to xargs) keeps traversal errors (permission
        // denied, etc.) in find's own exit code without `set -o pipefail`, which
        // Ubuntu's dash (/bin/sh) does not support.
        const string command =
            """
            find . \( -type l -o -name '*.strm' \) -exec sh -c '
              for path in \"$@\"; do
                echo \"$path\"
                if [ \"${path##*.}\" = \"strm\" ]; then
                  echo \"$(cat \"$path\")\"
                else
                  echo \"$(readlink \"$path\")\"
                fi
              done
            ' sh {} +
            """;

        var escapedDirectory = directoryPath.Replace("'", "'\"'\"'");
        var startInfo = new ProcessStartInfo
        {
            FileName = "sh",
            Arguments = $"-c \"cd '{escapedDirectory}' && {command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;

        // Drain stderr asynchronously. Leaving it unread can fill the OS pipe buffer and
        // deadlock find when a library tree produces many permission errors.
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
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

        while (process.StandardOutput.EndOfStream == false)
        {
            var filePath = process.StandardOutput.ReadLine();
            if (filePath == null) break;
            var target = process.StandardOutput.ReadLine();
            if (target == null) break;

            if (filePath.ToLower().EndsWith(".strm"))
            {
                yield return new StrmInfo()
                {
                    StrmPath = Path.GetFullPath(filePath, directoryPath),
                    TargetUrl = target
                };
            }
            else
            {
                yield return new SymlinkInfo()
                {
                    SymlinkPath = Path.GetFullPath(filePath, directoryPath),
                    TargetPath = target
                };
            }
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string stderr;
            lock (stderrBuilder)
                stderr = stderrBuilder.ToString();

            throw new InvalidOperationException(
                $"Library symlink scan failed with exit code {process.ExitCode}" +
                (string.IsNullOrWhiteSpace(stderr) ? "." : $": {stderr}"));
        }
    }

    private static IEnumerable<ISymlinkOrStrmInfo> GetAllSymlinksAndStrmsWindows(string directoryPath)
    {
        return Directory.EnumerateFileSystemEntries(directoryPath, "*", SearchOption.AllDirectories)
            .Select(x => new FileInfo(x))
            .Select(GetSymlinkOrStrmInfo)
            .Where(x => x != null)
            .Select(x => x!);
    }

    public static ISymlinkOrStrmInfo? GetSymlinkOrStrmInfo(FileInfo x)
    {
        return IsStrm(x) ? new StrmInfo() { StrmPath = x.FullName, TargetUrl = File.ReadAllText(x.FullName) }
            : IsSymLink(x) ? new SymlinkInfo() { SymlinkPath = x.FullName, TargetPath = x.LinkTarget! }
            : null;
    }

    private static bool IsStrm(FileInfo x) =>
        x.Extension.Equals(".strm", StringComparison.CurrentCultureIgnoreCase);

    private static bool IsSymLink(FileInfo x) =>
        x.Attributes.HasFlag(FileAttributes.ReparsePoint) && x.LinkTarget is not null;

    public interface ISymlinkOrStrmInfo;

    public struct SymlinkInfo : ISymlinkOrStrmInfo
    {
        public required string SymlinkPath;
        public required string TargetPath;
    }

    public struct StrmInfo : ISymlinkOrStrmInfo
    {
        public required string StrmPath;
        public required string TargetUrl;
    }
}
