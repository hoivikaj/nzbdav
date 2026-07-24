using System.Net;
using System.Text;
using NzbWebDAV.Clients.RadarrSonarr;

namespace NzbWebDAV.Tests.Clients;

public class RadarrSonarrClientTests
{
    [Fact]
    public async Task SonarrStaleCachedEpisodeAndSeries_ReturnsFalseAfter404()
    {
        const string seriesPath = "/library/tv/Stale Show";
        const string filePath = seriesPath + "/Stale Show S01E01.mkv";
        using var httpClient = new HttpClient(CreateHandler(
            ("GET /api/v3/series", JsonResponse("""[{"id":101,"path":"/library/tv/Stale Show"}]""")),
            ("GET /api/v3/episodefile?seriesId=101", JsonResponse($"[{{\"id\":201,\"seriesId\":101,\"path\":\"{filePath}\"}}]")),
            ("GET /api/v3/episode?episodeFileId=201", JsonResponse("""[{"id":301,"seriesId":101}]""")),
            ("DELETE /api/v3/episodefile/201", new HttpResponseMessage(HttpStatusCode.OK)),
            ("POST /api/v3/command", new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json"),
            }),
            ("GET /api/v3/episodefile/201", new HttpResponseMessage(HttpStatusCode.NotFound)),
            ("GET /api/v3/series/101", new HttpResponseMessage(HttpStatusCode.NotFound)),
            ("GET /api/v3/series", JsonResponse("[]"))));
        var client = new TestSonarrClient(httpClient);

        Assert.True(await client.RemoveAndSearch(filePath));
        Assert.False(await client.RemoveAndSearch(filePath));
    }

    [Fact]
    public async Task RadarrStaleCachedMovie_ReturnsFalseAfter404()
    {
        const string filePath = "/library/movies/Stale Movie/Stale Movie.mkv";
        using var httpClient = new HttpClient(CreateHandler(
            ("GET /api/v3/movie", JsonResponse($"[{{\"id\":101,\"movieFile\":{{\"id\":201,\"path\":\"{filePath}\"}}}}]")),
            ("DELETE /api/v3/moviefile/201", new HttpResponseMessage(HttpStatusCode.OK)),
            ("POST /api/v3/command", new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json"),
            }),
            ("GET /api/v3/movie/101", new HttpResponseMessage(HttpStatusCode.NotFound)),
            ("GET /api/v3/movie", JsonResponse("[]"))));
        var client = new TestRadarrClient(httpClient);

        Assert.True(await client.RemoveAndSearch(filePath));
        Assert.False(await client.RemoveAndSearch(filePath));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static ResponseQueueHandler CreateHandler(
        params (string request, HttpResponseMessage response)[] responses) =>
        new(responses
            .GroupBy(x => x.request)
            .ToDictionary(
                x => x.Key,
                x => new Queue<HttpResponseMessage>(x.Select(y => y.response))));

    private sealed class TestSonarrClient(HttpClient client) : SonarrClient("http://arr.test", "test-key")
    {
        protected override HttpClient Client => client;
    }

    private sealed class TestRadarrClient(HttpClient client) : RadarrClient("http://arr.test", "test-key")
    {
        protected override HttpClient Client => client;
    }

    private sealed class ResponseQueueHandler(
        Dictionary<string, Queue<HttpResponseMessage>> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
            if (!responses.TryGetValue(key, out var queuedResponses) || !queuedResponses.TryDequeue(out var response))
                throw new InvalidOperationException($"Unexpected request: {key}");

            return Task.FromResult(response);
        }
    }
}
