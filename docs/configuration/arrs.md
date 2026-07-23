# Radarr / Sonarr

*Arr instance credentials and automatic stuck-queue handling. Config key: `arr.instances`
(`NZBDAV_CONFIG__ARR__INSTANCES` — see [headless](headless.md)).

| Control | Effect |
|---------|--------|
| Radarr Host / API Key | Test Conn available |
| Sonarr Host / API Key | Test Conn available |
| Automatic Queue Management | Per status message: Do Nothing / Remove / Remove+Blocklist / Remove+Blocklist+Search |

Only **Usenet** queue items are acted upon. Typical mappings:

- **Remove, Blocklist, and Search** — no eligible files, samples, no audio tracks
- **Remove and Blocklist** — not an upgrade / custom format
- **Remove** — already imported
- **Do Nothing** — ID mismatches and similar manual-import cases

[Connect *Arr](../getting-started/connect-arr.md)
