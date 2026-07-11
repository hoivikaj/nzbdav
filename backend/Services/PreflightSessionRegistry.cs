using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

public class PreflightSessionRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessions = new();

    public Session BeginSession(string profileToken, string type, string id)
    {
        var key = MakeKey(profileToken, type, id);
        var cts = new CancellationTokenSource();
        var prior = _sessions.AddOrUpdate(key, _ => cts, (_, _) => cts);
        if (!ReferenceEquals(prior, cts))
        {
            try { prior.Cancel(); } catch (ObjectDisposedException) { }
            prior.Dispose();
        }
        return new Session(this, key, cts);
    }

    private void CompleteIfOwned(string key, CancellationTokenSource owned)
    {
        if (_sessions.TryGetValue(key, out var current) && ReferenceEquals(current, owned))
        {
            _sessions.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, owned));
        }
        owned.Dispose();
    }

    public void Cancel(string profileToken, string type, string id)
    {
        var key = MakeKey(profileToken, type, id);
        if (_sessions.TryGetValue(key, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private static string MakeKey(string profileToken, string type, string id) =>
        $"{profileToken}\x1f{type}\x1f{id}";

    public sealed class Session : IDisposable
    {
        private readonly PreflightSessionRegistry _registry;
        private readonly string _key;
        private readonly CancellationTokenSource _cts;
        private int _disposed;

        internal Session(PreflightSessionRegistry registry, string key, CancellationTokenSource cts)
        {
            _registry = registry;
            _key = key;
            _cts = cts;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _registry.CompleteIfOwned(_key, _cts);
        }
    }
}
