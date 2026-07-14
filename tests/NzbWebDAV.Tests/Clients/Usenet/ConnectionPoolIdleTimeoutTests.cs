using NzbWebDAV.Clients.Usenet.Connections;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class ConnectionPoolIdleTimeoutTests
{
    [Fact]
    public void Constructor_UsesConfiguredIdleTimeout()
    {
        using var pool = new ConnectionPool<object>(
            maxConnections: 1,
            connectionFactory: _ => ValueTask.FromResult(new object()),
            idleTimeout: TimeSpan.FromSeconds(120));

        Assert.Equal(TimeSpan.FromSeconds(120), pool.IdleTimeout);
    }

    [Fact]
    public void Constructor_DefaultsIdleTimeoutTo60Seconds()
    {
        using var pool = new ConnectionPool<object>(
            maxConnections: 1,
            connectionFactory: _ => ValueTask.FromResult(new object()));

        Assert.Equal(TimeSpan.FromSeconds(60), pool.IdleTimeout);
    }
}
