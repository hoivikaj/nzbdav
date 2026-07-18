using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers.AddFile;

namespace NzbWebDAV.Tests.Api;

public class AddFileRequestTests
{
    [Fact]
    public void ResolveFileName_PrefersNzbNameQueryParam()
    {
        Assert.Equal("My.Release.nzb", AddFileRequest.ResolveFileName("My.Release", "upload.nzb"));
    }

    [Fact]
    public void ResolveFileName_KeepsNzbExtensionOnNzbName()
    {
        Assert.Equal("My.Release.nzb", AddFileRequest.ResolveFileName("My.Release.nzb", "upload.nzb"));
    }

    [Theory]
    [InlineData("My.Release.nzb.gz", "My.Release.nzb")]
    [InlineData("My.Release.gz", "My.Release.nzb")]
    public void ResolveFileName_NormalizesGzipNames(string input, string expected)
    {
        Assert.Equal(expected, AddFileRequest.ResolveFileName(input, "upload.nzb"));
        Assert.Equal(expected, AddFileRequest.ResolveFileName(null, input));
    }

    [Fact]
    public void ResolveFileName_FallsBackToFormFileName()
    {
        Assert.Equal("upload.nzb", AddFileRequest.ResolveFileName(null, "upload.nzb"));
        Assert.Equal("upload.nzb", AddFileRequest.ResolveFileName("  ", "upload.nzb"));
    }

    [Fact]
    public void ResolveFileName_ThrowsWhenNeitherNameIsUsable()
    {
        var ex = Assert.Throws<BadHttpRequestException>(() => AddFileRequest.ResolveFileName(null, null));
        Assert.Contains("filename", ex.Message, StringComparison.OrdinalIgnoreCase);

        ex = Assert.Throws<BadHttpRequestException>(() => AddFileRequest.ResolveFileName("  ", ""));
        Assert.Contains("filename", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
