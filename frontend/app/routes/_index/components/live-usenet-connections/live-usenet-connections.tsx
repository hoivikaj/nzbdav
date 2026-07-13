import { useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";
import { Icon } from "~/components/ui";

const usenetConnectionsTopic = {'cxs': 'state'};

type LiveUsenetConnectionsProps = {
    hasUsenetProviders: boolean,
};

export function LiveUsenetConnections({ hasUsenetProviders }: LiveUsenetConnectionsProps) {
    const [connections, setConnections] = useState<string | null>(null);
    const parts = (connections || "0|0|0|0|1|0").split("|");
    const [_0, _1, _2, live, max, idle] = parts.map(x => Number(x));
    const active = live - idle;
    const activePercent = max > 0 ? 100 * (active / max) : 0;

    useEffect(() => {
        if (!hasUsenetProviders) {
            setConnections(null);
            return;
        }

        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setConnections(message));
            ws.onopen = () => ws.send(JSON.stringify(usenetConnectionsTopic));
            ws.onerror = () => { ws.close() };
            ws.onclose = onClose;
            return () => { disposed = true; ws.close(); }
        }
        function onClose(e: CloseEvent) {
            if (e.code == 1008) {
                globalThis.location.assign("/login");
                return;
            }
            !disposed && setTimeout(() => connect(), 1000);
            setConnections(null);
        }
        return connect();
    }, [hasUsenetProviders]);

    return (
        <section className="rounded-box border border-base-content/10 bg-base-200 p-3">
            <div className="flex flex-col gap-2">
                <div className="flex items-center gap-2 text-[11px] font-semibold uppercase tracking-wide text-base-content/50">
                    <Icon name="cloud" className="!text-[16px]" />
                    Usenet Connections
                </div>
                {hasUsenetProviders && (
                    <progress
                        className="progress progress-primary h-1.5 w-full"
                        value={Number.isFinite(activePercent) ? activePercent : 0}
                        max={100}
                    />
                )}
                <div className="text-xs text-base-content/60">
                    {!hasUsenetProviders && "No providers configured"}
                    {hasUsenetProviders && connections && `${live} connected / ${max} max`}
                    {hasUsenetProviders && !connections && (
                        <span className="flex items-center gap-1.5">
                            <span className="loading loading-spinner loading-xs" />
                            Connecting
                        </span>
                    )}
                </div>
                {hasUsenetProviders && connections && (
                    <div className="font-mono text-[11px] text-base-content/50">{active} active</div>
                )}
            </div>
        </section>
    );
}
