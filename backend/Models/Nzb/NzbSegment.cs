using NzbWebDAV.Models;

namespace NzbWebDAV.Models.Nzb;

public class NzbSegment
{
    public required long Bytes { get; init; }
    public required string MessageId { get; init; }
    /// <summary>
    /// NZB segment ordinal from the <c>number</c> attribute, when present.
    /// </summary>
    public int? Number { get; init; }
    public LongRange? ByteRange { get; set; }
}
