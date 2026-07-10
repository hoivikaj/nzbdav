import type { ReactNode } from "react";
import { Badge, Icon, Tooltip } from "~/components/ui";

export type StatusBadgeProps = {
    className?: string,
    status: string,
    percentage?: string,
    error?: string,
}


export function StatusBadge({ className, status, percentage, error }: StatusBadgeProps) {
    const statusLower = status?.toLowerCase();

    if (statusLower === "completed") {
        return <StatusShell className="border-emerald-500/40 bg-emerald-500/20 text-emerald-200">{statusLower}</StatusShell>;
    }

    if (statusLower === "failed" || statusLower == "upload failed") {
        if (error?.startsWith("Article with message-id"))
            error = "Missing articles";

        return (
            <Tooltip content={error || "Upload failed"}>
                <StatusShell className="cursor-help border-red-500/40 bg-red-500/20 text-red-200">
                    {statusLower === "upload failed" && <Icon name="upload" className="!text-[12px]" />}
                    failed
                </StatusShell>
            </Tooltip>
        );
    }

    if (statusLower === "downloading") {
        const percentNum = Number(percentage);
        const badgeText = `${percentNum > 100 ? percentNum - 100 : percentNum}%`;
        const isHealthChecking = percentNum > 100;

        const downloadProgressStyle = (percentNum >= 0)
            ? { width: `${Math.min(percentNum, 100)}%` }
            : undefined;

        const healthCheckProgressStyle = isHealthChecking
            ? { width: `${Math.min(percentNum - 100, 100)}%` }
            : undefined;

        return (
            <StatusShell>
                <span className={`absolute inset-y-0 left-0 transition-all duration-500 ${isHealthChecking ? "bg-slate-700" : "bg-blue-600"}`} style={downloadProgressStyle} />
                <span className="absolute inset-y-0 left-0 bg-emerald-600 transition-all duration-500" style={healthCheckProgressStyle} />
                <span className="relative">{badgeText}</span>
            </StatusShell>
        );
    }

    if (statusLower === "uploading") {
        const percentNum = Number(percentage);
        const badgeText = `${percentNum}%`;
        const uploadProgressStyle = { width: `${Math.min(percentNum, 100)}%` };

        return (
            <StatusShell>
                <span className="absolute inset-y-0 left-0 bg-cyan-600 transition-all duration-500" style={uploadProgressStyle} />
                <span className="relative flex items-center justify-center gap-0.5"><Icon name="upload" className="!text-[12px]" />{badgeText}</span>
            </StatusShell>
        );
    }

    if (statusLower === "pending") {
        return (
            <StatusShell><Icon name="upload" className="!text-[12px]" />pending</StatusShell>
        );
    }

    if (statusLower === "health-checking") {
        const percentNum = Number(percentage);
        const badgeText = `${percentNum}%`;
        const healthCheckProgressStyle = { width: `${Math.min(percentNum, 100)}%` };

        return (
            <StatusShell className={className}>
                <span className="absolute inset-y-0 left-0 bg-emerald-600 transition-all duration-500" style={healthCheckProgressStyle} />
                <span className="relative">{badgeText}</span>
            </StatusShell>
        );
    }

    return <StatusShell>{statusLower}</StatusShell>;
}

function StatusShell({ className = "", children }: { className?: string, children: ReactNode }) {
    return (
        <Badge className={`relative inline-flex w-[85px] items-center justify-center gap-0.5 overflow-hidden border-slate-600 bg-slate-800 px-1.5 py-1 font-semibold text-white ${className}`}>
            {children}
        </Badge>
    );
}