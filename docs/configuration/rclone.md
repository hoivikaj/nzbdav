# Rclone Server

Notify rclone RC when WebDAV files change (useful with high `dir-cache-time`).

!!! tip "Headless ENV"

    Map config keys below to `NZBDAV_CONFIG__...` with the
    [naming algorithm](headless.md#naming-algorithm)
    (`rclone.host` → `NZBDAV_CONFIG__RCLONE__HOST`).

| Control | Config key | Default | Effect |
|---------|------------|---------|--------|
| Enable Rclone RC notifications | `rclone.rc-enabled` | off | Auto `vfs/forget` on add/remove |
| Rclone Server Host | `rclone.host` | empty | e.g. `http://nzbdav_rclone:5572` |
| Rclone Server User | `rclone.user` | empty | Optional |
| Rclone Server Password | `rclone.pass` | empty | Optional |

Mount directory for symlink imports is configured on the [SABnzbd](sabnzbd.md) tab (`rclone.mount-dir`).

[Mounting WebDAV](../guides/mounting-webdav.md)
