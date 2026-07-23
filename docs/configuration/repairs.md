# Repairs

Background health monitoring and replacement of unhealthy library items.

!!! tip "Headless ENV"

    Map config keys below to `NZBDAV_CONFIG__...` with the
    [naming algorithm](headless.md#naming-algorithm)
    (`repair.enable` → `NZBDAV_CONFIG__REPAIR__ENABLE`). Enabling repairs via ENV
    also needs `media.library-dir` and *Arr instances.

| Control | Config key | Default | Effect |
|---------|------------|---------|--------|
| Enable Background Repairs | `repair.enable` | off | Needs library dir + *Arr |
| Health Check Concurrency | `repair.healthcheck-concurrency` | `50` | STAT connections; capped by pool |
| Health Check Depth | `repair.healthcheck-depth` | `standard` | standard / enhanced / deep / complete |
| Check older releases less thoroughly [since 0.8.0](https://github.com/nzbdav/nzbdav/releases/tag/v0.8.0){ .nzbdav-since } | `repair.healthcheck-aging` | off | Aging taper |
| Auto-Remove After Streaming Failures | `repair.auto-remove-after-failures` | `0` | `0` = disabled |
| Auto-remove unlinked files only | `repair.auto-remove-unlinked-only` | on | Linked → *Arr remove-and-search |
| Library Directory | `media.library-dir` | empty | Organized media path in container |

[Health and repairs](../operations/health-repairs.md)
