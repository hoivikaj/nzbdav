using System.Text;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Models;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Streams;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Tests.Queue;

public class FetchFirstSegmentsStepTests
{
    [Fact]
    public async Task FetchFirstSegments_PipelinedMismatch_RescuesViaPerArticlePath()
    {
        var config = CreatePipeliningConfig(enabled: true, depth: 4);
        using var client = new MismatchThenRescueNntpClient();
        var files = new List<NzbFile>
        {
            CreateFile("a@example.com", "\"a.rar\" yEnc"),
            CreateFile("b@example.com", "\"b.rar\" yEnc"),
        };

        var results = await FetchFirstSegmentsStep.FetchFirstSegments(
            files, client, config, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.MissingFirstSegment));
        Assert.All(results, r => Assert.NotNull(r.First16KB));
        Assert.Equal(2, client.ArticleFetches);
        Assert.Equal(1, client.PipelinedEnumerations);
    }

    [Fact]
    public async Task FetchFirstSegments_PipelinedSuccess_SetsByteRangeFromYencHeaders()
    {
        var config = CreatePipeliningConfig(enabled: true, depth: 2);
        var payload = Encoding.ASCII.GetBytes(new string('x', 64));
        using var client = new MatchingPipelinedNntpClient(new Dictionary<string, byte[]>
        {
            ["seg@example.com"] = payload,
        });
        var file = CreateFile("seg@example.com", "\"movie.rar\" yEnc");

        var result = Assert.Single(await FetchFirstSegmentsStep.FetchFirstSegments(
            [file], client, config, CancellationToken.None));

        Assert.False(result.MissingFirstSegment);
        Assert.NotNull(result.Header);
        Assert.NotNull(file.Segments[0].ByteRange);
        Assert.Equal(result.Header!.PartOffset, file.Segments[0].ByteRange!.StartInclusive);
        Assert.Equal(result.Header.PartSize, file.Segments[0].ByteRange!.Count);
        Assert.Equal(0, client.ArticleFetches);
    }

    private static ConfigManager CreatePipeliningConfig(bool enabled, int depth)
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetPipeliningEnabled,
                ConfigValue = enabled ? "true" : "false",
            },
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetPipeliningDepth,
                ConfigValue = depth.ToString(),
            },
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetMaxQueueConnections,
                ConfigValue = "5",
            },
        ]);
        return config;
    }

    private static NzbFile CreateFile(string messageId, string subject) => new()
    {
        Subject = subject,
        Segments =
        {
            new NzbSegment { MessageId = messageId, Bytes = 100 },
        },
    };

    private static CachedYencStream CreateCachedStream(byte[] payload, long partOffset = 0)
    {
        var headers = new UsenetYencHeader
        {
            FileName = "fake.bin",
            FileSize = partOffset + payload.Length,
            LineLength = 128,
            PartNumber = 1,
            TotalParts = 1,
            PartOffset = partOffset,
            PartSize = payload.Length,
        };
        return new CachedYencStream(headers, new MemoryStream(payload, writable: false));
    }

    private sealed class MismatchThenRescueNntpClient : NntpClient
    {
        public int PipelinedEnumerations { get; private set; }
        public int ArticleFetches { get; private set; }

        public override async IAsyncEnumerable<PipelinedArticleResult> DecodedArticlesPipelinedAsync(
            IReadOnlyList<string> segmentIds,
            int depth,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            PipelinedEnumerations++;
            // Wrong SegmentId → FetchFirstSegments leaves slots null for rescue.
            foreach (var _ in segmentIds)
            {
                yield return new PipelinedArticleResult
                {
                    SegmentId = "unknown@example.com",
                    Found = true,
                    Stream = CreateCachedStream(Encoding.ASCII.GetBytes("wrong")),
                    ArticleHeaders = null,
                };
            }
        }

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            DecodedArticleAsync(segmentId, null, cancellationToken);

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken)
        {
            ArticleFetches++;
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
            return Task.FromResult(new UsenetDecodedArticleResponse
            {
                SegmentId = segmentId.ToString(),
                ResponseCode = (int)UsenetResponseType.ArticleRetrievedHeadAndBodyFollow,
                ResponseMessage = "220 article",
                Stream = CreateCachedStream(Encoding.ASCII.GetBytes(segmentId.ToString())),
                ArticleHeaders = new UsenetArticleHeader
                {
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Date"] = DateTimeOffset.UtcNow.ToString("R"),
                    },
                },
            });
        }

        public override Task ConnectAsync(
            string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetStatResponse> StatAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetHeadResponse> HeadAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose()
        {
        }
    }

    private sealed class MatchingPipelinedNntpClient(IReadOnlyDictionary<string, byte[]> segments) : NntpClient
    {
        public int ArticleFetches { get; private set; }

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken)
        {
            var responses = segmentIds.Select(id =>
            {
                var key = id.ToString();
                var bytes = segments[key];
                return Task.FromResult(new UsenetDecodedBodyResponse
                {
                    SegmentId = key,
                    ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
                    ResponseMessage = $"222 0 <{key}>",
                    Stream = CreateCachedStream(bytes, partOffset: 100),
                });
            }).ToArray();
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
            return Task.FromResult(new UsenetDecodedBodyBatch { Responses = responses });
        }

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId, CancellationToken cancellationToken)
        {
            ArticleFetches++;
            throw new InvalidOperationException("Rescue path should not run for matching pipelined results");
        }

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            DecodedArticleAsync(segmentId, cancellationToken);

        public override Task ConnectAsync(
            string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetStatResponse> StatAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetHeadResponse> HeadAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose()
        {
        }
    }
}
