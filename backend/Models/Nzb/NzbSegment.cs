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
    /// <summary>
    /// Alternate MessageIds for the same segment number, in document order.
    /// Populated by <see cref="NzbFile.CanonicalizeSegments"/> when duplicates collapse.
    /// </summary>
    public string[] FallbackMessageIds { get; set; } = [];
    public LongRange? ByteRange { get; set; }
}
