# Content rebuild from retained NZB blobs

**Status:** design finalized — ready for implementation (rev 2; open questions resolved below)  
**Tracks:** local [#117](https://github.com/nzbdav/nzbdav/issues/117) phase 3  
**Upstream reference:** [nzbdav-dev/nzbdav#311](https://github.com/nzbdav-dev/nzbdav/pull/311) (content snapshot recovery after restart)

Phases 1–2 of #117 shipped structured deletion audit logging (`DeletionAuditLog`) and ops docs under [Why did my files disappear?](setup-guide.md#why-did-my-files-disappear). This document designs the **recovery** path: rebuild missing `/content` mounts from NZB blobs that are still retained in the BlobStore.

Rev 2 turns the design into an implementation-ready spec: candidate rules are exact,
the four open questions from rev 1 are resolved, and the [Implementation plan](#implementation-plan)
below names every file, signature, and test so an implementing agent does not need to
re-derive the analysis.

---

## Problem

`/content` is a database-backed WebDAV view. Mounted media appear as `DavItem` rows (directories + file metadata that point at Usenet segments via the original NZB). The NZB document itself is stored as a blob under `{CONFIG_PATH}/blobs/` and referenced by:

| Reference | How |
| --- | --- |
| `QueueItem.Id` | Queue item id **is** the NZB blob id |
| `HistoryItem.NzbBlobId` | Completed/failed history rows keep the blob while retained |
| `DavItem.NzbBlobId` | Mounted content under `/content` keeps the blob while present (set on **files**, not mount directories) |

Several independent paths can delete `DavItem`s under `/content` (history delete with `del_files=1`, health repair, Remove Orphaned Files, cascading child sweep, manual WebDAV/API delete). When those deletes run, or when the SQLite catalogue is partially lost while `/config` blobs survive, users can end up with:

- **NZB blobs still on disk** (still referenced by history and/or still present in BlobStore), and
- **No corresponding mount tree** under `/content`.

Playback and library imports then look empty even though the Usenet pointers are recoverable from the retained NZB.

### Which delete paths actually leave a rebuildable state

Worth being precise, because it scopes what this feature can and cannot recover.
`NzbBlobCleanupService` deletes a blob once **no** `QueueItem`, `HistoryItem`, or
`DavItem` references it (SQLite triggers enqueue the check on every delete). Therefore:

| Delete path | History row survives? | Blob survives? | Rebuildable? |
| --- | --- | --- | --- |
| Manual WebDAV/Explore delete of a mount | Yes (dangling `DownloadDirId`) | Yes (history ref) | **Yes — primary use case** |
| Health repair removes items | Yes | Yes (history ref) | Yes, but see the article-check rule in [Safety](#safety) — these were deleted *because articles were missing* |
| SAB history delete with `del_files=1` | No | No (last refs removed) | No — intentional delete, blob correctly cleaned |
| History retention prune → later orphan cleanup | No | No (cleaned after last DavItem ref drops) | No |
| DB restored from an older backup | Rows only as of backup time | Blobs newer than backup leak unreferenced | Not in v1 (no history row to correlate; `NzbNames` lived in the same lost DB) |

Upstream [#311](https://github.com/nzbdav-dev/nzbdav/pull/311) attacks a related failure mode with a **persisted `/content` snapshot** restored at startup. This fork's phase-3 approach is complementary: **rebuild mounts by re-running the queue mount pipeline against retained NZB blobs**, without requiring a prior snapshot of the DavItem tree.

---

## Recovery options compared

Operators now have three recovery mechanisms. UI copy and the setup guide should point to the right one:

| | This design (rebuild from blobs) | Fork DB backup/restore (`DatabaseBackupTask` / `DbRestoreController`) | Upstream #311 (snapshot restore) |
| --- | --- | --- | --- |
| Trigger | Manual maintenance task | Manual restore from scheduled backup | Startup restore from snapshot |
| Source of truth | Retained NZB blobs + history refs | Full SQLite backup | Serialized `/content` DavItem snapshot |
| Granularity | Per-release, only missing mounts | **Whole DB** — also rolls back settings, queue, history, health state | `/content` subtree |
| Recovers intentional deletes? | No (blob cleaned after last ref) | Yes, if backup predates the delete (with full rollback cost) | No (valid post-delete snapshot replaces prior) |
| Needs prior artifact? | No | Yes (a backup) | Yes (a snapshot) |
| Needs Usenet at recovery time? | Yes (first-segment fetch + article checks) | No | No |

Rule of thumb for docs/UI copy: *lost a few mounts, history intact → rebuild task; lost or corrupted the whole catalogue → restore a DB backup; neither exists → nothing to recover.*

---

## Goals

1. Offer a **manual maintenance task** that discovers recoverable releases and remounts them under `/content`.
2. Prefer **safety over completeness**: never overwrite or mutate healthy existing mounts; v1 performs **zero deletes**.
3. Support a **dry-run report** so operators can see what would be rebuilt before changing the DB.
4. Emit the same progress/report UX as **Remove Orphaned Files** (Settings → Maintenance).
5. **Verify article existence during every remount**, regardless of the `ensure-article-existence` category config (see Safety — resolved decision).
6. **Recreate STRM sidecar files** for rebuilt mounts when the import strategy is `strm` (rebuilt DavItems get new GUIDs, so pre-existing `.strm` files point at dead `/.ids/{guid}` URLs; without this the rebuild is useless to STRM users).

---

## Non-goals

- **Automatic startup restore** of the entire `/content` tree from a snapshot (upstream #311's model; out of scope here).
- **Recovering blobs that are already gone** from BlobStore (no NNTP re-fetch of the NZB document itself in v1).
- **Scanning BlobStore for unreferenced blobs** (resolved decision 1 below — history-referenced blobs only in v1).
- **Undoing intentional deletes** when the blob was cleaned up correctly after the last reference was removed.
- **Recreating library symlinks** — symlinks are created by Sonarr/Radarr import, not by nzbdav. Rebuilt mounts get new DavItem ids, so surviving symlinks pointing at old `/.ids/{guid}` targets stay dead; the operator must rescan in the *Arr or re-import. Document this in UI copy and setup guide.
- **Deleting or replacing broken stub mounts** (resolved decision 3 — v1 strictly skips conflicts).
- **Changing deletion semantics** of history retention, health repair, or Remove Orphaned Files.
- **Recovering from a wiped `/config`** — if both SQLite and blobs are gone, nothing can be rebuilt.
- **Scheduling** — manual only in v1.

---

## Candidate model (resolved)

A history row `h` is a **candidate** when *all* of the following hold. This replaces rev 1's
looser sketch — note the `DownloadStatus` filter, which rev 1's algorithm omitted:
failed jobs also carry `NzbBlobId` (see `QueueItemProcessor.CreateHistoryItem`, which sets
`NzbBlobId = queueItem.Id` unconditionally) and `DownloadDirId = null`, so without this
filter the task would attempt full remounts of known-failed, typically DMCA'd NZBs.

1. `h.NzbBlobId != null`
2. `h.DownloadStatus == HistoryItem.DownloadStatusOption.Completed`
3. **No** `DavItem` has `NzbBlobId == h.NzbBlobId` (anti-join; both columns are indexed — `IX_DavItems_NzbBlobId`, `IX_HistoryItems_NzbBlobId`)

Each candidate is then **classified** (same logic for dry-run and live run, and re-checked
immediately before each live remount to close planning-time races):

| Check (in order) | Classification |
| --- | --- |
| A `QueueItem` with `Id == h.NzbBlobId` exists (queue id **is** the blob id — the live queue owns it) | `queue-owned` → skip |
| `BlobStore.Exists(h.NzbBlobId)` is false | `blob-missing` → skip |
| A mount folder named `h.JobName` already exists under `/content/{h.Category}/` (same join as `QueueItemProcessor.GetMountFolder`) | `path-conflict` → skip |
| `h.DownloadDirId != null` and that `DavItem` row still exists (e.g. an empty stub directory, or a renamed mount folder) | `path-conflict` → skip |
| Otherwise | `rebuildable` |

Notes:

- Since mount **directories** never carry `NzbBlobId` (only files do — see the aggregators),
  the anti-join in rule 3 correctly treats an *empty stub directory* as "not mounted";
  the `DownloadDirId` classification check is what catches the stub and reports it as a conflict.
- Candidates are processed in `CreatedAt` ascending order for deterministic reports.
- `HistoryItem.Id == NzbBlobId` for every row created by `QueueItemProcessor` today, but the
  code must read `NzbBlobId` for blob access and `Id` for history wiring — do not assume equality.

---

## Safety

| Rule | Detail |
| --- | --- |
| **No overwrite of healthy mounts** | The candidate anti-join plus classification guarantee a rebuild only ever creates a mount where none exists. The task never renames, merges, or replaces. |
| **Zero deletes in v1** | Conflicts (occupied path, stub dir) are reported and skipped. Because the task deletes nothing, no `DeletionAuditLog` calls are needed; if a later revision adds stub-clearing, those deletes must use `DeletionAuditLog.Record` with source `content-rebuild`. |
| **Always verify articles** | The remount **always** runs the article-existence check over important files (`x.IsRar \|\| FilenameUtil.IsImportantFileType(x.FileName)`), regardless of `ApiEnsureArticleExistenceCategories`. Rationale: health-repair deletions are a major candidate source and were deleted *because articles were missing*; `DeletionAuditLog` is Serilog-only, so the task cannot distinguish "deleted by repair" from "lost by accident" — the article check is the guard that prevents resurrecting broken mounts that health check would delete again. Cost is acceptable for a manual, rare task. Aggregators are invoked with `checkedFullHealth: true` so rebuilt items get `LastHealthCheck` stamps. |
| **Per-candidate atomicity** | Each remount uses a fresh `DavDatabaseContext`; mount dir, file rows, and the `HistoryItem.DownloadDirId` update commit in **one** `SaveChangesAsync`. Any failure discards the context — no half-written mounts, no compensating deletes needed. |
| **Dry-run first** | Dry-run runs the same enumeration/classification with zero DB writes and publishes the same report. |
| **Concurrency** | `BaseTask` single-flight gives HTTP 409 when any maintenance task is running (same as Remove Orphaned Files). Per-candidate `queue-owned` classification (checked at plan time *and* re-checked at rebuild time) keeps the task off blobs the live queue owns. A blob deleted by `NzbBlobCleanupService` between planning and remount (e.g. concurrent history delete) surfaces as a clean per-candidate failure — see failure modes. |
| **No scheduled auto-run in v1** | Manual only. |

---

## Resolved design decisions (rev 1 open questions)

1. **History-referenced blobs only in v1.** No BlobStore directory scan. Unreferenced blobs
   almost always mean an intentional delete mid-cleanup or a DB restore; in the restore case
   the `NzbNames` row (needed to name the release) lived in the same lost DB, so the scan
   buys almost nothing for its correlation complexity. Revisit only with user demand.
2. **STRM recreation is in scope; symlinks are not.** For `configManager.GetImportStrategy() == "strm"`,
   run `CreateStrmFilesPostProcessor` after the remount commits (its `CollectVideoItems`
   queries `Items.Where(x => x.HistoryItemId == historyItemId)`, which works post-commit).
   Running it *after* commit avoids stray `.strm` files if the commit fails. Symlink users
   get a documented "rescan in Sonarr/Radarr" note instead — nzbdav cannot recreate links it never made.
3. **Strictly skip-all-conflicts in v1.** No "replace broken stub" mode. Keeps v1 delete-free.
4. **Yes to the structured success log.** One info line per rebuilt mount, symmetric with
   phase 1's `dav-delete` lines: `content-rebuild blob={BlobId} history={HistoryId} path={Path}`.

---

## Implementation plan

Nine steps, in dependency order. **Make one commit per step** (Conventional Commits;
suggested messages included). All backend code is nullable-enabled, async, Serilog,
`ConfigureAwait(false)` — match the surrounding files. After each backend step:
`dotnet build backend/NzbWebDAV.csproj`. Before the PR:
`dotnet test tests/NzbWebDAV.Tests/NzbWebDAV.Tests.csproj -c Release` and
`cd frontend && npm run typecheck && npm run build && npm test`.

> Note for the implementer: code blocks below are **reference implementations**, accurate
> against the repo as of this design's merge. If a cited signature has drifted, adapt to the
> current code rather than forcing the sample in — but do not change the *rules* (candidate
> filters, classification order, always-check-articles, single-save atomicity).

### Step 1 — websocket topic

**File:** `backend/Websocket/WebsocketTopic.cs`. Add one stateful topic next to `CleanupTaskProgress`:

```csharp
public static readonly WebsocketTopic ContentRebuildProgress = new("crb", TopicType.State);
```

A dedicated topic (rather than reusing `ctp`) keeps concurrent-looking progress from
Remove Orphaned Files and this task from interleaving in the UI.

Commit: `feat(webdav): add content-rebuild websocket progress topic`

### Step 2 — `BlobStore.Exists`

**File:** `backend/Database/BlobStore.cs`. Classification needs an existence probe that
doesn't open the file:

```csharp
public static bool Exists(Guid id)
{
    return File.Exists(GetBlobPath(id));
}
```

Commit: `feat(db): add BlobStore.Exists probe`

### Step 3 — make `QueueItemProcessor.GetFileProcessors` reusable

**File:** `backend/Queue/QueueItemProcessor.cs`. The remounter needs the same
file-processor grouping. Every other pipeline step it reuses is already a static/public API
(`FetchFirstSegmentsStep.FetchFirstSegments`, `GetPar2FileDescriptorsStep.GetPar2FileDescriptors`,
`GetFileInfosStep.GetFileInfos`, `LazyRarProcessor`, the four aggregators, the post-processors) —
this is the **only** refactor of `QueueItemProcessor`, deliberately minimal. Convert the
private instance method to internal static:

```csharp
internal static IEnumerable<BaseProcessor> GetFileProcessors
(
    List<GetFileInfosStep.FileInfo> fileInfos,
    INntpClient usenetClient,
    ConfigManager configManager,
    string? archivePassword,
    CancellationToken ct,
    bool skipRarGroup = false
)
```

Body is unchanged except the captured instance fields (`usenetClient`, `configManager`, `ct`)
become the new parameters. Update the single call site in `ProcessQueueItemAsync`:

```csharp
var fileProcessors = GetFileProcessors(fileInfos, usenetClient, configManager, archivePassword, ct, skipRarGroup).ToList();
```

Do **not** touch anything else in this file. `GetGroupName` stays private static.

Commit: `chore(queue): make GetFileProcessors reusable for content rebuild`

### Step 4 — `ContentRemounter`

**New file:** `backend/Queue/ContentRemounter.cs`. This is the "dedicated remount entry point"
from rev 1: it re-runs the analysis pipeline and stages DB rows, but performs **no queue-item
bookkeeping** — no `QueueItems` mutation, no history insertion, no `QueueItemStatus`/`HistoryItemAdded`
websockets, no *Arr `RefreshMonitoredDownloads`, no watchdog entries. It also does **not**
call `SaveChangesAsync`; the caller owns the transaction boundary.

```csharp
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Queue.DeobfuscationSteps._2.GetPar2FileDescriptors;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Queue.FileAggregators;
using NzbWebDAV.Queue.FileProcessors;
using NzbWebDAV.Queue.PostProcessors;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue;

/// <summary>
/// Rebuilds a /content mount for a completed HistoryItem from its retained NZB blob.
/// Reuses the queue's analysis pipeline and aggregators but performs none of the
/// queue/history bookkeeping of QueueItemProcessor. Stages all DavItem rows on the
/// caller's context; the caller commits (or discards) them in a single SaveChangesAsync.
/// </summary>
public class ContentRemounter(
    HistoryItem historyItem,
    Stream nzbStream,
    DavDatabaseClient dbClient,
    INntpClient usenetClient,
    ConfigManager configManager,
    CancellationToken ct
)
{
    /// <summary>Stages the mount and returns the new mount folder. Throws on any failure.</summary>
    public async Task<DavItem> RemountAsync()
    {
        // read the nzb document (mirrors QueueItemProcessor.ProcessQueueItemAsync)
        var nzb = await NzbDocument.LoadAsync(nzbStream).ConfigureAwait(false);
        var nzbFiles = nzb.Files.Where(x => x.Segments.Count > 0).ToList();
        if (usenetClient is ArticleCachingNntpClient cachingUsenetClient)
            cachingUsenetClient.TrackNzbFiles(nzbFiles);

        // password: filename override wins, then nzb metadata
        var archivePassword = FilenameUtil.GetNzbPassword(historyItem.FileName)
                              ?? nzb.Metadata.GetValueOrDefault("password");

        // fail fast on segment ids already known missing (issue #101 cache)
        var articlesToPrecheck = nzbFiles.SelectMany(x => x.Segments).Select(x => x.MessageId);
        HealthCheckService.CheckCachedMissingSegmentIds(articlesToPrecheck);

        // step 1 -- names and sizes (fetches first segments from usenet)
        var segments = await FetchFirstSegmentsStep.FetchFirstSegments(
            nzbFiles, usenetClient, configManager, ct).ConfigureAwait(false);
        var par2FileDescriptors = await GetPar2FileDescriptorsStep.GetPar2FileDescriptors(
            segments, usenetClient, ct).ConfigureAwait(false);
        var fileInfos = GetFileInfosStep.GetFileInfos(segments, par2FileDescriptors);

        // step 1b -- fail fast when any important file has a permanently missing
        // first segment (same exclusion list as the queue pipeline).
        HashSet<string> unimportantExtensions = [".par2", ".nfo", ".txt", ".sfv", ".nzb", ".srr"];
        var missingNzbFiles = segments
            .Where(x => x.MissingFirstSegment)
            .Select(x => x.NzbFile)
            .ToHashSet();
        var importantFilesMissing = fileInfos
            .Where(x => missingNzbFiles.Contains(x.NzbFile))
            .Where(x => !unimportantExtensions.Contains(Path.GetExtension(x.FileName).ToLowerInvariant()))
            .ToList();
        if (importantFilesMissing.Count > 0)
            throw new InvalidOperationException(
                $"missing articles: {importantFilesMissing.Count} important file(s) have missing first segments");

        // step 2a -- lazy rar mounting when enabled (same as queue pipeline)
        LazyRarProcessor.Result? lazyRarResult = null;
        var rarFiles = fileInfos.Where(x => GetGroup(x) == "rar").ToList();
        if (configManager.IsLazyRarParsingEnabled() && rarFiles.Count > 0)
        {
            var lazyProc = new LazyRarProcessor(rarFiles, usenetClient, archivePassword, ct);
            lazyRarResult = await lazyProc.ProcessAsync().ConfigureAwait(false) as LazyRarProcessor.Result;
        }

        // step 2b -- per-file processing.
        // IMPORTANT: call the ProcessAsync(IProgress<int>) overload. BaseProcessor children
        // override either overload ("but not both"); the no-arg overload returns null for
        // children that only implement the progress one, silently dropping their results.
        var noopProgress = new Progress<int>();
        var fileProcessors = QueueItemProcessor
            .GetFileProcessors(fileInfos, usenetClient, configManager, archivePassword, ct,
                skipRarGroup: lazyRarResult is not null)
            .ToList();
        var fileProcessingResultsAll = await fileProcessors
            .Select(x => x!.ProcessAsync(noopProgress))
            .WithConcurrencyAsync(Math.Min(configManager.GetMaxQueueConnections() + 5, 50))
            .GetAllAsync(ct).ConfigureAwait(false);
        var fileProcessingResults = fileProcessingResultsAll
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
        if (lazyRarResult is not null) fileProcessingResults.Add(lazyRarResult);

        // step 3 -- ALWAYS verify article existence for important files, regardless of
        // the ensure-article-existence category config. Health-repair deletions are a
        // major candidate source; this is the guard against resurrecting broken mounts.
        // Throws UsenetArticleNotFoundException on a definitive 430/451 miss.
        var articlesToCheck = fileInfos
            .Where(x => x.IsRar || FilenameUtil.IsImportantFileType(x.FileName))
            .SelectMany(x => x.NzbFile.GetSegmentIds())
            .ToList();
        await usenetClient.CheckAllSegmentsAsync(
            articlesToCheck, configManager.GetHealthCheckConcurrency(), null, ct).ConfigureAwait(false);

        // stage db rows: category folder, mount folder, file items
        var categoryFolder = await GetOrCreateCategoryFolderAsync().ConfigureAwait(false);
        var mountFolder = DavItem.New(
            id: Guid.NewGuid(),
            parent: categoryFolder,
            name: historyItem.JobName,
            fileSize: null,
            type: DavItem.ItemType.Directory,
            subType: DavItem.ItemSubType.Directory,
            releaseDate: null,
            lastHealthCheck: null,
            historyItemId: historyItem.Id,
            fileBlobId: null
        );
        dbClient.Ctx.Items.Add(mountFolder);

        // checkedFullHealth: true — step 3 above verified all important segments.
        new RarAggregator(dbClient, mountFolder, true).UpdateDatabase(fileProcessingResults);
        new FileAggregator(dbClient, mountFolder, true).UpdateDatabase(fileProcessingResults);
        new SevenZipAggregator(dbClient, mountFolder, true).UpdateDatabase(fileProcessingResults);
        new MultipartMkvAggregator(dbClient, mountFolder, true).UpdateDatabase(fileProcessingResults);

        new RenameDuplicatesPostProcessor(dbClient).RenameDuplicates();
        new BlocklistedFilePostProcessor(configManager, dbClient).RemoveBlocklistedFiles();
        if (configManager.IsEnsureImportableVideoEnabled())
            new EnsureImportableVideoValidator(dbClient).ThrowIfValidationFails();

        return mountFolder;
    }

    // mirrors QueueItemProcessor.GetOrCreateCategoryFolder (history fields instead of queue fields)
    private async Task<DavItem> GetOrCreateCategoryFolderAsync()
    {
        var categoryFolder = await dbClient.GetDirectoryChildAsync(
            DavItem.ContentFolder.Id, historyItem.Category, ct).ConfigureAwait(false);
        if (categoryFolder is not null)
            return categoryFolder;

        categoryFolder = DavItem.New(
            id: Guid.NewGuid(),
            parent: DavItem.ContentFolder,
            name: historyItem.Category,
            fileSize: null,
            type: DavItem.ItemType.Directory,
            subType: DavItem.ItemSubType.Directory,
            releaseDate: null,
            lastHealthCheck: null,
            historyItemId: null,
            fileBlobId: null
        );
        dbClient.Ctx.Items.Add(categoryFolder);
        return categoryFolder;
    }

    // same grouping predicate as QueueItemProcessor.GetGroupName
    private static string GetGroup(GetFileInfosStep.FileInfo x) =>
        FilenameUtil.Is7zFile(x.FileName) ? "7z"
        : x.IsRar || FilenameUtil.IsRarFile(x.FileName) ? "rar"
        : FilenameUtil.IsMultipartMkv(x.FileName) ? "multipart-mkv"
        : "other";
}
```

Implementation notes:

- Progress granularity inside a single remount is intentionally not reported (no-op
  `Progress<int>`); the task reports per-candidate counts instead.
- If `GetGroupName` in `QueueItemProcessor` can be made `internal` instead of duplicating
  `GetGroup`, prefer that (same commit as step 3).

Commit: `feat(queue): add ContentRemounter for rebuilding mounts from nzb blobs`

### Step 5 — `RebuildContentTask`

**New file:** `backend/Tasks/RebuildContentTask.cs`. Follows `RemoveUnlinkedFilesTask`
(same `BaseTask` single-flight, same `Func<DavDatabaseContext>?` test seam, same
static last-report + audit endpoint pattern).

```csharp
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.Queue.PostProcessors;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class RebuildContentTask(
    ConfigManager configManager,
    WebsocketManager websocketManager,
    INntpClient usenetClient,
    bool isDryRun,
    Func<DavDatabaseContext>? createContext = null
) : BaseTask
{
    private static List<string> _lastReportLines = [];

    internal enum CandidateStatus
    {
        Rebuildable,
        QueueOwned,
        BlobMissing,
        PathConflict,
    }

    internal sealed record PlanEntry(HistoryItem History, CandidateStatus Status);

    private DavDatabaseContext CreateContext() => createContext?.Invoke() ?? new DavDatabaseContext();

    protected override async Task ExecuteInternal()
    {
        try
        {
            await RebuildContent().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Report($"Failed: {e.Message}");
            Log.Error(e, "Failed to rebuild content from nzb blobs.");
        }
    }

    private async Task RebuildContent()
    {
        Report("Scanning history for rebuildable mounts...");
        List<PlanEntry> plan;
        await using (var ctx = CreateContext())
        {
            plan = await BuildPlanAsync(ctx, CancellationToken).ConfigureAwait(false);
        }

        var rebuildable = plan.Where(x => x.Status == CandidateStatus.Rebuildable).ToList();
        var conflicts = plan.Count(x => x.Status == CandidateStatus.PathConflict);
        var blobMissing = plan.Count(x => x.Status == CandidateStatus.BlobMissing);
        var queueOwned = plan.Count(x => x.Status == CandidateStatus.QueueOwned);

        _lastReportLines = plan
            .Select(x => $"{x.Status}: /content/{x.History.Category}/{x.History.JobName}" +
                         $" (history={x.History.Id})")
            .ToList();

        if (isDryRun)
        {
            Report($"Done. Identified {rebuildable.Count} rebuildable mounts, " +
                   $"{conflicts} path conflicts, {blobMissing} missing blobs, {queueOwned} queue-owned.");
            return;
        }

        var rebuilt = 0;
        var failed = 0;
        foreach (var entry in rebuildable)
        {
            CancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RebuildOneAsync(entry.History).ConfigureAwait(false);
                rebuilt++;
                Log.Information(
                    "content-rebuild blob={BlobId} history={HistoryId} path={Path}",
                    entry.History.NzbBlobId, entry.History.Id,
                    $"/content/{entry.History.Category}/{entry.History.JobName}");
            }
            catch (Exception e) when (!e.IsCancellationException())
            {
                failed++;
                _lastReportLines.Add($"remount-failed: /content/{entry.History.Category}/" +
                                     $"{entry.History.JobName} — {e.Message}");
                Log.Warning(e, "content-rebuild failed for {JobName} ({BlobId})",
                    entry.History.JobName, entry.History.NzbBlobId);
            }

            Report($"Rebuilding mounts...\n{rebuilt + failed}/{rebuildable.Count} " +
                   $"({rebuilt} ok, {failed} failed)...");
        }

        Report($"Done. Rebuilt {rebuilt} mounts, {failed} failed, " +
               $"{conflicts} path conflicts skipped, {blobMissing} missing blobs.");
    }

    /// <summary>
    /// Enumerates completed history rows whose blob has no mounted DavItems, classified
    /// per the candidate model. Internal static so tests can drive it against a TempDb.
    /// </summary>
    internal static async Task<List<PlanEntry>> BuildPlanAsync(
        DavDatabaseContext ctx, CancellationToken ct)
    {
        // Candidate rules: NzbBlobId set, Completed only (failed rows also carry
        // NzbBlobId and must never be remounted), and no DavItem references the blob.
        var candidates = await ctx.HistoryItems
            .AsNoTracking()
            .Where(h => h.NzbBlobId != null)
            .Where(h => h.DownloadStatus == HistoryItem.DownloadStatusOption.Completed)
            .Where(h => !ctx.Items.Any(d => d.NzbBlobId == h.NzbBlobId))
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var plan = new List<PlanEntry>(candidates.Count);
        foreach (var history in candidates)
            plan.Add(new PlanEntry(history, await ClassifyAsync(ctx, history, ct).ConfigureAwait(false)));
        return plan;
    }

    internal static async Task<CandidateStatus> ClassifyAsync(
        DavDatabaseContext ctx, HistoryItem history, CancellationToken ct)
    {
        var blobId = history.NzbBlobId!.Value;

        // the live queue owns this blob (queue item id IS the blob id)
        if (await ctx.QueueItems.AnyAsync(q => q.Id == blobId, ct).ConfigureAwait(false))
            return CandidateStatus.QueueOwned;

        if (!BlobStore.Exists(blobId))
            return CandidateStatus.BlobMissing;

        // v1 refuses any occupied target path (same join as QueueItemProcessor.GetMountFolder)
        var pathOccupied = await (
            from mountFolder in ctx.Items
            join categoryFolder in ctx.Items on mountFolder.ParentId equals categoryFolder.Id
            where mountFolder.Name == history.JobName
                  && mountFolder.ParentId != null
                  && categoryFolder.Name == history.Category
                  && categoryFolder.ParentId == DavItem.ContentFolder.Id
            select mountFolder
        ).AnyAsync(ct).ConfigureAwait(false);
        if (pathOccupied)
            return CandidateStatus.PathConflict;

        // dangling DownloadDirId still pointing at a live row (empty stub or renamed mount)
        if (history.DownloadDirId != null &&
            await ctx.Items.AnyAsync(d => d.Id == history.DownloadDirId.Value, ct).ConfigureAwait(false))
            return CandidateStatus.PathConflict;

        return CandidateStatus.Rebuildable;
    }

    private async Task RebuildOneAsync(HistoryItem history)
    {
        // fresh context per candidate: all-or-nothing commit, no tracker juggling
        await using var ctx = CreateContext();
        var dbClient = new DavDatabaseClient(ctx);

        // re-check eligibility to close the planning->rebuild race window
        var status = await ClassifyAsync(ctx, history, CancellationToken).ConfigureAwait(false);
        if (status != CandidateStatus.Rebuildable)
            throw new InvalidOperationException($"candidate no longer eligible: {status}");

        await using var nzbStream = BlobStore.ReadBlob(history.NzbBlobId!.Value)
            ?? throw new InvalidOperationException("nzb blob disappeared before remount");

        // per-candidate article cache, mirroring QueueManager's per-item cache
        using var cachingClient = new ArticleCachingNntpClient(usenetClient);
        var remounter = new ContentRemounter(
            history, nzbStream, dbClient, cachingClient, configManager, CancellationToken);
        var mountFolder = await remounter.RemountAsync().ConfigureAwait(false);

        // link history to the rebuilt mount (attach pattern per QueueItemProcessor.PauseUntil)
        history.DownloadDirId = mountFolder.Id;
        ctx.HistoryItems.Attach(history);
        ctx.Entry(history).Property(x => x.DownloadDirId).IsModified = true;

        // single atomic save: category/mount/file rows + history link commit together
        await ctx.SaveChangesAsync(CancellationToken).ConfigureAwait(false);

        // post-commit: strm sidecars (CollectVideoItems queries the now-persisted rows),
        // then rclone cache invalidation for the new paths
        if (configManager.GetImportStrategy() == "strm")
            await new CreateStrmFilesPostProcessor(configManager, dbClient, history.Id)
                .CreateStrmFilesAsync().ConfigureAwait(false);
        _ = DavDatabaseContext.RcloneVfsForget([mountFolder]);
    }

    private void Report(string message)
    {
        var dryRun = isDryRun ? "Dry Run - " : string.Empty;
        _ = websocketManager.SendMessage(WebsocketTopic.ContentRebuildProgress, $"{dryRun}{message}");
    }

    public static string GetAuditReport()
    {
        return _lastReportLines.Count > 0
            ? string.Join("\n", _lastReportLines)
            : "This list is Empty.\nYou must first run the task.";
    }

    internal static void ClearReportForTests() => _lastReportLines = [];
}
```

Implementation notes:

- `IsCancellationException()` comes from `NzbWebDAV.Extensions` (see `QueueItemProcessor` usage).
- Terminal report strings must start with `Done` / `Failed` / `Aborted` — the frontend derives
  run-state from those prefixes (see `remove-unlinked-files.tsx`).
- Do **not** touch `QueueItems`, insert `HistoryItems`, or send `HistoryItemAdded`/`QueueItemRemoved`
  websocket events anywhere in this task.

Commit: `feat(webdav): add rebuild-content maintenance task`

### Step 6 — controllers

**New folder:** `backend/Api/Controllers/RebuildContent/` with three files, exact mirrors of
the `RemoveUnlinkedFiles` trio (same `BaseApiController` base, same 409 shape). The only new
wrinkle: inject the `UsenetStreamingClient` singleton (registered in `Program.cs`) and pass it
as the task's `INntpClient`.

`RebuildContentController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers.RebuildContent;

[ApiController]
[Route("api/rebuild-content")]
public class RebuildContentController(
    ConfigManager configManager,
    WebsocketManager websocketManager,
    UsenetStreamingClient usenetClient
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var task = new RebuildContentTask(configManager, websocketManager, usenetClient, isDryRun: false);
        var executed = await task.Execute().ConfigureAwait(false);
        if (!executed)
            return Conflict(new { error = "Rebuild Content task is already running." });
        return Ok(executed);
    }
}
```

`RebuildContentDryRunController.cs`: identical with `[Route("api/rebuild-content/dry-run")]`
and `isDryRun: true`.

`RebuildContentAuditController.cs`: mirror of `RemoveUnlinkedFilesAuditController` —
`[Route("api/rebuild-content/audit")]`, returns `Ok(RebuildContentTask.GetAuditReport())`.

No frontend proxy changes are needed: `/api` is already forwarded by `frontend/server/app.ts`,
and no new WebDAV mount path is introduced.

Commit: `feat(api): add rebuild-content endpoints`

### Step 7 — frontend maintenance section

**New file:** `frontend/app/routes/settings/maintenance/rebuild-content/rebuild-content.tsx`.
Copy `remove-unlinked-files/remove-unlinked-files.tsx` and apply exactly these changes:

1. Component name `RebuildContent`; props stay `{ savedConfig: Record<string, string> }`
   (accepted for pattern-consistency even though unused — or drop props entirely; match file style).
2. Websocket topic: `useWebsocketTopic("crb", "state", setProgress, ...)`.
3. Endpoints: `/api/rebuild-content`, `/api/rebuild-content/dry-run`, audit link
   `/api/rebuild-content/audit`.
4. **Remove** the `libraryDir` gate (this task does not need the library dir):
   `isRunButtonEnabled` becomes `connected && !isRunning`, and the "configure Library Directory"
   warning alert is dropped.
5. Alert copy (single `variant="warning"` alert):
   - "Make a backup of your NzbDAV database prior to running this task."
   - "This task remounts releases from NZB files still retained on disk. It cannot recover
     anything if /config was wiped, and it cannot undo deletes where the NZB was already cleaned up."
   - "Rebuilt items get new internal ids: STRM files are recreated automatically (when using
     the STRM import strategy), but existing library symlinks are not — rescan in Sonarr/Radarr
     after rebuilding."
6. Help paragraph: explain dry-run first; mention that conflicts and queue-owned releases are
   skipped and listed in the audit report.

**Register it** in `frontend/app/routes/settings/maintenance/maintenance.tsx`: import
`RebuildContent` and add a `<details>` block titled **Rebuild Content from NZB Blobs**
immediately after the **Remove Orphaned Files** block, same markup as its siblings.
No changes to `isMaintenanceSettingsUpdated` (the task adds no config keys).

Commit: `feat(ui): add rebuild-content maintenance section`

### Step 8 — docs

1. This file: change the status header to "shipped in vX.Y.Z" once released (leave "design
   finalized" until then) and keep the candidate/safety sections as living documentation.
2. `docs/setup-guide.md`, section "Recovering mounts when NZB blobs remain": replace the
   "(Design only today — not shipped yet.)" sentence with a pointer to
   Settings → Maintenance → Rebuild Content from NZB Blobs, and add the symlink-rescan caveat.

Commit: `docs: update content-rebuild and setup guide for shipped rebuild task`

### Step 9 — tests

**New file:** `tests/NzbWebDAV.Tests/Tasks/RebuildContentTaskTests.cs`, following
`RemoveUnlinkedFilesTaskTests` (same `TempDb` harness, `[Collection(nameof(BaseTaskCollection))]`,
seeded root folders). All tests target `BuildPlanAsync`/`ClassifyAsync` — the remount pipeline
itself needs live NNTP and is covered by the manual test plan instead (per repo policy:
no live Usenet in the automated suite).

Blob-existence note: `BlobStore` reads `{CONFIG_PATH}/blobs/`; tests that need an existing
blob should write one via `BlobStore.WriteBlob(id, stream)` against the TempDb harness's
config path (see how existing tests set `CONFIG_PATH`), or the test can accept the
`BlobMissing` classification as the expected result when no blob is written.

| Test | Arrange | Assert |
| --- | --- | --- |
| `BuildPlanAsync_SkipsFailedHistoryRows` | Two history rows with `NzbBlobId` set, one `Completed` + one `Failed`, no DavItems | Plan contains only the Completed row |
| `BuildPlanAsync_SkipsMountedBlobs` | Completed history row; a DavItem file with the same `NzbBlobId` | Plan is empty |
| `ClassifyAsync_QueueOwned` | Completed history row + `QueueItem` with `Id == NzbBlobId` | `QueueOwned` |
| `ClassifyAsync_BlobMissing` | Completed history row, no blob file on disk | `BlobMissing` |
| `ClassifyAsync_PathConflict_SameNameMount` | Blob written; a `/content/{cat}/{job}` folder exists with a different blob's files | `PathConflict` |
| `ClassifyAsync_PathConflict_EmptyStubViaDownloadDirId` | Blob written; empty directory row whose id equals `history.DownloadDirId` | `PathConflict` |
| `ClassifyAsync_Rebuildable` | Blob written; no queue row, no mounts, `DownloadDirId` null or dangling | `Rebuildable` |
| `DryRun_WritesNothing` | Seed one rebuildable candidate; run task with `isDryRun: true` (inject `createContext`) | `Items` row count unchanged; `GetAuditReport()` lists the candidate |

Commit: `feat(webdav): add rebuild-content task tests` (or fold into step 5's commit if preferred — keep tests green per commit either way).

---

## Failure modes

| Failure | Expected behavior |
| --- | --- |
| Blob missing at plan time | Classified `blob-missing`; skipped and counted |
| Blob deleted between plan and remount (e.g. concurrent history delete dropped the last reference and `NzbBlobCleanupService` collected it) | Re-classification or `ReadBlob` null-check inside `RebuildOneAsync` throws; counted as `remount-failed`; no DB writes (fresh context discarded) |
| NZB parse failure / corrupt blob | `NzbDocument.LoadAsync` throws; `remount-failed`; blob is **not** deleted |
| Important first segments missing | Fail-fast throw before any DB staging; `remount-failed` |
| Article-existence check fails (DMCA'd/expired) | `UsenetArticleNotFoundException`; `remount-failed`; nothing persisted — this is the expected outcome for most health-repair-deleted candidates |
| Blocklist/importable-video validation fails | Same as queue semantics; `remount-failed`, nothing persisted |
| Path conflict / stub dir / renamed mount | Classified `path-conflict`; skipped, listed in audit report |
| Candidate re-enqueued in live queue mid-run | Re-classification returns `queue-owned`; counted as `remount-failed` with that reason (never touches the queue's blob) |
| Task already running (any `BaseTask`) | HTTP 409; UI shows "Task already running." |
| Process crash mid-remount | Per-candidate single `SaveChangesAsync` means either the full mount committed or nothing did |
| History pruned, blob leaked (e.g. old DB restore) | Not a candidate in v1; documented limitation (see resolved decision 1) |
| Operator expected snapshot-style restore of intentional deletes | Out of scope — stated in UI copy |

---

## Manual test plan (implementation PR)

Automated tests cover planning/classification only. Verify the remount path manually:

1. Complete a real NZB through the queue; confirm the mount under `/content/{cat}/{job}` and
   (STRM strategy) the `.strm` sidecars.
2. Delete the mount folder via the Explore UI (history row remains). Run **dry-run**: exactly
   one `rebuildable` candidate listed; DB unchanged.
3. Run the task: mount tree reappears; `HistoryItem.DownloadDirId` points at the new folder id;
   SAB history delete-with-files works against the rebuilt mount; `content-rebuild` info line
   in logs; STRM strategy: sidecars recreated and playable.
4. Re-run the task: zero candidates ("nothing to do" report).
5. Re-add the same NZB via SAB API while a candidate exists → candidate reported `queue-owned`.
6. Seed a same-named mount from a different NZB → `path-conflict`, untouched.
7. Candidate whose articles are gone (expired/DMCA test NZB) → `remount-failed`, no partial rows
   (`SELECT COUNT(*) FROM DavItems WHERE NzbBlobId = ...` is 0).
8. Rclone mount (if configured): rebuilt folder appears without a manual `vfs/refresh`.

---

## Acceptance criteria (implementation PR)

- [ ] Candidate query filters `DownloadStatus == Completed` and anti-joins `DavItem.NzbBlobId` (both indexed).
- [ ] Classification handles `queue-owned`, `blob-missing`, `path-conflict` (both same-name and dangling-`DownloadDirId` forms), in that order, and is re-run per candidate immediately before remount.
- [ ] Dry-run performs zero DB writes and publishes the same classification report (websocket + audit endpoint).
- [ ] Remount always verifies article existence for important files regardless of `ensure-article-existence` categories; aggregators run with `checkedFullHealth: true`.
- [ ] Each rebuilt candidate commits mount dir + file rows + `HistoryItem.DownloadDirId` in a single `SaveChangesAsync`; failures persist nothing.
- [ ] The task performs zero deletes, zero `QueueItems`/`HistoryItems` inserts or removals, and sends no queue/history websocket events.
- [ ] STRM sidecars are recreated post-commit when the import strategy is `strm`; UI copy documents the symlink-rescan caveat.
- [ ] Successful rebuilds log `content-rebuild blob=... history=... path=...`.
- [ ] Maintenance UI mirrors Remove Orphaned Files (dry-run + run buttons, `crb` topic, 409 handling, audit link).
- [ ] Automated tests from step 9 pass; manual test plan executed and noted in the PR description.
- [ ] Setup guide pointer updated per step 8.

---

## References

- Local issue: [#117](https://github.com/nzbdav/nzbdav/issues/117) (phases 1–2: audit log + docs; phase 3: this recovery feature)
- Upstream recovery PR: [nzbdav-dev/nzbdav#311](https://github.com/nzbdav-dev/nzbdav/pull/311)
- Upstream symptom issue: [nzbdav-dev/nzbdav#304](https://github.com/nzbdav-dev/nzbdav/issues/304)
- Ops diagnosis: [Why did my files disappear?](setup-guide.md#why-did-my-files-disappear)
- Blob lifecycle: `NzbBlobCleanupService` (blob retained while queue, history, or DavItem references it; serializable-transaction cleanup)
- Mount creation: `QueueItemProcessor` (`CreateMountFolder`, aggregators, STRM post-processor)
- Task/UX pattern: `RemoveUnlinkedFilesTask`, `backend/Api/Controllers/RemoveUnlinkedFiles/`, `frontend/app/routes/settings/maintenance/remove-unlinked-files/`
- DB recovery alternative: `DatabaseBackupTask` / `DbRestoreController`
