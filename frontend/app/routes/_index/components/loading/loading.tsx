import { Icon } from "~/components/ui";

export type LoadingProps = {
    className?: string
}

export function Loading({ className }: LoadingProps) {
    return (
        <div className={`flex min-h-[50dvh] w-full flex-col items-center justify-center gap-3 text-slate-400 ${className ?? ""}`}>
            <Icon name="progress_activity" className="animate-spin !text-[36px] text-blue-400" />
            <div className="text-sm font-medium">Loading...</div>
        </div>
    );
}