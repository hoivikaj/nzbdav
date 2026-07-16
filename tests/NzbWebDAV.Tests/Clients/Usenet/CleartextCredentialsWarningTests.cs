using NzbWebDAV.Clients.Usenet;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class CleartextCredentialsWarningTests
{
    [Theory]
    [InlineData(false, "user", true)]
    [InlineData(false, "", false)]
    [InlineData(false, null, false)]
    [InlineData(true, "user", false)]
    [InlineData(true, "", false)]
    public void ShouldWarnCleartextCredentials_OnlyWhenCleartextWithUser(
        bool useSsl, string? user, bool expected)
    {
        Assert.Equal(expected, UsenetStreamingClient.ShouldWarnCleartextCredentials(useSsl, user));
    }
}
