using System.Diagnostics;
using NzbWebDAV.Services.Metrics;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

/// <summary>
/// Wraps a YencStream and attributes every byte read back to the provider that
/// served it. The inner stream still performs the real decode work; this class
/// just observes the byte count on each Read and forwards it to
/// ProviderBytesTracker so per-provider download volume can be aggregated.
/// </summary>
public sealed class CountingYencStream : YencStream
{
    private readonly YencStream _inner;
    private readonly ProviderBytesTracker _tracker;
    private readonly string _host;
    private long _bytes;
    private long _activeReadTicks;

    public CountingYencStream(YencStream inner, ProviderBytesTracker tracker, string host) : base(Null)
    {
        _inner = inner;
        _tracker = tracker;
        _host = host;
    }

    public override ValueTask<UsenetYencHeader?> GetYencHeadersAsync(CancellationToken cancellationToken = default)
        => _inner.GetYencHeadersAsync(cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp();
        var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _activeReadTicks += Stopwatch.GetTimestamp() - start;
        if (n > 0)
        {
            _tracker.Add(_host, n);
            _bytes += n;
        }
        return n;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_bytes > 0 && _activeReadTicks > 0)
            {
                var activeMs = _activeReadTicks * 1000.0 / Stopwatch.Frequency;
                _tracker.RecordSegmentThroughput(_host, _bytes, activeMs);
            }

            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
