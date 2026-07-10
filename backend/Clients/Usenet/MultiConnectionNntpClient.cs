using System.Diagnostics.CodeAnalysis;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// This client is responsible for delegating NNTP commands to a connection pool.
///   * The connection pool enforces a maximum number of allowed connections
///   * When a connection is available, the NNTP command executes immediately
///   * When a connection is not available, the NNTP command waits until a connection becomes available.
///   * When multiple commands are awaiting a connection,
///     then BODY/ARTICLE commands have higher priority than STAT/HEAD/DATE commands.
/// </summary>
/// <param name="connectionPool"></param>
/// <param name="type"></param>
/// <param name="circuitBreaker"></param>
/// <param name="providerName"></param>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class MultiConnectionNntpClient(
    ConnectionPool<INntpClient> connectionPool,
    ProviderType type,
    ProviderCircuitBreaker circuitBreaker,
    string providerName
) : NntpClient
{
    public ProviderType ProviderType { get; } = type;
    public bool IsTripped => circuitBreaker.IsTripped;
    public int LiveConnections => connectionPool.LiveConnections;
    public int IdleConnections => connectionPool.IdleConnections;
    public int ActiveConnections => connectionPool.ActiveConnections;
    public int AvailableConnections => connectionPool.AvailableConnections;

    public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "STAT",
            SemaphorePriority.Low,
            (connection, _) => connection.StatAsync(segmentId, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "HEAD",
            SemaphorePriority.Low,
            (connection, _) => connection.HeadAsync(segmentId, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "BODY",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedBodyAsync(segmentId, onDone, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "ARTICLE",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedArticleAsync(segmentId, onDone, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken ct)
    {
        return RunWithConnection(
            "DATE",
            SemaphorePriority.Low,
            (connection, _) => connection.DateAsync(ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "BODY",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedBodyAsync(segmentId, onDone, ct),
            onConnectionReadyAgain,
            ct
        );
    }

    public override async Task<UsenetDecodedBodyBatch> DecodedBodiesAsync
    (
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        var retryCount = 1;
        while (retryCount >= 0)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            var deferredCallback = new DeferredArticleBodyCallback();
            try
            {
                connectionLock = await connectionPool
                    .GetConnectionLockAsync(SemaphorePriority.High, ct)
                    .ConfigureAwait(false);
                var batch = await connectionLock.Connection.DecodedBodiesAsync(
                    segmentIds, deferredCallback.Invoke, ct).ConfigureAwait(false);

                var callbackInvoked = 0;
                deferredCallback.Activate(OnConnectionReadyAgain);
                return batch;

                void OnConnectionReadyAgain(ArticleBodyResult result)
                {
                    if (Interlocked.Exchange(ref callbackInvoked, 1) != 0) return;
                    switch (result)
                    {
                        case ArticleBodyResult.Retrieved:
                        case ArticleBodyResult.NotFound:
                            circuitBreaker.RecordSuccess();
                            break;
                        case ArticleBodyResult.Cancelled:
                            break;
                        default:
                            circuitBreaker.RecordFailure();
                            LogException(connectionLock.Replace);
                            break;
                    }

                    LogException(connectionLock.Dispose);
                    LogException(() => onConnectionReadyAgain?.Invoke(result));
                }
            }
            catch (Exception e) when (e.IsCancellationException())
            {
                deferredCallback.Discard();
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                deferredCallback.Discard();
                circuitBreaker.RecordFailure();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Debug(e,
                        "Error executing pipelined NNTP BODY commands for provider {Provider}. Retrying with a new connection.",
                        providerName);
                    retryCount--;
                    continue;
                }

                Log.Warning(e,
                    "Error executing pipelined NNTP BODY commands for provider {Provider}.",
                    providerName);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
        }

        throw new InvalidOperationException("Unreachable code");
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "ARTICLE",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedArticleAsync(segmentId, onDone, ct),
            onConnectionReadyAgain,
            ct
        );
    }

    private async Task<T> RunWithConnection<T>
    (
        string name,
        SemaphorePriority priority,
        Func<INntpClient, Action<ArticleBodyResult>, Task<T>> command,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct,
        int retryCount = 1
    ) where T : UsenetResponse
    {
        while (retryCount >= 0)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            try
            {
                connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e.IsCancellationException())
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                circuitBreaker.RecordFailure();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Debug(e, "Error getting connection-lock for provider {Provider}. Retrying with a new connection.", providerName);
                    retryCount--;
                    continue;
                }

                Log.Warning(e, "Error getting connection-lock for provider {Provider}.", providerName);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            T? result;
            try
            {
                result = await command(connectionLock.Connection, OnConnectionReadyAgain).ConfigureAwait(false);
            }
            catch (Exception e) when (e.IsCancellationException())
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e) when (e.TryGetCausingException(out UsenetArticleNotFoundException _))
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                circuitBreaker.RecordFailure();
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Debug(e, "Error executing nntp {Command} command for provider {Provider}. Retrying with a new connection.", name, providerName);
                    retryCount--;
                    continue;
                }

                Log.Warning(e, "Error executing nntp {Command} command for provider {Provider}.", name, providerName);
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            circuitBreaker.RecordSuccess();

            // stat, head, and date
            if (name is "STAT" or "HEAD" or "DATE")
            {
                LogException(() => connectionLock?.Dispose());
            }
            
            // body and article
            else if ((result?.Success ?? false) == false)
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
            }

            return result!;

            void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
            {
                if (articleBodyResult != ArticleBodyResult.Retrieved) return;

                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(articleBodyResult));
            }
        }

        Log.Error("Unreachable code reached");
        throw new InvalidOperationException("Unreachable code ");
    }

    private static void LogException(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Unhandled exception");
        }
    }

    public override void Dispose()
    {
        connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}