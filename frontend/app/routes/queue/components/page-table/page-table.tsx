import type { ReactNode } from "react";
import { TriCheckbox, type TriCheckboxState } from "../tri-checkbox/tri-checkbox";
import { Truncate } from "../truncate/truncate";
import { StatusBadge } from "../status-badge/status-badge";
import { formatFileSize } from "~/utils/file-size";
import { Badge } from "~/components/ui";

export type PageTableProps = {
    children?: ReactNode,
    headerCheckboxState: TriCheckboxState,
    onHeaderCheckboxChange: (isChecked: boolean) => void,
    footer?: ReactNode,
}

export function PageTable({ children, headerCheckboxState, onHeaderCheckboxChange, footer }: PageTableProps) {
    return (
        <div className="-mx-4 overflow-x-auto sm:-mx-6">
            <table className="mb-0 w-full table-fixed text-slate-300 [&_tbody_tr:last-child_td]:border-b-0">
                <thead>
                    <tr>
                        <th className="w-auto bg-slate-900 px-0 py-4 text-left text-xs font-semibold tracking-wide text-slate-200 min-[900px]:w-1/2">
                            <TriCheckbox state={headerCheckboxState} onChange={onHeaderCheckboxChange}>
                                Name
                            </TriCheckbox>
                        </th>
                        <th className="hidden w-[100px] bg-slate-900 px-0 py-4 text-center text-xs font-semibold tracking-wide text-slate-200 min-[900px]:table-cell">Category</th>
                        <th className="hidden w-[100px] bg-slate-900 px-0 py-4 text-center text-xs font-semibold tracking-wide text-slate-200 min-[900px]:table-cell">Status</th>
                        <th className="hidden w-[100px] bg-slate-900 px-0 py-4 text-center text-xs font-semibold tracking-wide text-slate-200 min-[900px]:table-cell">Size</th>
                        <th className="w-[100px] bg-slate-900 px-0 py-4 text-center text-xs font-semibold tracking-wide text-slate-200">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    {children}
                </tbody>
            </table>
            {footer &&
                <div className="py-3 text-center">{footer}</div>
            }
        </div>
    );
}

export type PageRowProps = {
    isUploading?: boolean,
    isSelected: boolean,
    isRemoving: boolean,
    name: string,
    category: string,
    status: string,
    percentage?: string,
    error?: string,
    fileSizeBytes: number,
    actions: ReactNode,
    onRowSelectionChanged: (isSelected: boolean) => void
}
export function PageRow(props: PageRowProps) {
    return (
        <tr className={`${props.isRemoving ? "opacity-20" : ""} ${props.isUploading ? "bg-cyan-400/5 [&+tr]:border-t-[3px] [&+tr]:border-slate-900" : ""}`}>
            <td className="max-w-[200px] whitespace-nowrap border-b border-white/5 py-3 pl-0 pr-1 text-left align-middle text-slate-300">
                <TriCheckbox state={props.isSelected} onChange={props.onRowSelectionChanged}>
                    <Truncate>{props.name}</Truncate>
                    <div className="block min-[900px]:hidden">
                        <div className="mb-1 mt-1 flex gap-2.5">
                            <StatusBadge status={props.status} percentage={props.percentage} error={props.error} />
                            <CategoryBadge category={props.category} />
                        </div>
                        <div className="font-mono text-xs text-slate-400">{formatFileSize(props.fileSizeBytes)}</div>
                    </div>
                </TriCheckbox>
            </td>
            <td className="hidden max-w-[200px] whitespace-nowrap border-b border-white/5 px-1 py-3 text-center align-middle text-slate-300 min-[900px]:table-cell">
                <CategoryBadge category={props.category} />
            </td>
            <td className="hidden max-w-[200px] whitespace-nowrap border-b border-white/5 px-1 py-3 text-center align-middle text-slate-300 min-[900px]:table-cell">
                <StatusBadge status={props.status} percentage={props.percentage} error={props.error} />
            </td>
            <td className="hidden max-w-[200px] whitespace-nowrap border-b border-white/5 px-1 py-3 text-center align-middle font-mono text-xs text-slate-300 min-[900px]:table-cell">
                {formatFileSize(props.fileSizeBytes)}
            </td>
            <td className="max-w-[200px] whitespace-nowrap border-b border-white/5 px-1 py-3 text-center align-middle text-slate-300">
                <div className="flex flex-col items-end justify-center gap-2.5 pr-5 min-[410px]:flex-row min-[410px]:items-center min-[410px]:pr-0">
                    {props.actions}
                </div>
            </td>
        </tr>
    );
}

export function CategoryBadge({ category }: { category: string }) {
    const categoryLower = category?.toLowerCase();
    const categoryColor = categoryLower === "movies"
        ? "border-blue-500/40 bg-blue-500/15 text-blue-200"
        : categoryLower === "tv"
            ? "border-cyan-500/40 bg-cyan-500/15 text-cyan-200"
            : "";
    return <Badge className={`inline-block w-[85px] text-center ${categoryColor}`}>{categoryLower}</Badge>
}