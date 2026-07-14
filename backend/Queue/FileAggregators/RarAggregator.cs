using System.Diagnostics.CodeAnalysis;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Queue.FileAggregators;

public class RarAggregator(DavDatabaseClient dbClient, DavItem mountDirectory, bool checkedFullHealth) : BaseAggregator
{
    protected override DavDatabaseClient DBClient => dbClient;
    protected override DavItem MountDirectory => mountDirectory;

    public override void UpdateDatabase(List<BaseProcessor.Result> processorResults)
    {
        var fileSegments = processorResults
            .OfType<RarProcessor.Result>()
            .SelectMany(x => x.StoredFileSegments)
            .ToList();

        ProcessArchive(fileSegments);

        foreach (var lazy in processorResults.OfType<LazyRarProcessor.Result>())
            ProcessLazyArchive(lazy);
    }

    private void ProcessLazyArchive(LazyRarProcessor.Result result)
    {
        var pathInArchive = result.PathInArchive;
        var parentDirectory = EnsureParentDirectory(pathInArchive);
        var name = SanitizeDavName(Path.GetFileName(pathInArchive));

        // Mirror the eager path's obfuscation rename: when the archive
        // contains a single obfuscated file, name it after the mount folder.
        if (ObfuscationUtil.IsProbablyObfuscated(name))
            name = SanitizeDavName(mountDirectory.Name + Path.GetExtension(name));

        var davMultipartFile = new DavMultipartFile
        {
            Id = Guid.NewGuid(),
            Metadata = new DavMultipartFile.Meta
            {
                AesParams = result.AesParams,
                FileParts = [result.FirstPart],
                IsLazy = true,
                PathInArchive = pathInArchive,
                ArchivePassword = result.Password,
                PendingParts = result.PendingParts,
            }
        };

        var davItem = DavItem.New(
            id: Guid.NewGuid(),
            parent: parentDirectory,
            name: name,
            fileSize: result.TotalFileSize,
            type: DavItem.ItemType.UsenetFile,
            subType: DavItem.ItemSubType.MultipartFile,
            releaseDate: result.ReleaseDate,
            // Lazy mounts skip the per-part health check entirely; mark
            // unchecked so the background health-check sweep covers them.
            lastHealthCheck: null,
            historyItemId: MountDirectory.HistoryItemId,
            fileBlobId: davMultipartFile.Id,
            nzbBlobId: MountDirectory.HistoryItemId
        );

        dbClient.Ctx.Items.Add(davItem);
        dbClient.Ctx.AddBlob(davMultipartFile);
    }

    private void ProcessArchive(List<RarProcessor.StoredFileSegment> fileSegments)
    {
        var archiveFiles = new Dictionary<string, List<RarProcessor.StoredFileSegment>>();
        foreach (var fileSegment in fileSegments)
        {
            if (!archiveFiles.ContainsKey(fileSegment.PathWithinArchive))
                archiveFiles.Add(fileSegment.PathWithinArchive, []);

            archiveFiles[fileSegment.PathWithinArchive].Add(fileSegment);
        }

        foreach (var archiveFile in archiveFiles)
        {
            // Ensure we have all volumes necessary for this file.
            ValidateVolumes(archiveFile.Value);

            // Initialize dav-item fields
            var pathWithinArchive = archiveFile.Key;
            var fileParts = SortByPartNumber(archiveFile.Value);
            var aesParams = fileParts.Select(x => x.AesParams).FirstOrDefault(x => x != null);
            var fileSize = aesParams?.DecodedSize ?? fileParts.Sum(x => x.ByteRangeWithinPart.Count);
            var parentDirectory = EnsureParentDirectory(pathWithinArchive);
            var name = SanitizeDavName(Path.GetFileName(pathWithinArchive));

            // If there is only one file in the archive and the file-name is obfuscated,
            // then rename the file to the same name as the containing mount directory.
            if (archiveFiles.Count == 1 && ObfuscationUtil.IsProbablyObfuscated(name))
                name = SanitizeDavName(mountDirectory.Name + Path.GetExtension(name));

            var davMultipartFile = new DavMultipartFile()
            {
                Id = Guid.NewGuid(),
                Metadata = new DavMultipartFile.Meta()
                {
                    AesParams = aesParams,
                    FileParts = fileParts.Select(x => new DavMultipartFile.FilePart()
                    {
                        SegmentIds = x.NzbFile.GetSegmentIds(),
                        SegmentIdByteRange = LongRange.FromStartAndSize(0, x.PartSize),
                        FilePartByteRange = x.ByteRangeWithinPart,
                        SegmentByteRanges = x.NzbFile.GetSegmentByteRanges(),
                    }).ToArray(),
                }
            };

            var davItem = DavItem.New(
                id: Guid.NewGuid(),
                parent: parentDirectory,
                name: name,
                fileSize: fileSize,
                type: DavItem.ItemType.UsenetFile,
                subType: DavItem.ItemSubType.MultipartFile,
                releaseDate: fileParts.First().ReleaseDate,
                lastHealthCheck: checkedFullHealth ? DateTimeOffset.UtcNow : null,
                historyItemId: MountDirectory.HistoryItemId,
                fileBlobId: davMultipartFile.Id,
                nzbBlobId: MountDirectory.HistoryItemId
            );

            dbClient.Ctx.Items.Add(davItem);
            dbClient.Ctx.AddBlob(davMultipartFile);
        }
    }

    internal enum PartNumberSource
    {
        Header,
        Filename,
        Mixed
    }

    internal static PartNumberSource SelectPartNumberSource(
        List<RarProcessor.StoredFileSegment> storedFileSegments)
    {
        var headerNumbers = storedFileSegments
            .Select(x => x.PartNumber.PartNumberFromHeader)
            .Where(n => n is >= 0)
            .Select(n => n!.Value)
            .ToList();
        var filenameNumbers = storedFileSegments
            .Select(x => x.PartNumber.PartNumberFromFilename)
            .Where(n => n is >= 0)
            .Select(n => n!.Value)
            .ToList();

        var headerPresentForAll = storedFileSegments.All(x => x.PartNumber.PartNumberFromHeader is >= 0);
        var filenamePresentForAll = storedFileSegments.All(x => x.PartNumber.PartNumberFromFilename is >= 0);
        var headerDistinct = headerNumbers.Count > 0
                             && headerNumbers.Distinct().Count() == headerNumbers.Count;
        var filenameDistinct = filenameNumbers.Count > 0
                               && filenameNumbers.Distinct().Count() == filenameNumbers.Count;

        if (headerPresentForAll && headerDistinct)
            return PartNumberSource.Header;

        if (filenamePresentForAll && filenameDistinct)
        {
            if (headerNumbers.Count > 0 && !headerDistinct)
            {
                Log.Debug(
                    "RAR header volume numbers are not distinct; using filename-derived part numbers instead");
            }

            return PartNumberSource.Filename;
        }

        return PartNumberSource.Mixed;
    }

    internal static RarProcessor.StoredFileSegment[] SortByPartNumber(
        List<RarProcessor.StoredFileSegment> storedFileSegments)
    {
        var source = SelectPartNumberSource(storedFileSegments);

        int? delta = null;
        if (source == PartNumberSource.Mixed)
        {
            // Find delta between part number from headers and filenames.
            delta = storedFileSegments
                .Select(x => x.PartNumber)
                .Where(x => x is { PartNumberFromHeader: >= 0, PartNumberFromFilename: >= 0 })
                .Select(x => x.PartNumberFromHeader - x.PartNumberFromFilename)
                .GroupBy(x => x)
                .MaxBy(x => x.Count())?.Key;
        }

        var withNumbers = storedFileSegments
            .Select(x => (Segment: x, Part: GetNormalizedPartNumber(x.PartNumber, delta, source)))
            .ToList();

        // Drop identical physical duplicates before uniqueness validation.
        var deduped = DeduplicateIdenticalVolumes(withNumbers);

        ValidatePartNumbers(deduped.Select(x => x.Part));

        return deduped
            .OrderBy(x => x.Part)
            .Select(x => x.Segment)
            .ToArray();
    }

    private static List<(RarProcessor.StoredFileSegment Segment, int Part)> DeduplicateIdenticalVolumes(
        List<(RarProcessor.StoredFileSegment Segment, int Part)> parts)
    {
        var result = new List<(RarProcessor.StoredFileSegment Segment, int Part)>();
        foreach (var group in parts.GroupBy(x => x.Part))
        {
            var entries = group.ToList();
            if (entries.Count == 1)
            {
                result.Add(entries[0]);
                continue;
            }

            var first = entries[0].Segment;
            var allIdentical = entries.All(e =>
                e.Segment.PartSize == first.PartSize
                && e.Segment.ByteRangeWithinPart == first.ByteRangeWithinPart
                && e.Segment.PathWithinArchive == first.PathWithinArchive
                && e.Segment.FileUncompressedSize == first.FileUncompressedSize);

            if (allIdentical)
            {
                Log.Warning(
                    "Dropping duplicate RAR volume {Part} for {Archive}",
                    group.Key, first.ArchiveName);
                result.Add(entries[0]);
            }
            else
            {
                // Leave ambiguous group intact so ValidatePartNumbers throws.
                result.AddRange(entries);
            }
        }

        return result;
    }

    internal static void ValidateVolumes(List<RarProcessor.StoredFileSegment> storedFileSegments)
    {
        if (storedFileSegments.Count == 0) return;
        var distinctUncompressedSizes = storedFileSegments.Select(x => x.FileUncompressedSize).Distinct().ToList();
        if (distinctUncompressedSizes.Count != 1)
            throw new InvalidDataException("Inconsistent rar file size detected.");
        var expected = distinctUncompressedSizes[0];
        var actual = storedFileSegments.Sum(x => x.ByteRangeWithinPart.Count);
        if (Math.Abs(actual - expected) > 16)
            throw new InvalidDataException("Missing rar volumes detected.");
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    private static void ValidatePartNumbers(IEnumerable<int> partNumbers)
    {
        var count = partNumbers.Count();
        var uniqueCount = partNumbers.Distinct().Count();
        if (count != uniqueCount)
            throw new InvalidDataException("Rar archive has duplicate volume numbers.");
    }

    private static int GetNormalizedPartNumber(
        RarProcessor.PartNumber partNumber, int? delta, PartNumberSource source)
    {
        return source switch
        {
            PartNumberSource.Header when partNumber.PartNumberFromHeader is >= 0 or -1
                => partNumber.PartNumberFromHeader!.Value,
            PartNumberSource.Filename when partNumber.PartNumberFromFilename is >= 0 or -1
                => partNumber.PartNumberFromFilename!.Value,
            PartNumberSource.Mixed when partNumber.PartNumberFromHeader >= 0
                => partNumber.PartNumberFromHeader!.Value,
            PartNumberSource.Mixed when partNumber.PartNumberFromFilename >= 0
                => partNumber.PartNumberFromFilename!.Value + (delta ?? 0),
            _ when partNumber.PartNumberFromHeader is -1 => -1,
            _ when partNumber.PartNumberFromFilename is -1 => -1,
            _ => -1
        };
    }
}
