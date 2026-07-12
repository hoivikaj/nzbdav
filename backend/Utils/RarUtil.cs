using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using SharpCompress.Common;
using SharpCompress.Common.Rar.Headers;
using SharpCompress.IO;
using SharpCompress.Readers;

namespace NzbWebDAV.Utils;

public static class RarUtil
{
    public static async Task<List<IRarHeader>> GetRarHeadersAsync
    (
        Stream stream,
        string? password,
        CancellationToken ct
    )
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => GetRarHeaders(cancellableStream, password), ct).ConfigureAwait(false);
    }

    // Stops iterating as soon as `predicate` matches a file header. Critical
    // for LazyRarResolver: walking past the matching header would trigger
    // SharpCompress's `Position += compressedSize` seek, which on
    // NzbFileStream costs ~log2(segments) STAT calls per seek — turning a
    // sub-100ms parse into a 500ms+ pause at every volume boundary.
    public static async Task<IRarHeader?> FindFirstFileHeaderAsync
    (
        Stream stream,
        string? password,
        Func<IRarHeader, bool> predicate,
        CancellationToken ct
    )
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => FindFirstFileHeader(cancellableStream, password, predicate), ct)
            .ConfigureAwait(false);
    }

    private static IRarHeader? FindFirstFileHeader(Stream stream, string? password, Func<IRarHeader, bool> predicate)
    {
        try
        {
            var readerOptions = new ReaderOptions { Password = password };
            var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, readerOptions);
            foreach (var header in headerFactory.ReadHeaders(stream))
            {
                if (header.HeaderType != HeaderType.File || header.IsDirectory()) continue;
                if (header.GetCompressionMethod() != 0)
                    throw new UnsupportedRarCompressionMethodException(
                        "Only rar files with compression method m0 are supported.");
                if (predicate(header)) return header;
            }
            return null;
        }
        catch (Exception e) when (TryMapHeaderParseFailure(e, stream, out var mapped))
        {
            throw mapped;
        }
    }

    // Reads headers from the start of a RAR volume and stops as soon as the
    // first file header is yielded. Same motivation as FindFirstFileHeaderAsync
    // — never let SharpCompress seek past file data, because that triggers
    // NzbFileStream's InterpolationSearch (~7 STATs). Returned list typically
    // contains [archive_header, file_header]; callers use the archive header
    // for the IsFirstVolume eligibility check and the file header for
    // path/size/AES metadata.
    public static async Task<List<IRarHeader>> ReadHeadersUntilFirstFileAsync
    (
        Stream stream,
        string? password,
        CancellationToken ct
    )
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => ReadHeadersUntilFirstFile(cancellableStream, password), ct)
            .ConfigureAwait(false);
    }

    private static List<IRarHeader> ReadHeadersUntilFirstFile(Stream stream, string? password)
    {
        try
        {
            var readerOptions = new ReaderOptions { Password = password };
            var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, readerOptions);
            var headers = new List<IRarHeader>();
            foreach (var header in headerFactory.ReadHeaders(stream))
            {
                headers.Add(header);
                if (header.HeaderType != HeaderType.File || header.IsDirectory()) continue;
                if (header.GetCompressionMethod() != 0)
                    throw new UnsupportedRarCompressionMethodException(
                        "Only rar files with compression method m0 are supported.");
                return headers;
            }
            return headers;
        }
        catch (Exception e) when (TryMapHeaderParseFailure(e, stream, out var mapped))
        {
            throw mapped;
        }
    }

    private static List<IRarHeader> GetRarHeaders(Stream stream, string? password)
    {
        try
        {
            var readerOptions = new ReaderOptions
            {
                Password = password,
                LeaveStreamOpen = true,
            };
            var headerFactory = new RarHeaderFactory(StreamingMode.Seekable, readerOptions);
            var headers = new List<IRarHeader>();
            foreach (var header in headerFactory.ReadHeaders(stream))
            {
                // add archive headers
                if (header.HeaderType is HeaderType.Archive or HeaderType.EndArchive)
                {
                    headers.Add(header);
                    continue;
                }

                // skip comments
                if (header.HeaderType == HeaderType.Service)
                {
                    if (header.GetFileName() == "CMT")
                    {
                        var buffer = new byte[header.GetCompressedSize()];
                        _ = stream.Read(buffer, 0, buffer.Length);
                    }

                    continue;
                }

                // we only care about file headers
                if (header.HeaderType != HeaderType.File || header.IsDirectory() ||
                    header.GetFileName() == "QO") continue;

                // we only support stored files (compression method m0).
                if (header.GetCompressionMethod() != 0)
                    throw new UnsupportedRarCompressionMethodException(
                        "Only rar files with compression method m0 are supported.");

                // TODO: support solid archives
                if (header.GetIsEncrypted() && header.GetIsSolid())
                    throw new Exception("Password-protected rar archives cannot be solid.");

                // add the headers
                headers.Add(header);
            }

            return headers;
        }
        catch (Exception e) when (TryMapHeaderParseFailure(e, stream, out var mapped))
        {
            throw mapped;
        }
    }

    // Maps operational header-parse failures to typed exceptions so WebDAV
    // middleware can log a single human-readable ERROR without a stack dump.
    // Unexpected exceptions are left unmapped and rethrown by the caller.
    internal static bool TryMapHeaderParseFailure(Exception e, Stream stream, out Exception mapped)
    {
        if (e.TryGetCausingException(out UsenetArticleNotFoundException? missingArticle))
        {
            mapped = missingArticle!;
            return true;
        }

        if (e.TryGetCausingException(out ArgumentOutOfRangeException? seekPastEnd))
        {
            var offset = FormatActualValue(seekPastEnd!.ActualValue);
            var length = TryGetStreamLength(stream);
            mapped = new RarSeekPastEndException(
                $"Failed to parse RAR volume headers (seek past stream end at offset {offset}; stream length {length})");
            return true;
        }

        if (e.TryGetCausingException(out InvalidFormatException? invalidFormat))
        {
            mapped = new CorruptRarException(
                $"Failed to parse RAR volume headers: {invalidFormat!.Message}");
            return true;
        }

        if (e.TryGetCausingException(out EndOfStreamException? _))
        {
            mapped = new CorruptRarException(
                "Failed to parse RAR volume headers: unexpected end of stream");
            return true;
        }

        mapped = null!;
        return false;
    }

    private static string FormatActualValue(object? actualValue) =>
        actualValue switch
        {
            null => "unknown",
            long l => l.ToString(),
            int i => i.ToString(),
            _ => actualValue.ToString() ?? "unknown",
        };

    private static string TryGetStreamLength(Stream stream)
    {
        try
        {
            return stream.CanSeek ? stream.Length.ToString() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
