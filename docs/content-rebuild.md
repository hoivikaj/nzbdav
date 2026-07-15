# Content rebuild from retained NZB blobs

**Status:** design only — implementation gated on review  
**Tracks:** local [#117](https://github.com/nzbdav/nzbdav/issues/117) phase 3  
**Upstream reference:** [nzbdav-dev/nzbdav#311](https://github.com/nzbdav-dev/nzbdav/pull/311) (content snapshot recovery after restart)

Phases 1–2 of #117 shipped structured deletion audit logging (`DeletionAuditLog`) and ops docs under [Why did my files disappear?](setup-guide.md#why-did-my-files-disappear). This document designs the **recovery** path: rebuild missing `/content` mounts from NZB blobs that are still retained in the BlobStore.

---

## Problem

`/content` is a database-backed WebDAV view. Mounted media appear as `DavItem` rows (directories + file metadata that point at Usenet segments via the original NZB). The NZB document itself is stored as a blob under `{CONFIG_PATH}/blobs/` and referenced by:

| Reference | How |
| --- | --- |
| `QueueItem.Id` | Queue item id **is** the NZB blob id |
| `HistoryItem.NzbBlobId` | Completed/failed history rows keep the blob while retained |
| `DavItem.NzbBlobId` | Mounted content under `/content` keeps the blob while present |

Several independent paths can delete `DavItem`s under `/content` (history delete with `del_files=1`, health repair, Remove Orphaned Files, cascading child sweep, manual WebDAV/API delete). When those deletes run, or when the SQLite catalogue is partially lost while `/config` blobs survive, users can end up with:

- **NZB blobs still on disk** (still referenced by history and/or still present in BlobStore), and
- **No corresponding mount tree** under `/content`.

Playback and library imports then look empty even though the Usenet pointers are recoverable from the retained NZB.

Upstream [#311](https://github.com/nzbdav-dev/nzbdav/pull/311) attacks a related failure mode with a **persisted `/content` snapshot** restored at startup. This fork’s phase-3 approach is complementary: **rebuild mounts by re-running the queue mount path against retained NZB blobs**, without requiring a prior snapshot of the DavItem tree.

---

## Goals

1. Offer a **manual maintenance task** that discovers recoverable releases and remounts them under `/content`.
2. Prefer **safety over completeness**: never overwrite or mutate healthy existing mounts.
3. Support a **dry-run report** so operators can see what would be rebuilt before changing the DB.
4. Emit the same style of progress/report UX as **Remove Orphaned Files** (Settings → Maintenance).
5. If the task performs any DavItem deletes (e.g. clearing a broken partial mount before rebuild), route them through `DeletionAuditLog`.

---

## Non-goals

- **Automatic startup restore** of the entire `/content` tree from a snapshot (that is upstream #311’s model; out of scope here).
- **Recovering blobs that are already gone** from BlobStore (no NNTP re-fetch of the NZB document itself in v1).
- **Undoing intentional deletes** when the blob was cleaned up correctly after the last reference was removed.
- **Rebuilding library symlinks/STRM files for *Arr** beyond what the existing post-processors already do when a mount is created (operators may still need a Sonarr/Radarr rescan or the separate recreate-STRM maintenance work).
- **Changing deletion semantics** of history retention, health repair, or Remove Orphaned Files.
- **Recovering from a wiped `/config`** — if both SQLite and blobs are gone, nothing can be rebuilt.

---

## Inputs

| Input | Role |
| --- | --- |
| `HistoryItem` rows with `NzbBlobId` | Primary candidates: completed jobs whose mount (`DownloadDirId` / `/content/...`) is missing or empty |
| `QueueItem` rows | Secondary: rare edge where a queue row exists but mount creation never persisted; treat carefully to avoid racing the live queue |
| `DavItem` tree under `/content` | Ground truth for “already mounted” — keyed by path and/or `NzbBlobId` |
| `BlobStore` / `{CONFIG_PATH}/blobs/{guid}` | Source NZB document for remount |
| `NzbNames` (if present) | Optional human-readable name for reports when history/job name is missing |
| Category / `JobName` from history | Target mount path: `/content/{category}/{jobName}` (same as `QueueItemProcessor.CreateMountFolder`) |

Eligibility sketch for a candidate blob id `B`:

1. Blob file exists in BlobStore for `B`.
2. At least one of: a `HistoryItem` with `NzbBlobId == B`, a `QueueItem` with `Id == B`, or an orphaned blob discovered via BlobStore enumeration that can be correlated to history/queue.
3. No healthy mount currently references `B` under `/content` (see Safety).

---

## Safety

| Rule | Detail |
| --- | --- |
| **No overwrite of healthy mounts** | If any `DavItem` under `/content` already has `NzbBlobId == B` and the mount directory is non-empty / structurally valid, **skip** `B`. Do not rename, merge, or replace in place. |
| **Collision on path** | If `/content/{category}/{jobName}` exists but is tied to a **different** blob (or looks like a partial/broken mount), **do not** delete by default. Report as conflict; optional future flag may allow replace — v1 should refuse and list conflicts. |
| **Duplicate NZB behavior** | Reuse existing duplicate-name policy concepts (`increment` vs replace) only when creating a **new** mount for a blob that has no mount; never attach a second mount to a blob that already has one. |
| **Deletion audit** | Any DavItem removal performed by this task (if a later revision allows clearing broken stubs) must call `DeletionAuditLog.Record` / `RecordBatch` with a distinct source, e.g. `content-rebuild`. |
| **Dry-run first** | Default UX encourages dry-run: report candidates, skips, and conflicts with **zero** DB writes. |
| **Concurrency** | Refuse to start if the live queue is actively processing the same blob, or if another maintenance task of this type is already running (HTTP 409 pattern like Remove Orphaned Files). |
| **No scheduled auto-run in v1** | Manual only; scheduling can be considered after the rebuild path is proven safe. |

---

## UX

Mirror **Remove Orphaned Files** under Settings → Maintenance:

1. Collapsible section: **Rebuild Content from NZB Blobs** (name TBD).
2. Short danger/info alert: explains that this remounts from retained NZBs; recommend DB backup; does not restore wiped `/config`.
3. Buttons:
   - **Perform a dry-run** (warning variant) → `/api/.../dry-run`
   - **Run Task** (success when enabled) → `/api/...`
4. Live progress via websocket topic (new topic or reuse the maintenance progress channel pattern used by Remove Orphaned Files / CTP).
5. Terminal report text, for example:
   - `Dry Run - Found 12 rebuildable, 3 already mounted, 1 blob missing, 2 path conflicts`
   - `Done: rebuilt 12 mounts, skipped 5, failed 1`
6. Abort/finish messages for hard failures (`Failed: ...`).

No new settings keys required for v1 beyond whatever the controller/task needs to run.

---

## Algorithm sketch

Design-level steps only — implementation may split helpers differently.

```text
1. Acquire exclusive “content-rebuild” lock (or return 409).

2. Enumerate candidate blob ids:
   a. HistoryItems where NzbBlobId is set
      AND (DownloadDirId is null OR no DavItem exists for that id
           OR no DavItems under /content reference this NzbBlobId)
   b. Optionally: BlobStore directory listing minus blobs still referenced by
      healthy /content DavItems (catch history-pruned-but-blob-retained edges
      only when a correlatable HistoryItem/NzbNames row still exists)

3. For each candidate B (stable order: history CreatedAt ascending):
   a. If BlobStore missing → record "blob-missing"; continue
   b. If healthy /content DavItem(s) with NzbBlobId=B exist → record "already-mounted"; continue
   c. Resolve category + jobName from HistoryItem (preferred) or NzbNames
   d. If target path occupied by unrelated/healthy tree → record "path-conflict"; continue
   e. If dry-run → record "would-rebuild"; continue
   f. Else remount:
      - Load NZB from BlobStore(B)
      - Re-enter the queue mount pipeline carefully:
        prefer a dedicated rebuild entry point that reuses
        QueueItemProcessor aggregators / post-processors
        WITHOUT enqueueing a live SAB queue item when history already exists
      - Create mount folder under /content/{category}/ with HistoryItemId /
        NzbBlobId wired like a normal completion
      - Run RAR/7z/file aggregators + optional STRM post-processor
      - Update HistoryItem.DownloadDirId if a history row exists
   g. On failure → record error; leave DB consistent (no half-written mount,
      or roll back the new mount subtree)

4. Emit summary report; release lock.
```

### “Re-run mount/queue path carefully”

The dangerous shortcut is “create a fake `QueueItem` and call `QueueItemProcessor` end-to-end,” which can race the real queue, change history status, or trigger *Arr notifications. Preferred shape:

- Extract or call a **remount** API that accepts `(nzbStream, category, jobName, historyItemId?, nzbBlobId)` and only performs the DB mount + aggregator steps used after download completion.
- Skip queue-state transitions, progress websockets for “downloading,” and duplicate history insertion when a `HistoryItem` already exists.
- Respect `queue.blocklisted-files` and `ensure-importable-video` consistently with normal imports, and surface skips in the report.

Exact code boundaries are left to the implementation PR after this design is approved.

---

## Failure modes

| Failure | Expected behavior |
| --- | --- |
| Blob missing on disk | Skip; report `blob-missing` |
| NZB parse failure / corrupt blob | Skip; report `parse-failed`; do not delete blob |
| Usenet articles missing at remount time | Same as normal queue health checks: mount may be incomplete or fail validators; report `remount-failed` without deleting other mounts |
| Path conflict with existing healthy folder | Skip; report `path-conflict` |
| Already mounted | Skip; report `already-mounted` |
| Task already running | HTTP 409; UI shows “Task already running.” |
| Partial DB write mid-remount | Transaction or compensating delete of the new mount subtree only; audit if compensation deletes |
| History row pruned, blob retained, no name metadata | May be unlistable in v1; document as limitation or require NzbNames |
| Operator expected snapshot-style restore of intentional deletes | Out of scope — explain in UI copy |

---

## Relationship to upstream #311

| | This design (fork #117 phase 3) | Upstream [nzbdav-dev/nzbdav#311](https://github.com/nzbdav-dev/nzbdav/pull/311) |
| --- | --- | --- |
| Trigger | Manual maintenance task | Startup restore from snapshot |
| Source of truth | Retained NZB blobs + history/queue refs | Serialized `/content` DavItem snapshot under `/config` |
| Recovers intentional post-delete state? | No (blobs usually cleaned after last ref) | No (valid post-delete snapshot replaces prior) |
| Needs prior snapshot? | No | Yes |
| Needs Usenet at rebuild time? | Yes (for health/article checks during remount) | No (metadata restore only) |

Both can coexist later; neither replaces ops hygiene (persistent `/config`, careful orphan/health settings). See also the setup guide section linked below.

---

## Open questions (for review)

1. Should v1 rebuild **only** from history-referenced blobs, or also scan BlobStore for unreferenced-looking blobs that still have `NzbNames`?
2. Should remount refresh STRM/symlinks automatically, or leave that to existing maintenance tasks?
3. Is a “replace broken stub mount” mode acceptable in v1, or strictly skip-all-conflicts?
4. Should successful rebuilds emit a structured info log (e.g. `content-rebuild blob=... path=...`) symmetric to `dav-delete`?

---

## Acceptance criteria (implementation PR, later)

- [ ] Design reviewed and open questions resolved.
- [ ] Dry-run reports candidates/skips with no DB mutations.
- [ ] Live run never overwrites healthy mounts for the same `NzbBlobId` or conflicting paths.
- [ ] Remounts reuse mount/aggregator logic without corrupting live queue/history.
- [ ] Any deletes go through `DeletionAuditLog` with source `content-rebuild`.
- [ ] Maintenance UI mirrors Remove Orphaned Files (button + websocket report).
- [ ] Focused unit tests for candidate selection, skip rules, and dry-run.
- [ ] Setup guide pointer remains accurate.

---

## References

- Local issue: [#117](https://github.com/nzbdav/nzbdav/issues/117) (phases 1–2: audit log + docs; phase 3: this recovery feature)
- Upstream recovery PR: [nzbdav-dev/nzbdav#311](https://github.com/nzbdav-dev/nzbdav/pull/311)
- Upstream symptom issue: [nzbdav-dev/nzbdav#304](https://github.com/nzbdav-dev/nzbdav/issues/304)
- Ops diagnosis: [Why did my files disappear?](setup-guide.md#why-did-my-files-disappear)
- Blob lifecycle: `NzbBlobCleanupService` (blob retained while queue, history, or DavItem references it)
- Mount creation: `QueueItemProcessor` (`CreateMountFolder`, aggregators, STRM post-processor)
- UX pattern: Settings → Maintenance → Remove Orphaned Files (`RemoveUnlinkedFilesTask`)
