using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

public class NewznabRateLimiter
{
    private readonly ConcurrentDictionary<string, Gate> _gates = new();

    public async Task WaitAsync(string indexerName, int requestsPerMinute, CancellationToken ct)
    {
        if (requestsPerMinute <= 0) return;
        var interval = TimeSpan.FromMinutes(1) / requestsPerMinute;
        var gate = _gates.GetOrAdd(indexerName, _ => new Gate());

        TimeSpan wait;
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if (gate.NextAllowed < now) gate.NextAllowed = now;
            wait = gate.NextAllowed - now;
            gate.NextAllowed += interval;
        }

        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
    }

    public async Task<bool> TryWaitAsync(string indexerName, int requestsPerMinute, TimeSpan maxWait, CancellationToken ct)
    {
        if (requestsPerMinute <= 0) return true;
        var interval = TimeSpan.FromMinutes(1) / requestsPerMinute;
        var gate = _gates.GetOrAdd(indexerName, _ => new Gate());

        TimeSpan wait;
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if (gate.NextAllowed < now) gate.NextAllowed = now;
            wait = gate.NextAllowed - now;
            if (wait > maxWait) return false;
            gate.NextAllowed += interval;
        }

        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
        return true;
    }

    private class Gate
    {
        public DateTime NextAllowed = DateTime.UtcNow;
    }
}
