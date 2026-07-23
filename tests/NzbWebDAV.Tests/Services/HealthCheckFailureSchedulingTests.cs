using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Services;

public class HealthCheckFailureSchedulingTests
{
    [Fact]
    public void ComputeFailureNextHealthCheck_KnownFailure_DefersForOneDay()
    {
        var utcNow = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        var next = HealthCheckService.ComputeFailureNextHealthCheck(utcNow, knownFailure: true);

        Assert.Equal(utcNow + TimeSpan.FromDays(1), next);
    }

    [Fact]
    public void ComputeFailureNextHealthCheck_UnexpectedFailure_DefersForOneHour()
    {
        var utcNow = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        var next = HealthCheckService.ComputeFailureNextHealthCheck(utcNow, knownFailure: false);

        Assert.Equal(utcNow + TimeSpan.FromHours(1), next);
    }

    [Fact]
    public void LoginFailure_IsKnownAndCanUseLongDeferral()
    {
        var exception = new CouldNotLoginToUsenetException("invalid credentials");
        var utcNow = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        Assert.True(exception.TryGetKnownErrorMessage(out var reason));
        Assert.Equal("invalid credentials", reason);
        Assert.Equal(
            utcNow + TimeSpan.FromDays(1),
            HealthCheckService.ComputeFailureNextHealthCheck(utcNow, knownFailure: true));
    }

    [Fact]
    public void CancellationException_IsOnlySuppressedWhenHealthCheckTokenIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var exception = new OperationCanceledException();

        Assert.False(exception.IsCancellationException(cancellation.Token));

        cancellation.Cancel();

        Assert.True(exception.IsCancellationException(cancellation.Token));
    }
}
