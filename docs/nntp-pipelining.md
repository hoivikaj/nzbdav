# NNTP Pipelining

Pipelining sends multiple NNTP commands per connection without waiting for each
response, then reads the responses in order. It removes the per-article
round-trip stall that otherwise dominates small-request workloads (queue
imports) and per-segment streaming, especially on high-latency providers.

It is **off by default** and gated behind a settings switch.

## What it speeds up

| Path | Before | With pipelining |
|------|--------|-----------------|
| Queue first-segment fetch (0→50%) | one `ARTICLE` per file, each a full round-trip | first segments pipelined on one connection, back-to-back |
| Health check (100→200%) | one `STAT` per article | `STAT`s pipelined, falling back to per-segment failover on a miss |
| Streaming playback | up to `article-buffer-size` connections, one segment each | one connection streaming consecutive segments with no round-trip gaps |

## Enabling it

Settings → Usenet → **NNTP Pipelining (Experimental)**:

- **Enable NNTP pipelining** — toggles `usenet.pipelining.enabled`.
- **Pipeline depth** — `usenet.pipelining.depth`, the number of requests kept in
  flight per connection (1–64, default 8). Higher helps more on high-latency
  links; 8 is a good default.

(Config keys can also be set directly via the SAB-compatible config API.)

## How it's built

The pipelining engine lives in **UsenetSharp** (`UsenetClient.PipelinedAsync.cs`):
a windowed FIFO pipeline (`StatPipelinedAsync` / `BodyPipelinedAsync` /
`ArticlePipelinedAsync`) that writes up to *depth* commands ahead and reads
responses strictly in order. The existing single-command methods are unchanged.

nzbdav consumes it through the existing client chain. Each layer is handled:
- `BaseNntpClient` — real pipelining + yEnc decode
- `MultiConnectionNntpClient` — leases one connection per batch; **destroys it if
  the batch is abandoned early** so a half-read socket never returns to the pool
- `DownloadingNntpClient` — priority permit · `MultiProviderNntpClient` — provider
  selection + byte counting · `WrappingNntpClient` — delegation
- The abstract base provides a **non-pipelined fallback** for every batch method,
  so any path that isn't pipelined still works correctly.

## Build / release workflow

The pipelining engine is a change to the **UsenetSharp** library
(`github.com/nzbdav-dev/UsenetSharp`). `NzbWebDAV.csproj` references it
conditionally:

- **Local dev** — if a sibling checkout exists at `../../UsenetSharp` (overridable
  via the `UsenetSharpProject` MSBuild property), it's used as a `ProjectReference`
  so you can build both together.
- **Docker / CI** — when no sibling exists, it falls back to
  `PackageReference UsenetSharp 1.0.7`.

So to release:
1. Merge the UsenetSharp pipelining branch and **publish UsenetSharp 1.0.7** to
   NuGet.
2. The conditional reference then resolves to the package automatically — the
   Dockerfile needs no changes.

## Testing

UsenetSharp ships integration tests that run against a real provider. Fill in
`UsenetSharpTest/Credentials.cs` and valid segment ids, then run the
`UsenetClientPipelinedAsyncTests` suite — it verifies in-order delivery,
byte-for-byte equality with sequential fetches, mixed found/missing handling, and
that the connection stays reusable after a batch.

Because pipelining touches the core I/O path, validate with the switch **on**
against your providers before relying on it.

## v1 characteristics / limitations

- **Streaming uses one connection per stream** (pipelined, gap-free). This frees
  the connection pool dramatically versus the previous one-connection-per-segment
  read-ahead and is sufficient for typical bitrates. Striping a single stream
  across multiple connections is possible future work for very high bitrates.
- **Cross-provider failover for misses is reduced on the pipelined path.** A batch
  runs on the selected provider; per-segment failover to a backup provider mid-batch
  is not performed. Misses degrade gracefully per consumer:
  - queue first-segment → marked `MissingFirstSegment` (name still recoverable via par2)
  - streaming → zero-filled to keep playback alive
  - health check → falls back to the full per-segment failover check
  If you depend heavily on multi-provider redundancy, prefer leaving pipelining off
  until per-segment failover is added to the pipelined path.
- The segment/article cache is bypassed on the pipelined path in v1.
