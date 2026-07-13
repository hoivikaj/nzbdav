using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Api.Controllers.Profiles.Adapters;

[ApiController]
[Route("adapters/addon/{token}/failover_order")]
public class FailoverOrderController(
    SearchProfileService searchService,
    NzbResolutionCache cache,
    PreferredOrderStore preferredOrderStore
) : ControllerBase
{
    [HttpOptions]
    public IActionResult Preflight()
    {
        SetCors(Response);
        return NoContent();
    }

    [HttpPost]
    public IActionResult Post(string token, [FromBody] FailoverOrderRequest? request)
    {
        SetCors(Response);

        var profile = searchService.GetProfile(token);
        if (profile is null) return NotFound();
        if (!searchService.IsAdapterEnabled(token, "addon")) return NotFound();

        var ids = request?.Streams?
                      .Select(s => s?.FailoverId)
                      .Where(x => !string.IsNullOrWhiteSpace(x))
                      .Select(x => x!)
                      .ToList()
                  ?? request?.Order?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                  ?? [];

        if (ids.Count == 0)
            return new JsonResult(new { ok = false, error = "no failoverIds in body" }) { StatusCode = 400 };

        var groups = new Dictionary<(string Type, string Id), List<string>>();
        var seen = new Dictionary<(string Type, string Id), HashSet<string>>();
        var matched = 0;
        foreach (var id in ids)
        {
            var entry = cache.Get(id);
            if (entry is null || !entry.ProfileToken.FixedTimeEquals(token))
                continue;

            var c = entry.Primary;
            var key = ReleaseIdentity.Key(c.Size, c.Poster, c.UsenetDate, c.NzbUrl);
            var gk = (entry.Type, entry.Id);
            if (!groups.TryGetValue(gk, out var list))
            {
                list = [];
                groups[gk] = list;
                seen[gk] = [];
            }

            if (seen[gk].Add(key))
            {
                list.Add(key);
                matched++;
            }
        }

        foreach (var (gk, keys) in groups)
            preferredOrderStore.Report(token, gk.Type, gk.Id, "aiostreams", keys);

        if (groups.Count > 0)
            Log.Information(
                "FailoverOrder: profile {Profile} reported {Matched}/{Total} stream(s) across {Titles} title(s)",
                profile.Name, matched, ids.Count, groups.Count);

        return new JsonResult(new
        {
            ok = true,
            total = ids.Count,
            matched,
            unmatched = ids.Count - matched,
            titles = groups.Select(g => new { type = g.Key.Type, id = g.Key.Id, count = g.Value.Count }).ToList(),
        });
    }

    private static void SetCors(HttpResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "*";
        response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    }
}

public class FailoverOrderRequest
{
    public List<FailoverStream>? Streams { get; set; }
    public List<string>? Order { get; set; }
}

public class FailoverStream
{
    public string? Name { get; set; }
    public string? FailoverId { get; set; }
}
