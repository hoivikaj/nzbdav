using System.Text;
using NzbWebDAV.Models.Nzb;

namespace NzbWebDAV.Tests.Models;

public class NzbDocumentTests
{
    [Fact]
    public async Task LoadAsync_ParsesMetadataFilesAndSegments()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <nzb xmlns="http://www.newzbin.com/DTD/2003/nzb">
              <head>
                <meta type="category">movies</meta>
                <meta type="password">secret</meta>
              </head>
              <file subject="example.mkv">
                <segments>
                  <segment bytes="123" number="1">segment-1@example</segment>
                  <segment bytes="456" number="2">segment-2@example</segment>
                </segments>
              </file>
            </nzb>
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var document = await NzbDocument.LoadAsync(stream);

        Assert.Equal("movies", document.Metadata["category"]);
        Assert.Equal("secret", document.Metadata["password"]);
        var file = Assert.Single(document.Files);
        Assert.Equal("example.mkv", file.Subject);
        Assert.Collection(
            file.Segments,
            segment =>
            {
                Assert.Equal(123, segment.Bytes);
                Assert.Equal("segment-1@example", segment.MessageId);
            },
            segment =>
            {
                Assert.Equal(456, segment.Bytes);
                Assert.Equal("segment-2@example", segment.MessageId);
            });
    }

    [Fact]
    public async Task LoadAsync_TrimsWhitespaceAroundSegmentMessageIds()
    {
        const string xml = """
            <nzb><file subject="file"><segments>
              <segment bytes="10">
                padded@example.com
              </segment>
              <segment bytes="20">  spaced@example.com  </segment>
            </segments></file></nzb>
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var document = await NzbDocument.LoadAsync(stream);

        Assert.Collection(
            Assert.Single(document.Files).Segments,
            segment => Assert.Equal("padded@example.com", segment.MessageId),
            segment => Assert.Equal("spaced@example.com", segment.MessageId));
    }

    [Fact]
    public async Task LoadAsync_UsesZeroForInvalidSegmentSize()
    {
        const string xml = """
            <nzb><file subject="file"><segments>
              <segment bytes="invalid">segment</segment>
            </segments></file></nzb>
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var document = await NzbDocument.LoadAsync(stream);

        Assert.Equal(0, Assert.Single(Assert.Single(document.Files).Segments).Bytes);
    }

    [Fact]
    public async Task LoadAsync_WrapsMalformedXml()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<nzb><file>"));

        var exception = await Assert.ThrowsAsync<Exception>(
            () => NzbDocument.LoadAsync(stream));

        Assert.Equal("Could not parse the nzb document (malformed nzb)", exception.Message);
        Assert.IsType<System.Xml.XmlException>(exception.InnerException);
    }
}
