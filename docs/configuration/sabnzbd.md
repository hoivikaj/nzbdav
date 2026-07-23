# SABnzbd

SABnzbd-compatible download client API used by Radarr/Sonarr. See also [API compatibility](../features/sab-api.md).

!!! tip "Headless ENV"

    Map any config key in the table to `NZBDAV_CONFIG__...` with the
    [naming algorithm](headless.md#naming-algorithm)
    (`api.categories` → `NZBDAV_CONFIG__API__CATEGORIES`).

| Control | Config key | Default | Effect |
|---------|------------|---------|--------|
| API Key | `api.key` | from `FRONTEND_BACKEND_API_KEY` if unset | *Arr download client auth |
| Categories | `api.categories` | env/`audio,software,tv,movies` | Letters/numbers/dashes |
| Manual Upload Category | `api.manual-category` | `uncategorized` | Queue page uploads |
| Import Strategy | `api.import-strategy` | `symlinks` | Symlinks (Plex) / STRM (Emby/Jellyfin) |
| Rclone Mount Directory | `rclone.mount-dir` | env `MOUNT_DIR` or `/mnt/nzbdav` | When symlinks |
| Completed Downloads Dir | `api.completed-downloads-dir` | backend default under `/data` | When STRM |
| Base URL | `general.base-url` | `http://localhost:3000` | STRM / adapter absolute URLs |
| Ignored Files | `api.download-file-blocklist` | `*.nfo, *.par2, …` | Glob blocklist for mounts |
| Behavior for Duplicate NZBs | `api.duplicate-nzb-behavior` | `increment` | increment / mark-failed |
| User Agent | `api.user-agent` | env/default | `addurl` NZB fetch |
| Trusted local hosts [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since } | `api.addurl-trusted-hosts` | env `TRUSTED_INTERNAL_HOSTS` | SSRF allowlist for private addurl |
| Fail downloads without video | `api.ensure-importable-video` | on | Reject non-video NZBs |
| Fail when non-video missing articles | inverse of `api.skip-non-video-on-missing-articles` | skip non-video by default | |
| Article health check categories | `api.ensure-article-existence-categories` | empty (off) | Per-category; may be slow |
| Always send full History | `api.ignore-history-limit` | on | Ignore client history limit |
| Save backup copies of incoming NZBs | `api.nzb-backup-enabled` | off | On-disk `*.nzb` copies |
| Backup location | `api.nzb-backup-location` | — | By category |
| Keep NZB backups (days) | `api.nzb-backup-retention-days` | `30` | `0` = forever |

[Import strategies](../guides/import-strategies.md)
