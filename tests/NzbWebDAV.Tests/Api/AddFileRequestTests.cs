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
