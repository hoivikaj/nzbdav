using NzbWebDAV.Clients.Usenet.Models;
using Serilog;

namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Tracks recent BODY/ARTICLE outcomes for an NNTP provider and temporarily
/// disables it when a failure threshold is reached, preventing a single
/// misbehaving provider from blocking the entire download pipeline.
/// <para>
/// Failures accumulate in a short sliding window between successes. A success
/// clears the window and fully resets the cooldown ladder (same recovery
/// semantics as the former consecutive-failure breaker). After tripping, the
/// provider enters a cooldown during which it is skipped and additional
/// failures are ignored (latched). When the cooldown expires, exactly one
/// half-open probe is admitted; other callers keep seeing the provider as
/// tripped until that probe records success or failure. A failed probe
/// re-trips with the doubled cooldown. An abandoned probe (no outcome within
/// <see cref="ProbeAbandonTimeout"/>) can be retaken.
/// </para>
/// </summary>
public class ProviderCircuitBreaker
{
    private const int WindowSeconds = 30;
    private const int MinFailuresToTrip = 3;
    private const double TripFailureRate = 0.5;

    private static readonly TimeSpan InitialCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultProbeAbandonTimeout = TimeSpan.FromSeconds(60);

    private readonly string _providerName;
    private readonly Action<ProviderCircuitTransition>? _onTransition;
    private readonly object _lock = new();
    private readonly Queue<(long AtMs, bool Failed)> _window = new();

    private long _trippedUntilMs;
    private TimeSpan _currentCooldown = InitialCooldown;
    private int _halfOpenProbeInFlight; // 0/1
    private long _probeStartedMs;
    private string? _lastFailureReason;
    private long _tripCount;
    private long _failureCount;
    private long _articleMissCount;

    public ProviderCircuitBreaker(
        string providerName,
        Action<ProviderCircuitTransition>? onTransition = null)
    {
        _providerName = providerName;
        _onTransition = onTransition;
    }

    /// <summary>How long an unanswered half-open probe may hold the slot. For tests.</summary>
    internal TimeSpan ProbeAbandonTimeout { get; set; } = DefaultProbeAbandonTimeout;

    public bool IsTripped
    {
        get
        {
            var trippedUntil = Volatile.Read(ref _trippedUntilMs);
            if (trippedUntil == 0) return false;
            if (Environment.TickCount64 < trippedUntil) return true;

            // Cooldown expired → half-open: exactly one caller wins the probe slot.
            TryReclaimAbandonedProbe();
            if (Interlocked.CompareExchange(ref _halfOpenProbeInFlight, 1, 0) == 0)
            {
                Volatile.Write(ref _probeStartedMs, Environment.TickCount64);
                return false; // this caller is the probe
            }

            return true; // another probe is already in flight
        }
    }

    /// <summary>
    /// True once a trip has latched, whether still cooling down or waiting on a probe.
    /// Unlike <see cref="IsTripped"/> this claims no probe slot, so callers can read it
    /// without altering state.
    /// </summary>
    public bool IsLatched => Volatile.Read(ref _trippedUntilMs) != 0;

    /// <summary>TickCount64 deadline while latched open; 0 when not tripped. For tests.</summary>
    internal long TrippedUntilMs => Volatile.Read(ref _trippedUntilMs);

    /// <summary>Cooldown that will apply on the next trip. For tests.</summary>
    internal TimeSpan CurrentCooldown
    {
        get
        {
            lock (_lock) return _currentCooldown;
        }
    }

    /// <summary>Force the open cooldown into the past so half-open tests can proceed.</summary>
    internal void ExpireCooldownForTests()
    {
        lock (_lock)
        {
            if (_trippedUntilMs > 0)
                _trippedUntilMs = Environment.TickCount64 - 1;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            // Only a circuit that opened can recover. Failures that cleared without tripping
            // are routine, and announcing a recovery for them implies an outage that never
            // happened. Matches the transition notification below.
            var wasCircuitActive = _trippedUntilMs > 0 || _halfOpenProbeInFlight != 0;
            if (wasCircuitActive)
                Log.Information("Provider {Provider} recovered — circuit breaker reset.", _providerName);

            _window.Clear();
            _trippedUntilMs = 0;
            _currentCooldown = InitialCooldown;
            _lastFailureReason = null;
            Volatile.Write(ref _halfOpenProbeInFlight, 0);
            Volatile.Write(ref _probeStartedMs, 0);
            if (wasCircuitActive)
                NotifyTransition(ProviderCircuitTransitionState.Closed, cooldown: null);
        }
    }

    /// <summary>
    /// Article permanently missing from retention. Counts as a miss for diagnostics
    /// but does not contribute to provider-failure tripping.
    /// </summary>
    public void RecordArticleNotFound()
    {
        Interlocked.Increment(ref _articleMissCount);
        RecordSuccess();
    }

    /// <summary>Read-only snapshot for dashboards. Does not claim a half-open probe.</summary>
    public ProviderCircuitBreakerSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            var trippedUntil = _trippedUntilMs;
            var probeInFlight = Volatile.Read(ref _halfOpenProbeInFlight) == 1;

            ProviderCircuitState state;
            int? cooldownRemainingSeconds = null;
            if (trippedUntil > 0 && now < trippedUntil)
            {
                state = ProviderCircuitState.Open;
                cooldownRemainingSeconds = Math.Max(0, (int)Math.Ceiling((trippedUntil - now) / 1000.0));
            }
            else if (trippedUntil > 0 || probeInFlight)
            {
                state = ProviderCircuitState.HalfOpen;
            }
            else
            {
                state = ProviderCircuitState.Closed;
            }

            return new ProviderCircuitBreakerSnapshot(
                state,
                cooldownRemainingSeconds,
                _lastFailureReason,
                Volatile.Read(ref _tripCount),
                Volatile.Read(ref _failureCount),
                Volatile.Read(ref _articleMissCount));
        }
    }

    public void RecordFailure(string? reason = null)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;

            // Already latched open: ignore in-flight failures from the same burst
            // so they cannot extend the window, double the cooldown, or spam logs.
            if (_trippedUntilMs > 0 && now < _trippedUntilMs)
                return;

            // Half-open probe failed → re-trip immediately with the current
            // (already doubled from the previous trip) cooldown, then advance ladder.
            if (Volatile.Read(ref _halfOpenProbeInFlight) == 1)
            {
                Volatile.Write(ref _halfOpenProbeInFlight, 0);
                Volatile.Write(ref _probeStartedMs, 0);
                Trip(now, reason is null
                    ? "half-open probe failure"
                    : $"half-open probe failure ({reason})");
                return;
            }

            // Cooldown expired (or never tripped); clear a stale trip marker so
            // success recovery logging stays accurate.
            if (_trippedUntilMs > 0)
                _trippedUntilMs = 0;

            EvictOldEntries(now);
            _window.Enqueue((now, true));
            Interlocked.Increment(ref _failureCount);

            var failures = 0;
            foreach (var entry in _window)
                if (entry.Failed) failures++;

            if (failures >= MinFailuresToTrip
                && failures / (double)_window.Count >= TripFailureRate)
            {
                var tripReason = reason is null
                    ? $"{failures} failures in {_window.Count}-sample window"
                    : $"{failures} failures in {_window.Count}-sample window ({reason})";
                Trip(now, tripReason);
            }
        }
    }

    private void Trip(long nowMs, string reason)
    {
        var appliedCooldown = _currentCooldown;
        _lastFailureReason = reason;
        Interlocked.Increment(ref _tripCount);
        _trippedUntilMs = nowMs + (long)appliedCooldown.TotalMilliseconds;
        Log.Warning(
            "Provider {Provider} tripped ({Reason}). Skipping for {Cooldown}s.",
            _providerName, reason, appliedCooldown.TotalSeconds);
        NotifyTransition(ProviderCircuitTransitionState.Open, appliedCooldown);

        _window.Clear();
        _currentCooldown = TimeSpan.FromMilliseconds(
            Math.Min(_currentCooldown.TotalMilliseconds * 2, MaxCooldown.TotalMilliseconds));
    }

    private void NotifyTransition(
        ProviderCircuitTransitionState state,
        TimeSpan? cooldown)
    {
        if (_onTransition is null)
            return;

        try
        {
            _onTransition(new ProviderCircuitTransition(
                state,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cooldown));
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "Provider {Provider} circuit transition callback failed",
                _providerName);
        }
    }

    private void EvictOldEntries(long nowMs)
    {
        var cutoff = nowMs - WindowSeconds * 1000L;
        while (_window.Count > 0 && _window.Peek().AtMs < cutoff)
            _window.Dequeue();
    }

    private void TryReclaimAbandonedProbe()
    {
        if (Volatile.Read(ref _halfOpenProbeInFlight) != 1) return;
        var started = Volatile.Read(ref _probeStartedMs);
        if (started == 0) return;
        if (Environment.TickCount64 - started < (long)ProbeAbandonTimeout.TotalMilliseconds) return;

        // Abandoned probe (cancelled request, etc.): free the slot so another
        // caller can retry. CompareExchange so we don't clear a just-resolved probe.
        if (Interlocked.CompareExchange(ref _halfOpenProbeInFlight, 0, 1) == 1)
            Volatile.Write(ref _probeStartedMs, 0);
    }
}
