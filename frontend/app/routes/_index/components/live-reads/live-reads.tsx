import { useState } from "react";
import { useWebsocketTopic } from "~/utils/shared-websocket";
import { clientIdentityTooltip, clientLabelFromUserAgent } from "~/utils/client-label";
import { Icon } from "~/components/ui";

type ProviderUsage = { host: string; nickname?: string | null; segments: number };
type Read = {
    id: string;
    fileName: string;
    path: string;
    startedAt: number;
    lastActivityAt: number;
    bytesRead: number;
    fileSize: number | null;
    clientIp?: string | null;
    clientUserAgent?: string | null;
    providers: ProviderUsage[];
};
type Snapshot = { reads: Read[] };

const GENERIC_HOST_PREFIXES = new Set(["news", "reader", "premium", "secure", "ssl", "nntp", "usenet", "block"]);

function stripHost(host: string): string {
    if (!host) return "—";
    const labels = host.split(".").filter(Boolean);
    if (labels.length === 0) return host;
    if (labels.length === 1) return labels[0];
    if (labels.length === 2) return labels[0];
    if (GENERIC_HOST_PREFIXES.has(labels[0].toLowerCase())) return labels[1];
    return labels[0].length >= labels[1].length ? labels[0] : labels[1];
}

function shortName(name: string): string {
    if (!name) return "—";
    const max = 28;
    return name.length <= max ? name : name.slice(0, max - 1) + "…";
}

export function LiveReads() {
    const [snapshot, setSnapshot] = useState<Snapshot | null>(null);

    useWebsocketTopic("ar", "state", (message) => {
        try { setSnapshot(JSON.parse(message)); }
        catch { /* ignore malformed frames */ }
    }, {
        onClose: () => setSnapshot(null),
    });

    const reads = snapshot?.reads ?? [];
    if (reads.length === 0) return null;

    return (
        <section className="rounded-box border border-base-content/10 bg-base-200 p-3">
            <div className="mb-2 flex items-center gap-2 text-[11px] font-semibold uppercase tracking-wide text-base-content/50">
                <Icon name="download" className="!text-[16px]" />
                Active Reads
            </div>
            <div className="flex flex-col gap-2">
                {reads.map(r => <ReadRow key={r.id} item={r} />)}
            </div>
        </section>
    );
}

function ReadRow({ item }: { item: Read }) {
    const totalSegments = item.providers.reduce((acc, p) => acc + p.segments, 0);
    const clientLabel = clientLabelFromUserAgent(item.clientUserAgent);
    return (
        <div className="flex flex-col gap-0.5 text-[11px] leading-snug" title={item.fileName}>
            <div className="truncate text-base-content">{shortName(item.fileName)}</div>
            <div
                className="truncate text-base-content/50"
                title={clientIdentityTooltip(item.clientUserAgent, item.clientIp)}
            >
                {clientLabel}
            </div>
            <div className="flex flex-wrap items-center gap-x-1 text-base-content/50">
                {item.providers.length === 0
                    ? <span className="italic text-base-content/40">buffering…</span>
                    : item.providers.map((p, i) => (
                        <span key={`${p.host}-${i}`} className="inline-flex items-baseline gap-1">
                            {i > 0 && <span className="text-base-content/30">·</span>}
                            <span className="text-base-content/70" title={p.host}>{p.nickname?.trim() || stripHost(p.host)}</span>
                            {totalSegments > 0 && (
                                <span className="font-mono text-base-content/50">
                                    {Math.round((p.segments / totalSegments) * 100)}%
                                </span>
                            )}
                        </span>
                    ))}
            </div>
        </div>
    );
}
