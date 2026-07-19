using System.Net;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class TrustedHostsMatcherTests
{
    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.True(TrustedHostsMatcher.Parse(null).IsEmpty);
        Assert.True(TrustedHostsMatcher.Parse("").IsEmpty);
        Assert.True(TrustedHostsMatcher.Parse("   ,  \t").IsEmpty);
    }

    [Theory]
    [InlineData("prowlarr", "prowlarr")]
    [InlineData("prowlarr", "PROWLARR")]
    [InlineData("hydra.lan", "Hydra.LAN")]
    public void IsTrustedHost_MatchesCaseInsensitively(string entry, string host)
    {
        var matcher = TrustedHostsMatcher.Parse(entry);
        Assert.True(matcher.IsTrustedHost(host));
    }

    [Fact]
    public void IsTrustedHost_DoesNotMatchOtherHosts()
    {
        var matcher = TrustedHostsMatcher.Parse("prowlarr");
        Assert.False(matcher.IsTrustedHost("sonarr"));
        Assert.False(matcher.IsTrustedHost("192.168.1.10"));
    }

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50", true)]
    [InlineData("192.168.1.50", "192.168.1.51", false)]
    [InlineData("10.0.0.1", "10.0.0.1", true)]
    public void IsTrustedAddress_MatchesExactIpLiterals(string entry, string address, bool expected)
    {
        var matcher = TrustedHostsMatcher.Parse(entry);
        Assert.Equal(expected, matcher.IsTrustedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.1", true)]
    [InlineData("192.168.1.0/24", "192.168.1.255", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("10.0.0.0/8", "10.255.255.255", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    public void IsTrustedAddress_MatchesIpv4Cidr(string entry, string address, bool expected)
    {
        var matcher = TrustedHostsMatcher.Parse(entry);
        Assert.Equal(expected, matcher.IsTrustedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("fd00::/8", "fd12::1", true)]
    [InlineData("fd00::/8", "fe80::1", false)]
    [InlineData("2001:db8::/32", "2001:db8::abcd", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    public void IsTrustedAddress_MatchesIpv6Cidr(string entry, string address, bool expected)
    {
        var matcher = TrustedHostsMatcher.Parse(entry);
        Assert.Equal(expected, matcher.IsTrustedAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void IsTrustedAddress_NormalizesIpv4MappedIpv6()
    {
        var matcher = TrustedHostsMatcher.Parse("192.168.1.50");
        var mapped = IPAddress.Parse("::ffff:192.168.1.50");
        Assert.True(matcher.IsTrustedAddress(mapped));
    }

    [Fact]
    public void IsTrustedHost_TreatsListedIpLiteralAsTrustedHost()
    {
        var matcher = TrustedHostsMatcher.Parse("192.168.1.50");
        Assert.True(matcher.IsTrustedHost("192.168.1.50"));
    }

    [Fact]
    public void Wildcard_TrustsAnyHostAndAddress()
    {
        var matcher = TrustedHostsMatcher.Parse("*");
        Assert.False(matcher.IsEmpty);
        Assert.True(matcher.IsTrustedHost("prowlarr"));
        Assert.True(matcher.IsTrustedHost("anything.local"));
        Assert.True(matcher.IsTrustedAddress(IPAddress.Parse("10.0.0.1")));
        Assert.True(matcher.IsTrustedAddress(IPAddress.Loopback));
        Assert.True(matcher.IsTrustedAddress(IPAddress.Parse("fd00::1")));
    }

    [Fact]
    public void Parse_SupportsCommaAndWhitespaceSeparatedEntries()
    {
        var matcher = TrustedHostsMatcher.Parse("prowlarr, hydra.lan\n192.168.1.0/24\t10.0.0.5");
        Assert.True(matcher.IsTrustedHost("prowlarr"));
        Assert.True(matcher.IsTrustedHost("hydra.lan"));
        Assert.True(matcher.IsTrustedAddress(IPAddress.Parse("192.168.1.42")));
        Assert.True(matcher.IsTrustedAddress(IPAddress.Parse("10.0.0.5")));
        Assert.False(matcher.IsTrustedHost("sonarr"));
    }

    [Fact]
    public void Parse_IgnoresInvalidEntries()
    {
        var matcher = TrustedHostsMatcher.Parse("prowlarr, not-a-cidr/99, 192.168.1.0/24");
        Assert.True(matcher.IsTrustedHost("prowlarr"));
        Assert.True(matcher.IsTrustedAddress(IPAddress.Parse("192.168.1.10")));
        Assert.False(matcher.IsTrustedHost("not-a-cidr/99"));
    }
}
