using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.Watchtower;

[ApiController]
[Route("api/get-watchtower")]
public class GetWatchtowerController(DavDatabaseClient dbClient, ConfigManager configManager) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var ct = HttpContext.RequestAborted;
        var query = HttpContext.Request.Query;
        var statsOnly = query["statsOnly"].ToString() is "1" or "true";

        var sources = await dbClient.Ctx.ListSources.AsNoTracking()
            .OrderBy(s => s.CreatedAtUnix)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var counts = await dbClient.Ctx.WantedItems.AsNoTracking()
            .GroupBy(w => w.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        int CountFor(string s) => counts.FirstOrDefault(c => c.State == s)?.Count ?? 0;

        var stats = new GetWatchtowerResponse.StatsDto
        {
            Total = counts.Sum(c => c.Count),
            Ready = CountFor(WantedItem.StateReady),
            Scouting = CountFor(WantedItem.StateScouting),
            Unavailable = CountFor(WantedItem.StateUnavailable),
            Parked = CountFor(WantedItem.StateParked),
            Expanders = CountFor(WantedItem.StateExpander),
        };

        var sourceDtos = sources.Select(s => new GetWatchtowerResponse.SourceDto
        {
            Id = s.Id.ToString(),
            Kind = s.Kind,
            Name = s.Name,
            Url = s.Url,
            Enabled = s.Enabled,
            Cap = s.Cap,
            SeriesScope = s.SeriesScope,
            LastSyncedAtUnix = s.LastSyncedAtUnix,
            LastSyncError = s.LastSyncError,
        }).ToList();

        if (statsOnly)
        {
            return Ok(new GetWatchtowerResponse
            {
                Status = true,
                Enabled = configManager.IsWatchtowerEnabled(),
                Sources = sourceDtos,
                Items = new List<GetWatchtowerResponse.ItemDto>(),
                Shows = new List<GetWatchtowerResponse.ItemDto>(),
                Total = stats.Total,
                HasMore = false,
                Stats = stats,
            });
        }

        var offset = int.TryParse(query["offset"].ToString(), out var o) ? Math.Max(0, o) : 0;
        var limit = int.TryParse(query["limit"].ToString(), out var l) ? Math.Clamp(l, 1, 200) : 100;
        var stateFilter = query["state"].ToString();
        var q = query["q"].ToString().Trim();

        var expanderParam = query["expander"].ToString();
        var fetchingChildren = !string.IsNullOrEmpty(expanderParam);
        var browsingShows = !fetchingChildren && stateFilter == WantedItem.StateExpander;

        var leaves = dbClient.Ctx.WantedItems.AsNoTracking().Where(w => w.State != WantedItem.StateExpander);
        if (fetchingChildren)
            leaves = leaves.Where(w => w.Provenance.Contains(WtReconcile.ExpanderTag(expanderParam)));
        if (browsingShows)
            leaves = leaves.Where(_ => false);
        else if (IsLeafState(stateFilter))
            leaves = leaves.Where(w => w.State == stateFilter);
        if (!string.IsNullOrWhiteSpace(q))
            leaves = leaves.Where(w => w.Title.Contains(q) || w.ContentId.Contains(q));

        var total = browsingShows ? 0 : await leaves.CountAsync(ct).ConfigureAwait(false);
        var ordered = ApplySort(leaves, query["sort"].ToString());
        if (!fetchingChildren) ordered = ordered.Skip(offset).Take(limit);
        var page = await ordered
            .Select(w => new
            {
                w.Key,
                w.Type,
                w.ContentId,
                w.Title,
                w.State,
                w.Provenance,
                w.Shortlist,
                w.LastVerifiedAtUnix,
                w.NextCheckAtUnix,
                w.FailReason,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var shows = offset == 0 && !fetchingChildren
            ? await BuildShowsAsync(stateFilter, q, ct).ConfigureAwait(false)
            : new List<GetWatchtowerResponse.ItemDto>();

        return Ok(new GetWatchtowerResponse
        {
            Status = true,
            Enabled = configManager.IsWatchtowerEnabled(),
            Sources = sourceDtos,
            Items = page.Select(w =>
            {
                var shortlist = WtJson.ReadPointers(w.Shortlist);
                var winner = shortlist.FirstOrDefault();
                var provenance = WtJson.ReadStrings(w.Provenance);
                var expanderTag = provenance.FirstOrDefault(p => p.StartsWith("exp:", StringComparison.Ordinal));
                return new GetWatchtowerResponse.ItemDto
                {
                    Key = w.Key,
                    Type = w.Type,
                    ContentId = w.ContentId,
                    Title = w.Title,
                    State = w.State,
                    ProvenanceCount = provenance.Count,
                    ExpanderKey = expanderTag is null ? null : expanderTag.Substring(4),
                    ShortlistCount = shortlist.Count,
                    WinnerTitle = winner?.Title,
                    WinnerSize = winner?.Size ?? 0,
                    LastVerifiedAtUnix = w.LastVerifiedAtUnix,
                    NextCheckAtUnix = w.NextCheckAtUnix,
                    FailReason = w.FailReason,
                };
            }).ToList(),
            Shows = shows,
            Total = total,
            HasMore = offset + page.Count < total,
            Stats = stats,
        });
    }

    private async Task<List<GetWatchtowerResponse.ItemDto>> BuildShowsAsync(string? stateFilter, string? q, CancellationToken ct)
    {
        var expanders = await dbClient.Ctx.WantedItems.AsNoTracking()
            .Where(w => w.State == WantedItem.StateExpander)
            .Select(w => new { w.Key, w.Type, w.ContentId, w.Title, w.Provenance, w.NextCheckAtUnix })
            .ToListAsync(ct).ConfigureAwait(false);
        if (expanders.Count == 0) return new List<GetWatchtowerResponse.ItemDto>();

        var children = await dbClient.Ctx.WantedItems.AsNoTracking()
            .Where(w => w.State != WantedItem.StateExpander)
            .Select(w => new { w.State, w.Title, w.ContentId, w.Provenance })
            .ToListAsync(ct).ConfigureAwait(false);

        var hasState = IsLeafState(stateFilter);
        var hasQ = !string.IsNullOrWhiteSpace(q);
        var tally = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var relevant = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in children)
        {
            var tag = WtJson.ReadStrings(c.Provenance).FirstOrDefault(p => p.StartsWith("exp:", StringComparison.Ordinal));
            if (tag is null) continue;
            var key = tag.Substring(4);
            if (!tally.TryGetValue(key, out var n)) tally[key] = n = new int[3];
            if (c.State != WantedItem.StateParked) n[0]++;
            if (c.State == WantedItem.StateReady) n[1]++;
            if (c.State == WantedItem.StateUnavailable) n[2]++;

            var matches = (!hasState || c.State == stateFilter)
                          && (!hasQ || c.Title.Contains(q!, StringComparison.OrdinalIgnoreCase)
                                    || c.ContentId.Contains(q!, StringComparison.OrdinalIgnoreCase));
            if (matches) relevant.Add(key);
        }

        var showAll = !hasState && !hasQ;
        var result = new List<GetWatchtowerResponse.ItemDto>();
        foreach (var ex in expanders.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (!showAll && !relevant.Contains(ex.Key)) continue;
            tally.TryGetValue(ex.Key, out var n);
            n ??= new int[3];
            result.Add(new GetWatchtowerResponse.ItemDto
            {
                Key = ex.Key,
                Type = ex.Type,
                ContentId = ex.ContentId,
                Title = ex.Title,
                State = WantedItem.StateExpander,
                ProvenanceCount = WtJson.ReadStrings(ex.Provenance).Count,
                ShortlistCount = 0,
                WinnerSize = 0,
                NextCheckAtUnix = ex.NextCheckAtUnix,
                ChildTotal = n[0],
                ChildReady = n[1],
                ChildUnavailable = n[2],
            });
        }
        return result;
    }

    private static bool IsLeafState(string? s) => s is WantedItem.StateReady or WantedItem.StateScouting
        or WantedItem.StateUnavailable or WantedItem.StateParked;

    private static IQueryable<WantedItem> ApplySort(IQueryable<WantedItem> q, string? sort) => sort switch
    {
        "title" => q.OrderBy(w => w.Title).ThenBy(w => w.Key),
        "recheck" => q.OrderBy(w => w.NextCheckAtUnix == null).ThenBy(w => w.NextCheckAtUnix).ThenBy(w => w.Key),
        "status" => q
            .OrderBy(w =>
                w.State == WantedItem.StateUnavailable ? 0
                : w.State == WantedItem.StateScouting ? 1
                : w.State == WantedItem.StateParked ? 2
                : w.State == WantedItem.StateReady ? 3
                : 4)
            .ThenByDescending(w => w.UpdatedAtUnix)
            .ThenBy(w => w.Key),
        _ => q.OrderByDescending(w => w.UpdatedAtUnix).ThenBy(w => w.Key),
    };
}

public class GetWatchtowerResponse : BaseApiResponse
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
    [JsonPropertyName("sources")] public required List<SourceDto> Sources { get; init; }
    [JsonPropertyName("items")] public required List<ItemDto> Items { get; init; }
    [JsonPropertyName("shows")] public required List<ItemDto> Shows { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("hasMore")] public required bool HasMore { get; init; }
    [JsonPropertyName("stats")] public required StatsDto Stats { get; init; }

    public class SourceDto
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("kind")] public required string Kind { get; init; }
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
        [JsonPropertyName("cap")] public required int Cap { get; init; }
        [JsonPropertyName("seriesScope")] public string? SeriesScope { get; init; }
        [JsonPropertyName("lastSyncedAtUnix")] public long? LastSyncedAtUnix { get; init; }
        [JsonPropertyName("lastSyncError")] public string? LastSyncError { get; init; }
    }

    public class ItemDto
    {
        [JsonPropertyName("key")] public required string Key { get; init; }
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("contentId")] public required string ContentId { get; init; }
        [JsonPropertyName("title")] public required string Title { get; init; }
        [JsonPropertyName("state")] public required string State { get; init; }
        [JsonPropertyName("provenanceCount")] public required int ProvenanceCount { get; init; }
        [JsonPropertyName("expanderKey")] public string? ExpanderKey { get; init; }
        [JsonPropertyName("childTotal")] public int? ChildTotal { get; init; }
        [JsonPropertyName("childReady")] public int? ChildReady { get; init; }
        [JsonPropertyName("childUnavailable")] public int? ChildUnavailable { get; init; }
        [JsonPropertyName("shortlistCount")] public required int ShortlistCount { get; init; }
        [JsonPropertyName("winnerTitle")] public string? WinnerTitle { get; init; }
        [JsonPropertyName("winnerSize")] public required long WinnerSize { get; init; }
        [JsonPropertyName("lastVerifiedAtUnix")] public long? LastVerifiedAtUnix { get; init; }
        [JsonPropertyName("nextCheckAtUnix")] public long? NextCheckAtUnix { get; init; }
        [JsonPropertyName("failReason")] public string? FailReason { get; init; }
    }

    public class StatsDto
    {
        [JsonPropertyName("total")] public required int Total { get; init; }
        [JsonPropertyName("ready")] public required int Ready { get; init; }
        [JsonPropertyName("scouting")] public required int Scouting { get; init; }
        [JsonPropertyName("unavailable")] public required int Unavailable { get; init; }
        [JsonPropertyName("parked")] public required int Parked { get; init; }
        [JsonPropertyName("expanders")] public required int Expanders { get; init; }
    }
}
