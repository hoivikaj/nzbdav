# Headless environment configuration [since 0.9.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.9.0){ .nzbdav-since }

Authoritative, opt-in configuration of every user-owned Settings (`ConfigItems`) value via `NZBDAV_CONFIG__...` environment variables. Designed for infrastructure-as-code / Docker Compose deployments where the Settings UI should not own those values.

!!! tip "Operational vs headless"

    Process wiring (`CONFIG_PATH`, `PORT`, `LOG_LEVEL`, cookies, …) and legacy Settings *fallbacks* (`WEBDAV_USER`, `MOUNT_DIR`, …) stay on the [Environment variables](environment-variables.md) page. This page covers only the new authoritative `ConfigItems` overlay.

## What is in scope

- Every public key in Settings that persists as a `ConfigItems` row (scalars and JSON blobs such as `usenet.providers`, `arr.instances`, `indexers.instances`, `profiles.instances`).
- Read-only Settings UI for ENV-managed keys, with the exact variable name shown.
- Startup validation that fails fast on unknown / invalid prefixed variables.

## What is out of scope (v0.9)

These remain separate persistence / operational domains — they are **not** driven by `NZBDAV_CONFIG__...`:

- Frontend **admin account** bootstrap (first-run username/password)
- **Warden** source lists and GitHub backup state (`warden.db`)
- Database restore / one-off maintenance task actions
- Process variables such as `CONFIG_PATH`, `PORT`, `LOG_LEVEL`, `FRONTEND_BACKEND_API_KEY` (unless also mirrored as a Settings key like `api.key`)

## Naming algorithm

Take the Settings config key (documented on each [configuration](index.md) page), then:

1. Replace every `.` with `__`
2. Replace every `-` with `_`
3. Uppercase
4. Prefix with `NZBDAV_CONFIG__`

Examples:

| Config key | Environment variable |
|------------|----------------------|
| `api.categories` | `NZBDAV_CONFIG__API__CATEGORIES` |
| `usenet.segment-cache.enabled` | `NZBDAV_CONFIG__USENET__SEGMENT_CACHE__ENABLED` |
| `api.addurl-trusted-hosts` | `NZBDAV_CONFIG__API__ADDURL_TRUSTED_HOSTS` |
| `usenet.providers` | `NZBDAV_CONFIG__USENET__PROVIDERS` |

Structured settings use the same JSON shapes as the Settings API / UI. Per-feature pages list the config keys; apply the mapping rule above for the ENV name.

Internal runtime keys are **excluded** and rejected if supplied:

- `search.exclude` (prefix constant)
- `search.exclude-sync-cache`

## Precedence

```
NZBDAV_CONFIG__...  >  SQLite / Settings UI  >  legacy fallbacks (WEBDAV_*, MOUNT_DIR, …)  >  built-in defaults
```

- ENV values are **not** written into SQLite.
- Removing an ENV variable and restarting restores the prior SQLite value (if any) and re-enables editing in the UI.
- With **no** `NZBDAV_CONFIG__...` variables set, behavior is identical to previous releases.

## Settings UI

When a key is ENV-managed:

- The control is shown **read-only** (disabled fieldset) with a lock badge naming the variable
- Save payloads omit managed keys (backend still rejects writes as the authority)
- A page banner notes that managed settings require changing container configuration and restarting

## Validation and restart

- Unknown `NZBDAV_CONFIG__...` names, or invalid values for a known key, abort startup with a **single-line** error that names the variable and **never** prints its value
- Changing ENV configuration requires a **container restart** to take effect
- Logs list managed **key names** only (not secret values)

## Secrets

!!! danger "Runtime visibility"

    Even when Compose uses `${WEBDAV_PASS:?missing}` placeholders, resolved secrets are visible to the container runtime and inspection tools (`docker inspect`, orchestrator secret mounts that materialize as env, etc.). Prefer an uncommitted `.env` file or orchestrator secret injection. **Never** commit real credentials or paste resolved values into docs, issues, or logs.

`NZBDAV_CONFIG__WEBDAV__PASS` accepts plaintext; NzbDAV hashes it at overlay load (same as the Settings UI). Other secrets in JSON (Usenet `Pass`, indexer / *Arr API keys) remain in the ENV JSON as supplied.

## Fully hydrated Compose example

Copy-ready skeleton. Secrets use `${VAR:?message}` so Compose fails closed if they are missing — put real values in an uncommitted `.env` (or your orchestrator's secret store), not in git.

```yaml
services:
  nzbdav:
    image: ghcr.io/nzbdav/nzbdav:latest
    container_name: nzbdav
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "curl -fsSL http://localhost:3000/healthz > /dev/null || exit 1"]
      interval: 30s
      retries: 3
      start_period: 60s
      timeout: 5s
    ports:
      - "3000:3000"
    environment:
      # --- Process / operational (unchanged; see environment-variables.md) ---
      PUID: "1000"
      PGID: "1000"
      TZ: America/New_York
      TRUST_PROXY: "1"
      SECURE_COOKIES: "true"
      FRONTEND_BACKEND_API_KEY: ${FRONTEND_BACKEND_API_KEY:?set FRONTEND_BACKEND_API_KEY}

      # --- Authoritative ConfigItems overlay (NZBDAV_CONFIG__...) ---
      NZBDAV_CONFIG__API__KEY: ${FRONTEND_BACKEND_API_KEY:?set FRONTEND_BACKEND_API_KEY}
      NZBDAV_CONFIG__API__CATEGORIES: "tv,movies"
      NZBDAV_CONFIG__API__MANUAL_CATEGORY: "uncategorized"
      NZBDAV_CONFIG__API__IMPORT_STRATEGY: "symlinks"
      NZBDAV_CONFIG__API__ENSURE_IMPORTABLE_VIDEO: "true"
      NZBDAV_CONFIG__API__IGNORE_HISTORY_LIMIT: "true"

      NZBDAV_CONFIG__WEBDAV__USER: "webdav"
      NZBDAV_CONFIG__WEBDAV__PASS: ${WEBDAV_PASS:?set WEBDAV_PASS}
      NZBDAV_CONFIG__WEBDAV__ENFORCE_READONLY: "true"

      NZBDAV_CONFIG__RCLONE__MOUNT_DIR: "/mnt/remote/nzbdav"

      NZBDAV_CONFIG__USENET__MAX_DOWNLOAD_CONNECTIONS: "0"
      NZBDAV_CONFIG__USENET__STREAMING_PRIORITY: "80"
      NZBDAV_CONFIG__USENET__PIPELINING__ENABLED: "false"
      NZBDAV_CONFIG__USENET__CASCADE__ENABLED: "true"
      NZBDAV_CONFIG__USENET__PROVIDERS: >-
        {"Providers":[{"Type":1,"Host":"news.example.com","Port":563,"UseSsl":true,"User":"${USENET_USER:?set USENET_USER}","Pass":"${USENET_PASS:?set USENET_PASS}","MaxConnections":20,"Nickname":"primary"}]}

      NZBDAV_CONFIG__ARR__INSTANCES: >-
        {"RadarrInstances":[{"Host":"http://radarr:7878","ApiKey":"${RADARR_API_KEY:?set RADARR_API_KEY}"}],"SonarrInstances":[{"Host":"http://sonarr:8989","ApiKey":"${SONARR_API_KEY:?set SONARR_API_KEY}"}],"QueueRules":[]}

      NZBDAV_CONFIG__INDEXERS__INSTANCES: >-
        {"Indexers":[{"Name":"Example","Url":"https://indexer.example/api","ApiKey":"${INDEXER_API_KEY:?set INDEXER_API_KEY}","Enabled":true}]}

      NZBDAV_CONFIG__PROFILES__INSTANCES: >-
        {"Profiles":[{"Name":"Default","Token":"default"}]}

      NZBDAV_CONFIG__REPAIR__ENABLE: "true"
      NZBDAV_CONFIG__REPAIR__HEALTHCHECK_CONCURRENCY: "50"
      NZBDAV_CONFIG__REPAIR__HEALTHCHECK_DEPTH: "standard"

      NZBDAV_CONFIG__MAINTENANCE__REMOVE_ORPHANED_SCHEDULE_ENABLED: "true"
      NZBDAV_CONFIG__MAINTENANCE__REMOVE_ORPHANED_SCHEDULE_TIME: "180"

      NZBDAV_CONFIG__BACKUP__SCHEDULE_ENABLED: "true"
      NZBDAV_CONFIG__BACKUP__SCHEDULE_TIME: "120"
      NZBDAV_CONFIG__BACKUP__RETENTION_COUNT: "7"

      NZBDAV_CONFIG__PREFLIGHT__MODE: "off"
      NZBDAV_CONFIG__PLAY__WATCHDOG_ENABLED: "true"
    volumes:
      - ./config:/config
      - /mnt:/mnt
```

!!! note "JSON and Compose escaping"

    Multi-line `>-` YAML folds to a single line. Nested `${...}` inside JSON strings are expanded by Compose before the container starts. `ProviderType` for **Pool Connections** is `1` (`0` = Disabled, `2` = BackupAndStats, `3` = BackupOnly — see [Usenet](usenet.md)). Omit `ProviderId` in ENV JSON — NzbDAV preserves matching SQLite ids by host/port/user, or assigns new ones.

### After start

1. Open **Settings**. ENV-managed controls show a lock badge such as `Managed by NZBDAV_CONFIG__WEBDAV__USER`.
2. Attempting to save a managed key is rejected by the API.
3. To hand a key back to SQLite/UI: remove that `NZBDAV_CONFIG__...` line, recreate the container, and the prior SQLite value (if any) becomes editable again.

### Minimal `.env` (do not commit)

```bash
FRONTEND_BACKEND_API_KEY=generate-a-long-random-string
WEBDAV_PASS=
USENET_USER=
USENET_PASS=
RADARR_API_KEY=
SONARR_API_KEY=
INDEXER_API_KEY=
```

## Legacy compatibility

- Existing `WEBDAV_USER` / `WEBDAV_PASSWORD` / `MOUNT_DIR` / `CATEGORIES` / … fallbacks are unchanged and still apply only when neither ENV overlay nor SQLite supplies a value.
- Prefer `NZBDAV_CONFIG__...` for new headless deployments so Settings shows the authoritative source and rejects conflicting UI edits.

## Related pages

- [Environment variables](environment-variables.md) — process + legacy fallbacks
- [Docker](../getting-started/docker.md) — Compose basics
- [First run](../getting-started/first-run.md) — admin account (still UI / out of ENV scope)
- Feature Settings references: [Usenet](usenet.md) · [SABnzbd](sabnzbd.md) · [WebDAV](webdav.md) · [Radarr/Sonarr](arrs.md) · [Indexers](indexers.md) · [Profiles](profiles.md) · [Repairs](repairs.md) · [Maintenance](maintenance.md) · [Backup](backup.md)
