namespace NzbWebDAV.Exceptions;

// Header parse failed because the parser seeked beyond the stream's declared
// length. Distinct from generic corruption: it usually means the volume's
// stream length was estimated too small (see LazyRarResolver's measured-size
// retry), not that the archive data itself is bad.
public class RarSeekPastEndException(string message) : CorruptRarException(message)
{
}
