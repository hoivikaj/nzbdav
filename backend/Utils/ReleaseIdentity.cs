using System.Security.Cryptography;
using System.Text;

namespace NzbWebDAV.Utils;

public static class ReleaseIdentity
{
    public static string Key(long size, string? poster, DateTimeOffset? usenetDate, string? nzbUrl)
    {
        var fingerprint = WardenFingerprint.Compute(size, poster, usenetDate);
        if (fingerprint is not null) return fingerprint;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(nzbUrl ?? string.Empty));
        return "rk1:" + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
