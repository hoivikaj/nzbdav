namespace NzbWebDAV.Clients.Usenet.Models;

public enum ProviderCircuitTransitionState
{
    Open,
    Closed,
}

public sealed record ProviderCircuitTransition(
    ProviderCircuitTransitionState State,
    long AtUnixMilliseconds,
    TimeSpan? Cooldown);
