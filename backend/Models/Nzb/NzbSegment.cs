using NzbWebDAV.Models;

namespace NzbWebDAV.Models.Nzb;

public class NzbSegment
{
    public required long Bytes { get; init; }
    public required string MessageId { get; init; }
    public LongRange? ByteRange { get; set; }
}
