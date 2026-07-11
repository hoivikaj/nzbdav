using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

public class PreferredOrderStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void Report(string profileToken, string type, string contentId, string source, IReadOnlyList<string> orderedKeys)
    {
        if (string.IsNullOrWhiteSpace(profileToken) || string.IsNullOrWhiteSpace(contentId) || orderedKeys.Count == 0)
            return;
        Cleanup();
        _entries[Key(profileToken, type, contentId)] = new Entry(orderedKeys.ToList(), DateTime.UtcNow);
    }

    public IReadOnlyList<string>? GetOrder(string profileToken, string type, string contentId)
    {
        if (string.IsNullOrWhiteSpace(profileToken) || string.IsNullOrWhiteSpace(contentId))
            return null;
        if (_entries.TryGetValue(Key(profileToken, type, contentId), out var e) && DateTime.UtcNow - e.At <= Ttl)
            return e.Order;
        return null;
    }

    public static List<T> ApplyOrder<T>(IReadOnlyList<T> items, Func<T, string> keyOf, IReadOnlyList<string>? order)
    {
        if (order is null || order.Count == 0) return items.ToList();

        var rank = new Dictionary<string, int>(order.Count);
        for (var i = 0; i < order.Count; i++)
            rank.TryAdd(order[i], i);

        var matched = new List<(int Rank, int Orig, T Item)>();
        var unmatched = new List<T>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (rank.TryGetValue(keyOf(item), out var r)) matched.Add((r, i, item));
            else unmatched.Add(item);
        }

        matched.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Orig.CompareTo(b.Orig));

        var result = new List<T>(items.Count);
        result.AddRange(matched.Select(m => m.Item));
        result.AddRange(unmatched);
        return result;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var kv in _entries)
            if (kv.Value.At < cutoff)
                _entries.TryRemove(kv.Key, out _);
    }

    private static string Key(string profileToken, string type, string contentId) =>
        string.Join('\n', profileToken, type, contentId);

    private readonly record struct Entry(IReadOnlyList<string> Order, DateTime At);
}
