using System.Text;
using Serilog;

namespace NzbWebDAV.Utils;

/// <summary>
/// Sanitizes Dav path components for Windows-invalid names. Uses an explicit
/// Windows character list — never <see cref="Path.GetInvalidFileNameChars()"/>
/// (host-OS dependent; Linux returns only '/' and NUL).
/// </summary>
public static class PathSanitizer
{
    private static readonly char[] WindowsInvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static volatile bool _windowsSafePathsEnabled = true;

    public static void SetWindowsSafePathsEnabled(bool enabled) =>
        _windowsSafePathsEnabled = enabled;

    public static bool IsWindowsSafePathsEnabled => _windowsSafePathsEnabled;

    public static string SanitizeComponent(string name, bool? windowsSafe = null)
    {
        var enabled = windowsSafe ?? _windowsSafePathsEnabled;
        if (string.IsNullOrEmpty(name))
            return "untitled";

        if (!enabled)
            return SanitizeMinimal(name);

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch < 0x20 || WindowsInvalidChars.Contains(ch))
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var sanitized = sb.ToString();

        // Windows silently strips trailing dots/spaces — trim all of them.
        sanitized = sanitized.TrimEnd('.', ' ');

        if (string.IsNullOrEmpty(sanitized))
            return "untitled";

        var extension = Path.GetExtension(sanitized);
        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (WindowsReservedNames.Contains(stem))
            sanitized = "_" + sanitized;

        if (sanitized.Length > 240)
        {
            if (extension.Length > 0 && extension.Length < 240)
            {
                var maxStem = 240 - extension.Length;
                sanitized = sanitized[..maxStem].TrimEnd('.', ' ') + extension;
            }
            else
            {
                sanitized = sanitized[..240].TrimEnd('.', ' ');
            }

            if (string.IsNullOrEmpty(sanitized))
                return "untitled";
        }

        return sanitized;
    }

    /// <summary>
    /// Minimal sanitization when Windows-safe paths are disabled: only '/' and NUL.
    /// </summary>
    private static string SanitizeMinimal(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch is '/' or '\0')
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var sanitized = sb.ToString();
        return string.IsNullOrEmpty(sanitized) ? "untitled" : sanitized;
    }

    public static string SanitizeComponentWithLog(string original)
    {
        var sanitized = SanitizeComponent(original);
        if (!string.Equals(original, sanitized, StringComparison.Ordinal))
        {
            Log.Information("Sanitized path component {Original} -> {Sanitized}", original, sanitized);
        }

        return sanitized;
    }
}
