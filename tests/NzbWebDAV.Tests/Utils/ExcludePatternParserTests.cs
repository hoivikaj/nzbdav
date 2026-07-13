using System.Text.RegularExpressions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class ExcludePatternParserTests
{
    [Fact]
    public void Parse_BareBody_IsCaseInsensitiveByDefault()
    {
        var parsed = ExcludePatternParser.Parse(@"\.iso$");
        Assert.NotNull(parsed);
        Assert.Matches(parsed!.Value.Regex, "FILE.ISO");
        Assert.DoesNotMatch(parsed.Value.Regex, "FILE.mkv");
    }

    [Fact]
    public void Parse_JsWrapper_SharesDedupKeyWithBareBody()
    {
        var bare = ExcludePatternParser.Parse(@"\.(iso|img)$");
        var wrapped = ExcludePatternParser.Parse(@"/\.(iso|img)$/i");
        Assert.NotNull(bare);
        Assert.NotNull(wrapped);
        Assert.Equal(bare!.Value.Key, wrapped!.Value.Key);
        Assert.Matches(wrapped.Value.Regex, "Movie.ISO");
    }

    [Fact]
    public void Parse_MsFlags_AreOrderIndependentInKey()
    {
        var ms = ExcludePatternParser.Parse("/x/ms");
        var sm = ExcludePatternParser.Parse("/x/sm");
        Assert.NotNull(ms);
        Assert.NotNull(sm);
        Assert.Equal(ms!.Value.Key, sm!.Value.Key);
        Assert.True((ms.Value.Regex.Options & RegexOptions.Multiline) != 0);
        Assert.True((ms.Value.Regex.Options & RegexOptions.Singleline) != 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# comment")]
    [InlineData("[")]
    public void Parse_BlanksCommentsAndInvalid_ReturnNull(string? line)
    {
        Assert.Null(ExcludePatternParser.Parse(line));
    }

    [Fact]
    public void Parse_UnknownJsFlags_AreIgnored()
    {
        var parsed = ExcludePatternParser.Parse("/foo/gu");
        Assert.NotNull(parsed);
        Assert.Equal("foo ", parsed!.Value.Key);
        Assert.Matches(parsed.Value.Regex, "FOO");
    }
}
