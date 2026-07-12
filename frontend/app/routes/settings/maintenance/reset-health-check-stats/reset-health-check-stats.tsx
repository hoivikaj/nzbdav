import { useCallback, useState } from "react";
import { Button } from "~/components/ui/button";
import { Alert } from "~/components/ui/feedback";

export function ResetHealthCheckStats() {
    const [isRunning, setIsRunning] = useState(false);
    const [message, setMessage] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const onReset = useCallback(async () => {
        if (!window.confirm(
            "Reset all health-check statistics and history? This cannot be undone."
        )) {
            return;
        }

        setIsRunning(true);
        setMessage(null);
        setError(null);
        try {
            const response = await fetch("/api/clear-health-check-history", { method: "POST" });
            if (!response.ok) {
                const body = await response.json().catch(() => ({}));
                throw new Error(body.error || `Request failed (${response.status})`);
            }
            const data = await response.json();
            setMessage(
                `Reset complete. Removed ${data.deletedResults ?? 0} result row(s) and ${data.deletedStats ?? 0} stat row(s).`
            );
        } catch (e) {
            setError(e instanceof Error ? e.message : "Failed to reset health-check statistics.");
        } finally {
            setIsRunning(false);
        }
    }, []);

    return (
        <div className="space-y-3">
            <p className="text-xs leading-relaxed text-slate-400">
                Clears all accumulated health-check history and counters (repairs, deletions, healthy/unhealthy totals).
            </p>
            <Button
                type="button"
                variant={isRunning ? "secondary" : "danger"}
                disabled={isRunning}
                className="inline-flex"
                onClick={onReset}
            >
                {isRunning ? "Resetting..." : "Reset Health-Check Statistics"}
            </Button>
            {message && <Alert variant="success">{message}</Alert>}
            {error && <Alert variant="danger">{error}</Alert>}
        </div>
    );
}
