using System.IO.Compression;
using System.Text;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class NzbStreamUtilTests
{
    [Fact]
    public async Task OpenMaybeCompressedAsync_ReplaysPlainStreamPrefix()
    {
        var expected = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><nzb />");
        await using var source = new MemoryStream(expected);

        var result = await NzbStreamUtil.OpenMaybeCompressedAsync(source);
        await using var stream = result.Stream;
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);

        Assert.False(result.IsGzip);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task OpenMaybeCompressedAsync_DetectsGzipByMagicBytes()
    {
        var expected = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><nzb />");
        await using var source = new MemoryStream();
        await using (var gzip = new GZipStream(source, CompressionMode.Compress, leaveOpen: true))
            await gzip.WriteAsync(expected);
        source.Position = 0;

        var result = await NzbStreamUtil.OpenMaybeCompressedAsync(source);
        await using var stream = result.Stream;
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);

        Assert.True(result.IsGzip);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task OpenMaybeCompressedAsync_InvalidGzipFailsDuringRead()
    {
        await using var source = new MemoryStream([0x1f, 0x8b, 0x00, 0x01]);
        var result = await NzbStreamUtil.OpenMaybeCompressedAsync(source);
        await using var stream = result.Stream;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await stream.CopyToAsync(Stream.Null));
    }

    [Theory]
    [InlineData("release", "release.nzb")]
    [InlineData("release.nzb", "release.nzb")]
    [InlineData("release.nzb.gz", "release.nzb")]
    [InlineData("release.GZ", "release.nzb")]
    public void NormalizeFileName_ProducesPlainNzbName(string input, string expected)
    {
        Assert.Equal(expected, NzbStreamUtil.NormalizeFileName(input));
    }
}
