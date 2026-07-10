import type { ReactNode } from "react";
import { Badge } from "~/components/ui";

export type PageTableProps = {
    title: ReactNode,
    subTitle?: ReactNode,
    badgeText?: string,
    children?: ReactNode,
}

export function PageSection({ title, subTitle, badgeText, children }: PageTableProps) {
    return (
        <section className="mb-12 w-full rounded-lg border border-slate-700/70 bg-gray-800 p-4 pb-0 shadow-md sm:p-6 sm:pb-0">
            <div className="mb-6">
                <div className="mb-2.5 flex flex-wrap items-center justify-between gap-4">
                    {title}
                    {badgeText &&
                        <Badge className="px-3 py-1.5 text-xs font-medium text-slate-400">
                            {badgeText}
                        </Badge>
                    }
                </div>
                {subTitle}
            </div>
            {children}
        </section>
    );
}