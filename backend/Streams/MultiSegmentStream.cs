using System.Threading.Channels;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Contexts;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

public class MultiSegmentStream : FastReadOnlyNonSeekableStream
{
    private const int BodyPipelineBatchSize = 4;
    private readonly Memory<string> _segmentIds;
    private readonly INntpClient _usenetClient;
    private readonly Channel<Task<Stream>> _streamTasks;
    private readonly int _bodyPipelineBatchSize;
    private readonly ContextualCancellationTokenSource _cts;
    private Stream? _stream;
    private bool _disposed;

    public static Stream Create
    (
        Memory<string> segmentIds,
        INntpClient usenetClient,
        int articleBufferSize,
        CancellationToken cancellationToken
    )
    {
        return articleBufferSize == 0
            ? new UnbufferedMultiSegmentStream(segmentIds, usenetClient)
            : new MultiSegmentStream(segmentIds, usenetClient, articleBufferSize, cancellationToken);
    }

    private MultiSegmentStream
    (
        Memory<string> segmentIds,
        INntpClient usenetClient,
        int articleBufferSize,
        CancellationToken cancellationToken
    )
    {
        _segmentIds = segmentIds;
        _usenetClient = usenetClient;
        _bodyPipelineBatchSize = Math.Min(BodyPipelineBatchSize, articleBufferSize);
        _streamTasks = Channel.CreateBounded<Task<Stream>>(articleBufferSize);
        _cts = ContextualCancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = DownloadSegments(_cts.Token);
    }

    private async Task DownloadSegments(CancellationToken cancellationToken)
    {
        try
        {
            for (var batchStart = 0; batchStart < _segmentIds.Length;)
            {
                var batchCount = Math.Min(
                    _bodyPipelineBatchSize, _segmentIds.Length - batchStart);
                var segmentIds = new SegmentId[batchCount];
                for (var index = 0; index < batchCount; index++)
                {
                    segmentIds[index] = _segmentIds.Span[batchStart + index];
                }

                await _streamTasks.Writer.WaitToWriteAsync(cancellationToken);
                var connection = await _usenetClient.AcquireExclusiveConnectionAsync(
                    segmentIds, cancellationToken);
                var batch = await _usenetClient.DecodedBodiesAsync(
                    segmentIds, connection, cancellationToken).ConfigureAwait(false);
                var streamTasks = batch.Responses.Select(DownloadSegment).ToArray();

                var responseIndex = 0;
                try
                {
                    for (; responseIndex < streamTasks.Length; responseIndex++)
                    {
                        await _streamTasks.Writer.WriteAsync(
                            streamTasks[responseIndex], cancellationToken);
                    }
                }
                catch
                {
                    for (; responseIndex < streamTasks.Length; responseIndex++)
                    {
                        _ = DisposeStreamAsync(streamTasks[responseIndex]);
                    }

                    throw;
                }

                batchStart += batchCount;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _streamTasks.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            _streamTasks.Writer.TryComplete(exception);
        }
        finally
        {
            _streamTasks.Writer.TryComplete();
        }

        return;
    }

    private static async Task<Stream> DownloadSegment(
        Task<UsenetDecodedBodyResponse> responseTask)
    {
        var bodyResponse = await responseTask.ConfigureAwait(false);
        return bodyResponse.Stream ??
            throw new InvalidDataException(
                $"NNTP BODY failed for segment {bodyResponse.SegmentId}: {bodyResponse.ResponseMessage}");
    }

    private static async Task DisposeStreamAsync(Task<Stream> streamTask)
    {
        try
        {
            await using var stream = await streamTask.ConfigureAwait(false);
        }
        catch
        {
            // The producer owns reporting download failures.
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // if the stream is null, get the next stream.
            if (_stream == null)
            {
                if (!await _streamTasks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!_streamTasks.Reader.TryRead(out var streamTask)) return 0;
                _stream = await streamTask;
            }

            // read from the stream
            var read = await _stream.ReadAsync(buffer, cancellationToken);
            if (read > 0) return read;

            // if the stream ended, continue to the next stream.
            await _stream.DisposeAsync();
            _stream = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (!disposing) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _stream?.Dispose();
        _streamTasks.Writer.TryComplete();

        // ensure that streams that were never read from the channel get disposed
        while (_streamTasks.Reader.TryRead(out var streamTask))
            _ = DisposeStreamAsync(streamTask);

        base.Dispose();
    }
}