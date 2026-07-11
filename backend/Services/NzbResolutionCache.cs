using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NzbWebDAV.Services;

public class NzbResolutionCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>
    /// Register a candidate group and return one token per candidate. Each token's entry
    /// points at the same ordered Candidates list with its own StartIndex, so the play
    /// handler can iterate from any starting position for fast-fail + fallback.
    /// </summary>
    public string[] AddGroup(IReadOnlyList<Candidate> candidates, string type, string profileToken, string id)
    {
        Cleanup();
        var tokens = new string[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var token = GenerateToken();
            _entries[token] = new Entry
            {
                Candidates = candidates,
                StartIndex = i,
                Type = type,
                ProfileToken = profileToken,
                Id = id,
                CreatedAt = DateTime.UtcNow,
            };
            tokens[i] = token;
        }
        return tokens;
    }

    public Entry? Get(string token) => _entries.TryGetValue(token, out var e) ? e : null;

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var kv in _entries)
            if (kv.Value.CreatedAt < cutoff)
                _entries.TryRemove(kv.Key, out _);
    }

    private static string GenerateToken()
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    public class Candidate
    {
        public required string IndexerName { get; init; }
        public required string IndexerUserAgent { get; init; }
        public required string NzbUrl { get; init; }
        public required string Title { get; init; }
        public long Size { get; init; }
        public DateTimeOffset? Posted { get; init; }
        public DateTimeOffset? UsenetDate { get; init; }
        public string? Poster { get; init; }
        public int? Grabs { get; init; }
        public int? Password { get; init; }
        public string? ProxyUrl { get; init; }
        public string? SourceIndexerName { get; init; }
        public string? Language { get; init; }
        public string? Subs { get; init; }
        public string? InfoHash { get; init; }
    }

    public class Entry
    {
        public required IReadOnlyList<Candidate> Candidates { get; init; }
        public required int StartIndex { get; init; }
        public required string Type { get; init; }
        public required string ProfileToken { get; init; }
        public required string Id { get; init; }
        public required DateTime CreatedAt { get; init; }

        public Candidate Primary => Candidates[StartIndex];
    }
}
