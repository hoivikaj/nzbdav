import styles from "./page-table.module.css";
import type { ReactNode } from "react";
import { Link } from "react-router";
import { TriCheckbox, type TriCheckboxState } from "../tri-checkbox/tri-checkbox";
import { Truncate } from "../truncate/truncate";
import { StatusBadge } from "../status-badge/status-badge";
import { formatFileSize } from "~/utils/file-size";
import type { ProviderUsage } from "~/clients/backend-client.server";

const desktopHeaderClass = "hidden w-[120px] bg-slate-900 px-1 py-4 text-center text-xs font-semibold tracking-wide text-slate-200 min-[900px]:table-cell";
const desktopCellClass = "hidden max-w-[200px] whitespace-nowrap border-b border-white/5 px-1 py-3 text-center align-middle text-slate-300 min-[900px]:table-cell";

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
                        <th className={desktopHeaderClass}>Category</th>
                        <th className={desktopHeaderClass}>Indexer</th>
                        <th className={desktopHeaderClass}>Provider</th>
                        <th className={desktopHeaderClass}>Status</th>
                        <th className={desktopHeaderClass}>Size</th>
                        <th className="w-[100px] bg-slate-900 px-1 py-4 text-center text-xs font-semibold text-slate-200">Actions</th>
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
    nameHref?: string | null,
    category: string,
    status: string,
    percentage?: string,
    error?: string,
    fileSizeBytes: number,
    actions: ReactNode,
    indexer?: string | null,
    providers?: ProviderUsage[] | null,
    onRowSelectionChanged: (isSelected: boolean) => void
}
export function PageRow(props: PageRowProps) {
    const nameContent = props.nameHref
        ? <Link to={props.nameHref} className="text-slate-200 hover:text-white hover:underline" onClick={e => e.stopPropagation()}>{props.name}</Link>
        : props.name;

    return (
        <tr className={`${props.isRemoving ? "opacity-20" : ""} ${props.isUploading ? "bg-cyan-400/5 [&+tr]:border-t-[3px] [&+tr]:border-slate-900" : ""}`}>
            <td className="max-w-[200px] whitespace-nowrap border-b border-white/5 py-3 pl-0 pr-1 text-left align-middle text-slate-300">
                <TriCheckbox state={props.isSelected} onChange={props.onRowSelectionChanged}>
                    <Truncate>{nameContent}</Truncate>
                    <div className="block min-[900px]:hidden">
                        <div className="mb-1 mt-1 flex gap-2.5">
                            <StatusBadge status={props.status} percentage={props.percentage} error={props.error} />
                            <CategoryBadge category={props.category} />
                            {props.indexer && <IndexerBadge indexer={props.indexer} />}
                            {props.providers && props.providers.length > 0 && <ProvidersBadge providers={props.providers} />}
                        </div>
                        <div className="font-mono text-xs text-slate-400">{formatFileSize(props.fileSizeBytes)}</div>
                    </div>
                </TriCheckbox>
            </td>
            <td className="hidden max-w-[200px] whitespace-nowrap border-b border-white/5 px-1 py-3 text-center align-middle text-slate-300 min-[900px]:table-cell">
                <CategoryBadge category={props.category} />
            </td>
            <td className={desktopCellClass}>
                {props.indexer ? <IndexerBadge indexer={props.indexer} /> : <span className={styles.emptyCell}>—</span>}
            </td>
            <td className={desktopCellClass}>
                {props.providers && props.providers.length > 0
                    ? <ProvidersBadge providers={props.providers} />
                    : <span className={styles.emptyCell}>—</span>}
            </td>
            <td className={desktopCellClass}>
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
    return <div className={styles.categoryBadge}>{categoryLower}</div>
}

export function IndexerBadge({ indexer }: { indexer: string }) {
    return <div className={styles.indexerBadge} title={`Indexer: ${indexer}`}>via {indexer}</div>
}

const MAX_INLINE_PROVIDERS = 3;

export function ProvidersBadge({ providers }: { providers: ProviderUsage[] }) {
    if (providers.length === 0) return null;
    const total = providers.reduce((acc, p) => acc + p.segments, 0);
    const visible = providers.slice(0, MAX_INLINE_PROVIDERS);
    const hidden = providers.length - visible.length;
    const labelOf = (p: ProviderUsage) => p.nickname?.trim() || stripHost(p.host);
    const tooltip = providers
        .map(p => total > 0
            ? `${labelOf(p)} (${p.host}): ${p.segments} segments (${Math.round((p.segments / total) * 100)}%)`
            : `${labelOf(p)} (${p.host}): idle`)
        .join("\n");
    return (
        <div className={styles.providersBadge} title={tooltip}>
            {visible.map((p, i) => (
                <span key={p.host} className={styles.providersEntry}>
                    {i > 0 && <span className={styles.providersSep}>·</span>}
                    <span className={styles.providersHost}>{labelOf(p)}</span>
                    {total > 0 && (
                        <span className={styles.providersPct}>
                            {Math.round((p.segments / total) * 100)}%
                        </span>
                    )}
                </span>
            ))}
            {hidden > 0 && <span className={styles.providersMore}>+{hidden}</span>}
        </div>
    );
}

// Generic NNTP hostname prefixes that aren't brand-identifying.
const GENERIC_HOST_PREFIXES = new Set(["news", "reader", "premium", "secure", "ssl", "nntp", "usenet", "block"]);

function stripHost(host: string): string {
    if (!host) return "—";
    const labels = host.split(".").filter(Boolean);
    if (labels.length === 0) return host;
    if (labels.length === 1) return labels[0];
    if (labels.length === 2) return labels[0];
    // 3+ labels: skip a generic prefix to get to the brand label
    if (GENERIC_HOST_PREFIXES.has(labels[0].toLowerCase())) return labels[1];
    // pick whichever of the first two is longer (heuristic for "more identifying")
    return labels[0].length >= labels[1].length ? labels[0] : labels[1];
}