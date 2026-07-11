using NzbWebDAV.Clients.Indexers;
using NzbWebDAV.Config;

namespace NzbWebDAV.Services;

/// <summary>
/// Applies the per-indexer <see cref="IndexerConfig.ResultFilter"/> rules to a single indexer's
/// search results before they're merged with results from other indexers.
///
/// All rules are opt-in. When the filter is null or its master toggle is off, the input list
/// is returned untouched. Each rule independently checks whether it has enough data to act —
/// if the relevant attribute is missing from the indexer's response (e.g. an older newznab
/// implementation that doesn't return <c>grabs</c>), that rule is skipped for that item, never
/// causing a false drop.
/// </summary>
public static class IndexerResultFilter
{
    /// <summary>
    /// Returns a new list with drop rules applied and (when configured) sorted by grabs descending.
    /// Items whose drop rules cannot evaluate due to missing attributes are kept (fail-open).
    /// </summary>
    public static List<NewznabClient.NewznabItem> Apply(
        IReadOnlyList<NewznabClient.NewznabItem> items,
        IndexerConfig.ResultFilter? filter,
        DateTimeOffset now)
    {
        if (filter is null || !filter.Enabled || items.Count == 0)
            return items.ToList();

        var kept = new List<NewznabClient.NewznabItem>(items.Count);
        foreach (var item in items)
        {
            if (ShouldDrop(item, filter, now)) continue;
            kept.Add(item);
        }

        if (filter.PreferDownloaded && kept.Count > 1)
        {
            // OrderBy is stable in LINQ-to-Objects, so ties preserve indexer's original order.
            // Null grabs are coerced to -1 so they sort BELOW honest 0-grab items.
            kept = kept
                .OrderByDescending(x => x.Grabs ?? -1)
                .ToList();
        }

        return kept;
    }

    private static bool ShouldDrop(NewznabClient.NewznabItem item, IndexerConfig.ResultFilter f, DateTimeOffset now)
    {
        // 1. Passworded — drop only when the indexer explicitly says password != 0.
        //    A missing/null Password attribute is treated as "unknown, keep".
        if (f.SkipPassworded && item.Password is > 0)
            return true;

        var age = EffectiveAge(item, now);

        // 2. MaxAgeDaysWithoutGrabs — drop releases that have been around for a while
        //    with literally zero recorded grabs. Requires both a known age and known grabs.
        if (f.MaxAgeDaysWithoutGrabs > 0
            && age is { } a1
            && a1.TotalDays >= f.MaxAgeDaysWithoutGrabs
            && item.Grabs is 0)
        {
            return true;
        }

        // 3. MinGrabs — drop releases below the threshold, but only after the grace window
        //    has elapsed (so a fresh post isn't punished for being fresh). If we can't
        //    determine age, we apply the grace conservatively: skip the rule entirely.
        if (f.MinGrabs > 0 && item.Grabs is { } g && g < f.MinGrabs)
        {
            if (f.GrabsGraceHours > 0)
            {
                // Bypass when within grace window. Unknown age = treat as within grace.
                if (age is null || age.Value.TotalHours < f.GrabsGraceHours)
                    return false;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prefer the indexer-asserted <c>usenetdate</c> (when the post hit Usenet) over <c>pubDate</c>
    /// (when the indexer first saw it). Falls back to pubDate; returns null if neither is present
    /// or if the resulting age is negative (clock skew on either side).
    /// </summary>
    private static TimeSpan? EffectiveAge(NewznabClient.NewznabItem item, DateTimeOffset now)
    {
        var reference = item.UsenetDate ?? item.Posted;
        if (reference is null) return null;
        var age = now - reference.Value;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }
}
