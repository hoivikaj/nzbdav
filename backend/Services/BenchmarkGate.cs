namespace NzbWebDAV.Services;

/// <summary>
/// A lightweight, in-memory gate that suspends background NNTP work — the
/// download queue, health checks and watchtower — while a connection speed-test
/// runs, so the test gets the provider's full connection budget instead of
/// contending with everything else.
///
/// In-memory by design: a crash or restart clears it, so background work can
/// never get permanently stuck paused. Counter-based so overlapping tests
/// (shouldn't happen, but cheap to be safe) nest correctly.
/// </summary>
public sealed class BenchmarkGate
{
    private int _active;

    /// <summary>True while at least one benchmark is running.</summary>
    public bool IsPaused => Volatile.Read(ref _active) > 0;

    /// <summary>Marks a benchmark as running. Dispose the returned scope to release.</summary>
    public IDisposable Enter()
    {
        Interlocked.Increment(ref _active);
        return new Scope(this);
    }

    private void Exit() => Interlocked.Decrement(ref _active);

    private sealed class Scope(BenchmarkGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            // Guard against double-dispose so the counter can't go negative.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Exit();
        }
    }
}
