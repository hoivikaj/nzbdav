# NNTP Pipelining

NzbDav uses **UsenetSharp 3.x** batch BODY requests and pipelined STAT existence
checks to send multiple NNTP commands on one connection without waiting for each
response. Responses are read strictly in order with bounded backpressure.

There are **two separate toggles**:

| Setting | Location | Default | What it controls |
|---------|----------|---------|------------------|
| `usenet.pipelining.enabled` | Settings → Usenet | off | Queue first-segment fetch, provider benchmark batch downloads, and pipelined STAT health / import existence checks |
| `usenet.pipelined-body-requests` | Settings → WebDAV | on | WebDAV streaming read-ahead via `DecodedBodiesAsync` batches |

## What the Usenet toggle speeds up

| Path | Without pipelining | With pipelining |
|------|-------------------|-----------------|
| Queue first-segment fetch (0→50%) | one `BODY` per file, concurrent across connections | first segments fetched in depth-sized batches on one connection |
| Provider benchmark | one `BODY` per article | depth-sized `DecodedBodiesAsync` batches |
| Health check / import existence | concurrent `STAT` across the pool | primary-provider pipelined `STAT` via `StatPipelinedAsync`, with concurrent failover recheck of any misses |

## Enabling queue pipelining

Settings → Usenet → **NNTP Pipelining**:

- **Enable NNTP pipelining** — toggles `usenet.pipelining.enabled`.
- **Pipeline depth** — `usenet.pipelining.depth`, requests per BODY batch (1–64,
  default 8). Each provider can override this in its own settings. STAT sweeps
  use a larger fixed chunk and UsenetSharp's internal `MaxPipelineDepth` window;
  BODY depth does not size STAT batches.

For WebDAV playback, use Settings → WebDAV → **Pipelined article downloads**
(`usenet.pipelined-body-requests`).

## How it's built

UsenetSharp exposes batch BODY pipelining through `DecodedBodiesAsync` and
pipelined existence checks through `StatPipelinedAsync`. nzbdav routes
`*PipelinedAsync` paths through those APIs. The client chain is:

- `BaseNntpClient` — delegates batch calls to UsenetSharp; classifies STAT
  replies so connection-level codes (e.g. buffered 400) throw instead of looking
  like missing articles
- `MultiConnectionNntpClient` — leases one connection per batch (STAT path does
  not feed the circuit breaker)
- `MultiProviderNntpClient` — provider selection and byte counting
- `DownloadingNntpClient` / `WrappingNntpClient` — permits and delegation

## Testing

Validate with the Usenet toggle **on** against your providers before relying on
it for queue imports. The provider benchmark can recommend a depth and whether
pipelining helps at your connection count.

## Limitations

- **Queue / WebDAV pipelined BODY batches use the same per-segment failover as
  `DecodedBodiesAsync`.** Each depth-sized chunk selects an ordered provider list
  and retries individual misses on the primary (then backups) before yielding
  `Found = false`. Queue first-segment rescue still re-fetches any remaining null
  slots with full per-article failover.
- **Pipelined STAT sweeps are primary-only.** Misses are rechecked with concurrent
  per-segment `STAT` (full failover) so backups can still satisfy articles.
  Connection-level errors during the sweep fall back to the concurrent path.
- The per-queue-item article cache bypasses pipelined queue paths when caching is
  enabled (pre-existing; first segments may be re-fetched during RAR header parse).
