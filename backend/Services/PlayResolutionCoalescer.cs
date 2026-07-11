using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

public class PlayResolutionCoalescer
{
    private readonly ConcurrentDictionary<string, Lease> _inFlight = new();

    public Lease Acquire(string groupKey)
    {
        while (true)
        {
            var mine = new Lease(this, groupKey);
            var existing = _inFlight.GetOrAdd(groupKey, mine);
            if (ReferenceEquals(existing, mine))
                return mine;
            if (!existing.IsCompleted)
                return existing.AsFollower();
            _inFlight.TryRemove(new KeyValuePair<string, Lease>(groupKey, existing));
        }
    }

    public sealed class Lease
    {
        private readonly PlayResolutionCoalescer _owner;
        private readonly string _key;
        private readonly TaskCompletionSource<Guid?> _tcs;

        internal Lease(PlayResolutionCoalescer owner, string key)
        {
            _owner = owner;
            _key = key;
            _tcs = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsLeader = true;
        }

        private Lease(Lease leader)
        {
            _owner = leader._owner;
            _key = leader._key;
            _tcs = leader._tcs;
            IsLeader = false;
        }

        public bool IsLeader { get; }
        internal bool IsCompleted => _tcs.Task.IsCompleted;
        internal Lease AsFollower() => new(this);

        public Task<Guid?> WaitAsync(CancellationToken ct) => _tcs.Task.WaitAsync(ct);

        public void Publish(Guid? nzoId)
        {
            if (!IsLeader) return;
            _owner._inFlight.TryRemove(new KeyValuePair<string, Lease>(_key, this));
            _tcs.TrySetResult(nzoId);
        }
    }
}
