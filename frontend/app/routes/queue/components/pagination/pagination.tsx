import { SimpleDropdown } from "../simple-dropdown/simple-dropdown";
import { memo } from "react";
import { Icon } from "~/components/ui";

export type PaginationProps = {
    pageNumber: number,
    totalPages: number,
    onPageSelected?: (page: number) => void,
}

export const Pagination = memo(({ pageNumber, totalPages, onPageSelected }: PaginationProps) => {
    const handlePageClick = (page: number, e: React.MouseEvent) => {
        e.preventDefault();
        if (onPageSelected && page !== pageNumber && page >= 1 && page <= totalPages) {
            onPageSelected(page);
        }
    };

    const handleDropdownChange = (value: string) => {
        const page = parseInt(value, 10);
        if (onPageSelected && !isNaN(page)) {
            onPageSelected(page);
        }
    };

    const pageOptions = Array.from({ length: totalPages }, (_, i) => String(i + 1));

    return (
        <div className="flex flex-wrap items-center justify-center gap-4">
            {pageNumber > 1 ? (
                <a
                    href="#"
                    className="flex items-center gap-1 px-2 py-1 text-xs text-blue-400 hover:text-blue-300 hover:underline"
                    onClick={(e) => handlePageClick(pageNumber - 1, e)}
                >
                    <Icon name="chevron_left" className="!text-[16px]" /> Prev
                </a>
            ) : (
                <span className="flex cursor-not-allowed items-center gap-1 px-2 py-1 text-xs text-slate-600"><Icon name="chevron_left" className="!text-[16px]" /> Prev</span>
            )}

            <div className="flex items-center gap-2">
                <span className="text-xs font-medium text-slate-400">Page</span>
                <SimpleDropdown
                    type={'bordered'}
                    options={pageOptions}
                    value={String(pageNumber)}
                    onChange={handleDropdownChange}
                />
                <span className="text-xs font-medium text-slate-400">of {totalPages}</span>
            </div>

            {pageNumber < totalPages ? (
                <a
                    href="#"
                    className="flex items-center gap-1 px-2 py-1 text-xs text-blue-400 hover:text-blue-300 hover:underline"
                    onClick={(e) => handlePageClick(pageNumber + 1, e)}
                >
                    Next <Icon name="chevron_right" className="!text-[16px]" />
                </a>
            ) : (
                <span className="flex cursor-not-allowed items-center gap-1 px-2 py-1 text-xs text-slate-600">Next <Icon name="chevron_right" className="!text-[16px]" /></span>
            )}
        </div>
    );
});
