using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.Profiles.Adapters;

[ApiController]
[Route("adapters/newznab/{token}/api")]
public class NewznabAdapterController(
    SearchProfileService searchService,
    ConfigManager configManager
) : ControllerBase
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Newznab = "http://www.newznab.com/DTD/2010/feeds/attributes/";

    [HttpGet]
    public async Task<IActionResult> Get(
        string token,
        [FromQuery(Name = "t")] string? type,
        [FromQuery] string? q,
        [FromQuery] string? imdbid,
        [FromQuery] string? tvdbid,
        [FromQuery] int? season,
        [FromQuery] int? ep,
        [FromQuery] int? offset,
        [FromQuery] int? limit)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";

        var profile = searchService.GetProfile(token);
        if (profile is null) return NotFound();
        if (!searchService.IsAdapterEnabled(token, "newznab")) return NotFound();

        var op = (type ?? "").ToLowerInvariant();
        if (op is "" or "caps")
        {
            return Content(BuildCapsXml(GetAdvertisedLimit()), "application/xml", Encoding.UTF8);
        }

        var ct = HttpContext.RequestAborted;
        var baseUrl = HttpContext.GetPublicBaseUrl(configManager.GetBaseUrl());

        SearchProfileService.SearchResult? result = null;

        if (op == "movie" && !string.IsNullOrWhiteSpace(imdbid))
        {
            var id = imdbid.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? imdbid : $"tt{imdbid}";
            result = await searchService.SearchByImdbAsync(token, "movie", id, ct, q).ConfigureAwait(false);
        }
        else if (op == "tvsearch" && !string.IsNullOrWhiteSpace(imdbid) && season.HasValue && ep.HasValue)
        {
            var idDigits = imdbid.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? imdbid[2..] : imdbid;
            var id = $"tt{idDigits}:{season.Value}:{ep.Value}";
            result = await searchService.SearchByImdbAsync(token, "series", id, ct, q).ConfigureAwait(false);
        }
        else
        {
            return Content(BuildFeedXml(profile, baseUrl, token, Array.Empty<NewznabResult>()), "application/xml", Encoding.UTF8);
        }

        if (result is null) return NotFound();

        var feedItems = result.Candidates
            .Select((c, i) => new NewznabResult(c, result.PlayTokens[i]))
            .ToList();

        return Content(BuildFeedXml(profile, baseUrl, token, feedItems), "application/xml", Encoding.UTF8);
    }

    // Advertise the largest result count this instance can actually return (the global default,
    // raised by any per-indexer override) so downstream clients don't cap themselves below it.
    private int GetAdvertisedLimit()
    {
        var cfg = configManager.GetIndexerConfig();
        var globalLimit = cfg.SearchResultLimit is int g && g > 0 ? g : IndexerConfig.DefaultSearchResultLimit;
        return cfg.Indexers.Aggregate(globalLimit, (max, i) =>
            i.SearchResultLimit is int p && p > max ? p : max);
    }

    private static string BuildCapsXml(int limit)
    {
        var limitStr = limit.ToString();
        var caps = new XElement("caps",
            new XElement("server",
                new XAttribute("appversion", ConfigManager.AppVersion),
                new XAttribute("version", "0.1"),
                new XAttribute("title", "NzbDav Search Profile"),
                new XAttribute("strapline", "Newznab adapter for a NzbDav Search Profile")),
            new XElement("limits",
                new XAttribute("max", limitStr),
                new XAttribute("default", limitStr)),
            new XElement("searching",
                new XElement("search",
                    new XAttribute("available", "no"),
                    new XAttribute("supportedParams", "")),
                new XElement("tv-search",
                    new XAttribute("available", "yes"),
                    new XAttribute("supportedParams", "imdbid,season,ep")),
                new XElement("movie-search",
                    new XAttribute("available", "yes"),
                    new XAttribute("supportedParams", "imdbid")),
                new XElement("audio-search",
                    new XAttribute("available", "no"),
                    new XAttribute("supportedParams", "")),
                new XElement("book-search",
                    new XAttribute("available", "no"),
                    new XAttribute("supportedParams", ""))),
            new XElement("categories",
                new XElement("category", new XAttribute("id", "2000"), new XAttribute("name", "Movies")),
                new XElement("category", new XAttribute("id", "5000"), new XAttribute("name", "TV"))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), caps).ToString();
    }

    private static string BuildFeedXml(
        ProfileConfig.Profile profile,
        string baseUrl,
        string token,
        IReadOnlyList<NewznabResult> items)
    {
        var channel = new XElement("channel",
            new XElement("title", $"NzbDav Search Profile: {profile.Name}"),
            new XElement("description", "Newznab adapter for a NzbDav Search Profile"),
            new XElement("link", baseUrl),
            new XElement("language", "en-us"),
            new XElement("category", "search"),
            new XElement(Atom + "link",
                new XAttribute("href", $"{baseUrl}/adapters/newznab/{token}/api"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")),
            new XElement(Newznab + "response",
                new XAttribute("offset", "0"),
                new XAttribute("total", items.Count.ToString())));

        foreach (var r in items)
        {
            var nzbUrl = $"{baseUrl}/api/search/{token}/nzb/{r.PlayToken}.nzb";
            var item = new XElement("item",
                new XElement("title", r.Candidate.Title),
                new XElement("guid", new XAttribute("isPermaLink", "false"), r.PlayToken),
                new XElement("link", nzbUrl),
                new XElement("comments", string.Empty),
                new XElement("pubDate", (r.Candidate.Posted ?? DateTimeOffset.UtcNow).ToString("R")),
                new XElement("category", "Movies"),
                new XElement("description", r.Candidate.Title),
                new XElement("enclosure",
                    new XAttribute("url", nzbUrl),
                    new XAttribute("length", r.Candidate.Size.ToString()),
                    new XAttribute("type", "application/x-nzb")),
                new XElement(Newznab + "attr", new XAttribute("name", "size"), new XAttribute("value", r.Candidate.Size.ToString())),
                new XElement(Newznab + "attr", new XAttribute("name", "category"), new XAttribute("value", "2000")));

            if (r.Candidate.Grabs.HasValue)
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "grabs"),
                    new XAttribute("value", r.Candidate.Grabs.Value.ToString())));
            if (r.Candidate.Password.HasValue)
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "password"),
                    new XAttribute("value", r.Candidate.Password.Value.ToString())));
            if (r.Candidate.UsenetDate.HasValue)
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "usenetdate"),
                    new XAttribute("value", r.Candidate.UsenetDate.Value.ToString("R"))));

            var displayIndexer = !string.IsNullOrWhiteSpace(r.Candidate.SourceIndexerName)
                ? r.Candidate.SourceIndexerName
                : r.Candidate.IndexerName;
            if (!string.IsNullOrWhiteSpace(displayIndexer))
            {
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "hydraIndexerName"),
                    new XAttribute("value", displayIndexer)));
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "sourceIndexerName"),
                    new XAttribute("value", displayIndexer)));
            }
            if (!string.IsNullOrWhiteSpace(r.Candidate.Language))
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "language"),
                    new XAttribute("value", r.Candidate.Language)));
            if (!string.IsNullOrWhiteSpace(r.Candidate.Subs))
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "subs"),
                    new XAttribute("value", r.Candidate.Subs)));
            if (!string.IsNullOrWhiteSpace(r.Candidate.InfoHash))
                item.Add(new XElement(Newznab + "attr",
                    new XAttribute("name", "infohash"),
                    new XAttribute("value", r.Candidate.InfoHash)));

            channel.Add(item);
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", Atom.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "newznab", Newznab.NamespaceName),
            channel);

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), rss).ToString();
    }

    private record NewznabResult(NzbResolutionCache.Candidate Candidate, string PlayToken);
}
