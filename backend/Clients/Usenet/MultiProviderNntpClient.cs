using System.Runtime.ExceptionServices;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class MultiProviderNntpClient(List<MultiConnectionNntpClient> providers) : NntpClient
{
    public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken ct)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken ct)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(x => x.StatAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(x => x.HeadAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        CancellationToken cancellationToken
    )
    {
        return RunFromPoolWithBackup(x => x.DecodedBodyAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        CancellationToken cancellationToken
    )
    {
        return RunFromPoolWithBackup(x => x.DecodedArticleAsync(segmentId, cancellationToken), cancellationToken);
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return RunFromPoolWithBackup(x => x.DateAsync(cancellationToken), cancellationToken);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        UsenetDecodedBodyResponse? result;
        try
        {
            result = await RunFromPoolWithBackup(
                x => x.DecodedBodyAsync(segmentId, OnConnectionReadyAgain, cancellationToken),
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }

        if (result.ResponseType != UsenetResponseType.ArticleRetrievedBodyFollows)
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);

        return result;

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            if (articleBodyResult == ArticleBodyResult.Retrieved)
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        }
    }

    public override async Task<UsenetDecodedBodyBatch> DecodedBodiesAsync
    (
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        ExceptionDispatchInfo? lastException = null;
        var orderedProviders = GetOrderedProviders();
        for (var providerIndex = 0; providerIndex < orderedProviders.Count; providerIndex++)
        {
            var provider = orderedProviders[providerIndex];
            var deferredCallback = new DeferredArticleBodyCallback();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var primaryBatch = await provider.DecodedBodiesAsync(
                    segmentIds, deferredCallback.Invoke, cancellationToken).ConfigureAwait(false);
                var coordinator = new BatchCallbackCoordinator(
                    primaryBatch.Responses.Count, onConnectionReadyAgain);
                deferredCallback.Activate(coordinator.CompleteTransfer);
                var fallbackProviders = orderedProviders
                    .Skip(providerIndex + 1)
                    .ToArray();
                var responses = primaryBatch.Responses
                    .Select((response, index) => ResolveBatchResponseAsync(
                        response,
                        segmentIds[index],
                        fallbackProviders,
                        coordinator,
                        cancellationToken))
                    .ToArray();
                return new UsenetDecodedBodyBatch { Responses = responses };
            }
            catch (Exception e) when (!e.IsCancellationException())
            {
                deferredCallback.Discard();
                lastException = ExceptionDispatchInfo.Capture(e);
            }
            catch
            {
                deferredCallback.Discard();
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
                throw;
            }
        }

        onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
        lastException?.Throw();
        throw new Exception("There are no usenet providers configured.");
    }

    private static async Task<UsenetDecodedBodyResponse> ResolveBatchResponseAsync(
        Task<UsenetDecodedBodyResponse> primaryResponse,
        SegmentId segmentId,
        IReadOnlyList<MultiConnectionNntpClient> fallbackProviders,
        BatchCallbackCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            UsenetDecodedBodyResponse? response = null;
            ExceptionDispatchInfo? lastException = null;
            try
            {
                response = await primaryResponse.ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationException())
            {
                lastException = ExceptionDispatchInfo.Capture(e);
            }

            if (response?.ResponseType == UsenetResponseType.ArticleRetrievedBodyFollows)
            {
                return response;
            }

            if (response != null &&
                response.ResponseType != UsenetResponseType.NoArticleWithThatMessageId)
            {
                throw new UsenetArticleNotFoundException(segmentId);
            }

            foreach (var provider in fallbackProviders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                coordinator.AddTransfer();
                var deferredCallback = new DeferredArticleBodyCallback();
                try
                {
                    response = await provider.DecodedBodyAsync(
                        segmentId, deferredCallback.Invoke, cancellationToken).ConfigureAwait(false);
                    var responseType = response.ResponseType;
                    deferredCallback.Activate(
                        responseType == UsenetResponseType.ArticleRetrievedBodyFollows
                            ? coordinator.CompleteTransfer
                            : _ => coordinator.CompleteAttempt());
                    lastException = null;
                }
                catch (Exception e) when (!e.IsCancellationException())
                {
                    deferredCallback.Discard();
                    coordinator.CompleteAttempt();
                    lastException = ExceptionDispatchInfo.Capture(e);
                    continue;
                }
                catch
                {
                    deferredCallback.Discard();
                    coordinator.CompleteAttempt();
                    throw;
                }

                if (response.ResponseType == UsenetResponseType.ArticleRetrievedBodyFollows)
                {
                    return response;
                }
            }

            lastException?.Throw();
            throw new UsenetArticleNotFoundException(segmentId);
        }
        catch
        {
            coordinator.MarkResolutionFailure();
            throw;
        }
        finally
        {
            coordinator.CompleteDecision();
        }
    }

    private sealed class BatchCallbackCoordinator(
        int responseCount,
        Action<ArticleBodyResult>? callback)
    {
        private int _remaining = responseCount + 1;
        private int _transportFailed;
        private int _resolutionFailed;
        private int _callbackInvoked;

        public void AddTransfer()
        {
            Interlocked.Increment(ref _remaining);
        }

        public void CompleteTransfer(ArticleBodyResult result)
        {
            if (result == ArticleBodyResult.NotRetrieved)
            {
                Volatile.Write(ref _transportFailed, 1);
            }
            else if (result == ArticleBodyResult.Cancelled)
            {
                MarkResolutionFailure();
            }

            CompleteOne();
        }

        public void CompleteDecision()
        {
            CompleteOne();
        }

        public void CompleteAttempt()
        {
            CompleteOne();
        }

        public void MarkResolutionFailure()
        {
            Volatile.Write(ref _resolutionFailed, 1);
        }

        private void CompleteOne()
        {
            if (Interlocked.Decrement(ref _remaining) != 0 ||
                Interlocked.Exchange(ref _callbackInvoked, 1) != 0)
            {
                return;
            }

            callback?.Invoke(
                Volatile.Read(ref _transportFailed) == 0 &&
                Volatile.Read(ref _resolutionFailed) == 0
                ? ArticleBodyResult.Retrieved
                : ArticleBodyResult.NotRetrieved);
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        UsenetDecodedArticleResponse? result;
        try
        {
            result = await RunFromPoolWithBackup(
                x => x.DecodedArticleAsync(segmentId, OnConnectionReadyAgain, cancellationToken),
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }

        if (result.ResponseType != UsenetResponseType.ArticleRetrievedHeadAndBodyFollow)
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);

        return result;

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            if (articleBodyResult == ArticleBodyResult.Retrieved)
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        }
    }

    private async Task<T> RunFromPoolWithBackup<T>
    (
        Func<INntpClient, Task<T>> task,
        CancellationToken cancellationToken
    ) where T : UsenetResponse
    {
        ExceptionDispatchInfo? lastException = null;
        var orderedProviders = GetOrderedProviders();
        for (var i = 0; i < orderedProviders.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = orderedProviders[i];
            var isLastProvider = i == orderedProviders.Count - 1;

            if (lastException is not null)
            {
                var msg = lastException.SourceException.Message;
                Log.Debug($"Encountered error during NNTP Operation: `{msg}`. Trying another provider.");
            }

            try
            {
                var result = await task.Invoke(provider).ConfigureAwait(false);

                // if no article with that message-id is found, try again with the next provider.
                if (!isLastProvider && result.ResponseType == UsenetResponseType.NoArticleWithThatMessageId)
                    continue;

                return result;
            }
            catch (Exception e) when (!e.IsCancellationException())
            {
                lastException = ExceptionDispatchInfo.Capture(e);
            }
        }

        lastException?.Throw();
        throw new Exception("There are no usenet providers configured.");
    }

    private List<MultiConnectionNntpClient> GetOrderedProviders()
    {
        var enabled = providers
            .Where(x => x.ProviderType != ProviderType.Disabled)
            .OrderBy(x => x.ProviderType)
            .ThenByDescending(x => x.AvailableConnections)
            .ToList();

        var healthy = enabled.Where(x => !x.IsTripped).ToList();

        // Always return at least one provider so cooldown probes can fire.
        return healthy.Count > 0 ? healthy : enabled;
    }

    public override void Dispose()
    {
        foreach (var provider in providers)
            provider.Dispose();
        GC.SuppressFinalize(this);
    }
}