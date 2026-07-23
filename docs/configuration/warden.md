# Warden

Portable dead-release fingerprint list: filter search results, sync remote sources, optional GitHub backup.

!!! note "Headless ENV scope"

    Scalar Settings below (`warden.hide-dead`, `warden.quorum`, `warden.backbone-scope`) can use
    [`NZBDAV_CONFIG__...`](headless.md). Warden **sources**, GitHub backup state, and `warden.db`
    remain a separate domain and are **not** driven by the ENV overlay.

## Persisted settings

| Control | Config key | Default | Effect |
|---------|------------|---------|--------|
| Filter out anything on the list | `warden.hide-dead` | on | Hide matching search hits (fallback if all match) |
| Agreement needed for shared lists | `warden.quorum` | `2` | Quorum for corroborate sources |
| Only filter when the provider matches | `warden.backbone-scope` | on | Scope remote/imported fingerprints |

## Operational UI

Sources (local / remote / imported): trust full/corroborate/observe, enable, refresh hours, import/export/bundle, clear/remove.

**Backup to GitHub:** repo, fine-grained PAT, path, branch, scope, interval, auto backup, restore. Fingerprints only — keep backup repos private for personal lists. Restore replaces the **local** list only.

!!! warning

    Merge-into-my-list import cannot be un-merged.
