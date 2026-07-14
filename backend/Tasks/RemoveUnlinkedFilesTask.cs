using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class RemoveUnlinkedFilesTask(
    ConfigManager configManager,
    WebsocketManager websocketManager,
    bool isDryRun,
    Func<DavDatabaseContext>? createContext = null
) : BaseTask
{
    private static List<string> _allRemovedPaths = [];

    private record UnlinkedItemInfo(string Id, int Type, string Path);

    private DavDatabaseContext CreateContext() => createContext?.Invoke() ?? new DavDatabaseContext();

    protected override async Task ExecuteInternal()
    {
        try
        {
            await RemoveUnlinkedFiles().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Report($"Failed: {e.Message}");
            Log.Error(e, "Failed to remove unlinked files.");
        }
    }

    private async Task RemoveUnlinkedFiles()
    {
        // get linked file paths
        Report("Scanning all linked files...");
        var startTime = DateTime.Now;
        var linkedIdCount = await WriteLinkedIdsToTable().ConfigureAwait(false);
        if (linkedIdCount < 5)
        {
            Report($"Aborted: " +
                   $"There are less than five linked files found in your library. " +
                   $"Cancelling operation to prevent accidental bulk deletion.");
            return;
        }

        Report("Searching for unlinked webdav items...");
        var unlinkedItems = await CountUnlinkedItems(startTime).ConfigureAwait(false);
        Report($"Found {unlinkedItems} webdav items to remove.");

        // The `linkedIdCount < 5` check above only catches a COMPLETELY empty scan. A library
        // dir that is partially mounted, or pointed at the wrong path, can still expose a
        // handful of symlinks and sail past it -- and then nearly every webdav item looks
        // unlinked. Refuse to delete an implausible share of the deletable population.
        // A healthy library here sits around 31% unlinked (samples, nfos, unimported extras),
        // so 90% leaves wide headroom while still catching a broken scan.
        var deletableItems = await CountDeletableItems(startTime).ConfigureAwait(false);
        var extremeUnlinkedRatio = deletableItems > 0 && unlinkedItems > deletableItems * 0.9;
        if (extremeUnlinkedRatio)
        {
            var percent = 100.0 * unlinkedItems / deletableItems;
            var detail =
                $"{unlinkedItems} of {deletableItems} webdav items appear unlinked ({percent:F0}%). " +
                $"That usually means the library directory is missing, unmounted, or misconfigured " +
                $"rather than that the items are orphaned.";

            if (!isDryRun)
            {
                Report($"Aborted: {detail} Cancelling to prevent accidental bulk deletion. " +
                       $"Run a dry-run to inspect if this is genuinely expected.");
                return;
            }

            Report($"Warning: {detail} A non-dry-run would abort.");
        }

        if (isDryRun)
        {
            await DryRunIdentifyUnlinkedFiles(startTime).ConfigureAwait(false);
            Report($"Done. Identified {_allRemovedPaths.Count} unlinked files.");
        }
        else
        {
            await RemoveUnlinkedItems(startTime, unlinkedItems).ConfigureAwait(false);
            await RemoveEmptyDirectories(startTime).ConfigureAwait(false);
            Report($"Done. Removed {_allRemovedPaths.Count} unlinked files.");
        }
    }

    private async Task<int> WriteLinkedIdsToTable()
    {
        await using var dbContext = CreateContext();

        // Create a new table "TMP_LINKED_FILES", dropping old one if it already exists.
        // No index initially for fast writes.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS TMP_LINKED_FILES;
            CREATE TABLE TMP_LINKED_FILES (Id TEXT NOT NULL);
            """).ConfigureAwait(false);

        var count = 0;
        var batches = GetLinkedIds().ToBatches(100);
        foreach (var batch in batches)
        {
            await InsertLinkedIdBatchAsync(dbContext, batch).ConfigureAwait(false);
            count += batch.Count;
        }

        // Remove duplicates and add primary key index.
        // Create a new table with unique constraint, copy distinct values, then swap.
        Report($"Indexing {count} linked files...");
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE TMP_LINKED_FILES_UNIQUE (Id TEXT NOT NULL PRIMARY KEY);
            INSERT OR IGNORE INTO TMP_LINKED_FILES_UNIQUE (Id) SELECT Id FROM TMP_LINKED_FILES;
            DROP TABLE TMP_LINKED_FILES;
            ALTER TABLE TMP_LINKED_FILES_UNIQUE RENAME TO TMP_LINKED_FILES;
            """).ConfigureAwait(false);

        return count;
    }

    /// <summary>
    /// Parameterized multi-row INSERT so large libraries do not pay one round-trip per id.
    /// </summary>
    internal static async Task InsertLinkedIdBatchAsync(
        DavDatabaseContext dbContext,
        IReadOnlyList<Guid> batch,
        CancellationToken cancellationToken = default)
    {
        if (batch.Count == 0)
            return;

        var parameters = new SqliteParameter[batch.Count];
        var valueSql = new string[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            var name = $"@p{i}";
            valueSql[i] = $"({name})";
            parameters[i] = new SqliteParameter(name, batch[i].ToString().ToUpperInvariant());
        }

        // Parameter names are generated locally (@p0..@pN); values are bound via SqliteParameter.
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"INSERT INTO TMP_LINKED_FILES (Id) VALUES {string.Join(",", valueSql)}",
            parameters.AsEnumerable(),
            cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
    }

    /// <summary>
    /// Deletes DavItems by the exact Id text returned from a raw SELECT. Going through
    /// Guid.Parse + ExecuteDelete re-serializes Ids as uppercase, which silently misses
    /// rows stored lowercase (e.g. the folder seeded by the Fix-Empty-Categories migration).
    /// </summary>
    private static async Task<int> DeleteItemsByIdTextAsync(
        DavDatabaseContext dbContext,
        IReadOnlyList<UnlinkedItemInfo> items,
        CancellationToken cancellationToken = default)
    {
        var parameters = new SqliteParameter[items.Count];
        var placeholders = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var name = $"@p{i}";
            placeholders[i] = name;
            parameters[i] = new SqliteParameter(name, items[i].Id);
        }

        // Placeholder names are generated locally (@p0..@pN); values are bound via SqliteParameter.
#pragma warning disable EF1002
        return await dbContext.Database.ExecuteSqlRawAsync(
            $"DELETE FROM DavItems WHERE Id IN ({string.Join(",", placeholders)})",
            parameters.AsEnumerable(),
            cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
    }

    private IEnumerable<Guid> GetLinkedIds()
    {
        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(500));
        var linkedIds = OrganizedLinksUtil
            .GetLibraryDavItemLinks(configManager)
            .Select(x => x.DavItemId);

        var count = 0;
        foreach (var linkedId in linkedIds)
        {
            count++;
            debounce(() => Report($"Scanning all linked files...\nFound {count}..."));
            yield return linkedId;
        }

        Report($"Scanning all linked files...\nFound {count}...");
    }

    /// <summary>
    /// The population CountUnlinkedItems draws from: every item this task is allowed to delete,
    /// linked or not. Must mirror CountUnlinkedItems' predicates exactly (minus the link join),
    /// otherwise the safety ratio compares two different populations.
    /// </summary>
    private async Task<int> CountDeletableItems(DateTime createdBefore)
    {
        await using var dbContext = CreateContext();
        var usenetFileType = (int)DavItem.ItemType.UsenetFile;

        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                 SELECT COUNT(i.Id) AS Value FROM DavItems i
                 WHERE i.Type = {usenetFileType}
                   AND i.HistoryItemId IS NULL
                   AND i.CreatedAt < {createdBefore}
                 """)
            .FirstAsync()
            .ConfigureAwait(false);
    }

    private async Task<int> CountUnlinkedItems(DateTime createdBefore)
    {
        await using var dbContext = CreateContext();
        var usenetFileType = (int)DavItem.ItemType.UsenetFile;

        // LEFT JOIN is equivalent to the NOT IN subquery used by RemoveUnlinkedItems /
        // DryRunIdentifyUnlinkedFiles; CountDeletableItems mirrors these predicates without
        // the link join so the safety ratio compares the same population.
        var count = await dbContext.Database
            .SqlQuery<int>(
                $"""
                 SELECT COUNT(i.Id) AS Value FROM DavItems i
                 LEFT JOIN TMP_LINKED_FILES t ON i.Id = t.Id
                 WHERE i.Type = {usenetFileType}
                   AND i.HistoryItemId IS NULL
                   AND i.CreatedAt < {createdBefore}
                   AND t.Id IS NULL
                 """)
            .FirstAsync()
            .ConfigureAwait(false);

        return count;
    }

    private async Task RemoveUnlinkedItems(DateTime createdBefore, int totalCount)
    {
        Report("Removing unlinked items...");
        _allRemovedPaths.Clear();
        await using var dbContext = CreateContext();
        var removed = 0;
        var usenetFileType = (int)DavItem.ItemType.UsenetFile;

        while (true)
        {
            // Select items to delete (batch of 100)
            var itemsToDelete = await dbContext.Database
                .SqlQuery<UnlinkedItemInfo>(
                    $"""
                     SELECT Id, Type, Path FROM DavItems
                     WHERE Type = {usenetFileType}
                       AND HistoryItemId IS NULL
                       AND CreatedAt < {createdBefore}
                       AND Id NOT IN (SELECT Id FROM TMP_LINKED_FILES)
                     LIMIT 100
                     """)
                .ToListAsync()
                .ConfigureAwait(false);

            // If there are no more items to delete, we're done.
            if (itemsToDelete.Count == 0)
                break;

            // Delete by the exact Id text from the select so stored casing never matters.
            foreach (var item in itemsToDelete)
            {
                DeletionAuditLog.Record(
                    "remove-orphaned",
                    new DavItem { Id = Guid.Parse(item.Id), Path = item.Path },
                    "no library symlink/strm link");
            }

            var deleted = await DeleteItemsByIdTextAsync(dbContext, itemsToDelete)
                .ConfigureAwait(false);

            // A batch that selects rows but deletes none would loop forever with a climbing
            // counter. Throw so ExecuteInternal reports a terminal "Failed:" status.
            if (deleted == 0)
            {
                throw new InvalidOperationException(
                    $"selected {itemsToDelete.Count} unlinked items but deleted 0; " +
                    $"aborting to avoid an infinite loop.");
            }

            // Trigger rclone vfs/forget for deleted items
            _ = DavDatabaseContext.RcloneVfsForget(itemsToDelete.Select(x => new DavItem
            {
                Id = Guid.Parse(x.Id),
                Type = (DavItem.ItemType)x.Type,
                Path = x.Path
            }).ToList());

            // Track removed paths
            _allRemovedPaths.AddRange(itemsToDelete.Select(x => x.Path));
            removed += deleted;

            Report($"Removing unlinked items...\nRemoved {removed}/{totalCount}...");
        }

        Report($"Removing unlinked items...\nRemoved {removed} of {removed}...");
    }

    private async Task RemoveEmptyDirectories(DateTime createdBefore)
    {
        Report($"Removing empty directories...");
        await using var dbContext = CreateContext();
        await RemoveEmptyDirectoriesAsync(
            dbContext,
            createdBefore,
            removedSoFar => Report($"Removing empty directories...\nRemoved {removedSoFar}..."),
            dirs => DavDatabaseContext.RcloneVfsForget(dirs),
            CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes empty regular directories in batches and returns the number removed. Throws if a
    /// selected batch deletes 0 rows, which would otherwise loop forever with a climbing counter.
    /// </summary>
    internal static async Task<int> RemoveEmptyDirectoriesAsync(
        DavDatabaseContext dbContext,
        DateTime createdBefore,
        Action<int>? onProgress = null,
        Func<List<DavItem>, Task>? onDeleted = null,
        CancellationToken cancellationToken = default)
    {
        var removed = 0;
        var directorySubType = (int)DavItem.ItemSubType.Directory;

        while (true)
        {
            // NOT EXISTS uses IX_DavItems_ParentId_Name's ParentId prefix; avoid the
            // previous LEFT JOIN anti-join which rescanned poorly at large scale.
            var emptyDirs = await dbContext.Database
                .SqlQuery<UnlinkedItemInfo>(
                    $"""
                     SELECT d.Id AS Id, d.Type AS Type, d.Path AS Path FROM DavItems d
                     WHERE d.SubType = {directorySubType}
                       AND d.CreatedAt < {createdBefore}
                       AND NOT EXISTS (
                           SELECT 1 FROM DavItems c WHERE c.ParentId = d.Id
                       )
                     LIMIT 100
                     """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (emptyDirs.Count == 0)
                break;

            // Delete by the exact Id text from the select so stored casing never matters
            // (the Fix-Empty-Categories migration seeds a lowercase-Id folder).
            foreach (var dir in emptyDirs)
            {
                DeletionAuditLog.Record(
                    "remove-orphaned",
                    new DavItem { Id = Guid.Parse(dir.Id), Path = dir.Path },
                    "empty directory after orphaned-file cleanup");
            }

            var deleted = await DeleteItemsByIdTextAsync(dbContext, emptyDirs, cancellationToken)
                .ConfigureAwait(false);

            if (deleted == 0)
            {
                throw new InvalidOperationException(
                    $"selected {emptyDirs.Count} empty directories but deleted 0; " +
                    $"aborting to avoid an infinite loop.");
            }

            if (onDeleted is not null)
            {
                _ = onDeleted(emptyDirs.Select(x => new DavItem
                {
                    Id = Guid.Parse(x.Id),
                    Type = (DavItem.ItemType)x.Type,
                    Path = x.Path
                }).ToList());
            }

            removed += deleted;
            onProgress?.Invoke(removed);
        }

        return removed;
    }

    private async Task DryRunIdentifyUnlinkedFiles(DateTime createdBefore)
    {
        _allRemovedPaths.Clear();
        await using var dbContext = CreateContext();
        var usenetFileType = (int)DavItem.ItemType.UsenetFile;
        // Keyset pagination: LIMIT/OFFSET without ORDER BY has no stable order in SQLite,
        // which can duplicate or skip rows across batches.
        var lastId = string.Empty;

        while (true)
        {
            var batch = await dbContext.Database
                .SqlQuery<UnlinkedItemInfo>(
                    $"""
                     SELECT Id, Type, Path FROM DavItems
                     WHERE Type = {usenetFileType}
                       AND HistoryItemId IS NULL
                       AND CreatedAt < {createdBefore}
                       AND Id > {lastId}
                       AND Id NOT IN (SELECT Id FROM TMP_LINKED_FILES)
                     ORDER BY Id
                     LIMIT 100
                     """)
                .ToListAsync()
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            _allRemovedPaths.AddRange(batch.Select(x => x.Path));
            lastId = batch[^1].Id;
            Report($"Identifying unlinked files...\nFound {_allRemovedPaths.Count}...");
        }
    }

    private void Report(string message)
    {
        var dryRun = isDryRun ? "Dry Run - " : string.Empty;
        _ = websocketManager.SendMessage(WebsocketTopic.CleanupTaskProgress, $"{dryRun}{message}");
    }

    public static string GetAuditReport()
    {
        return _allRemovedPaths.Count > 0
            ? string.Join("\n", _allRemovedPaths)
            : "This list is Empty.\nYou must first run the task.";
    }

    internal static void ClearAuditPathsForTests() => _allRemovedPaths = [];
}
