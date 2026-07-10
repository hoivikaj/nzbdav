import { memo, useCallback, useEffect, useRef, useState, type ChangeEvent } from "react"
import { Icon } from "~/components/ui";

export type SimpleDropdownProps = {
    type?: "plain" | "bordered"
    options: string[],
    value?: string,
    onChange?: (value: string) => void,
    valueRef?: React.RefObject<string>,
}

export const SimpleDropdown = memo(({ type, options, value, onChange, valueRef }: SimpleDropdownProps) => {
    // validation
    if (!valueRef && (!value || !onChange)) {
        throw new Error("SimpleDropdown requires either the valueRef prop or both the value and onChange props.")
    }

    // state variables
    const [internalValue, setInternalValue] = useState(options.length > 0 ? options[0] : "");
    const [isOpen, setIsOpen] = useState(false);
    const [openAbove, setOpenAbove] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    // derived variables
    const renderedValue = value || internalValue;
    const containerClassNames = `relative inline-block ${type === "bordered" ? "rounded border border-slate-600 px-1" : ""}`;

    // events
    const toggleDropdown = useCallback(() => {
        if (!isOpen && dropdownRef.current) {
            const rect = dropdownRef.current.getBoundingClientRect();
            const viewportHeight = window.innerHeight;
            setOpenAbove(rect.top > viewportHeight / 2);
        }
        setIsOpen(prev => !prev);
    }, [isOpen]);

    const handleSelectedOptionChange = useCallback((option: string) => {
        if (!!valueRef) {
            setInternalValue(option);
            valueRef.current = option;
        }
        else if (!!onChange) {
            onChange(option);
        }
    }, [valueRef, setInternalValue, onChange]);

    const handleOptionClick = useCallback((option: string) => {
        handleSelectedOptionChange(option);
        setIsOpen(false);
    }, [onChange]);

    const handleNativeChange = useCallback((e: ChangeEvent<HTMLSelectElement>) => {
        handleSelectedOptionChange(e.target.value);
    }, [handleSelectedOptionChange]);

    // close dropdown when clicking outside
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };

        if (isOpen) {
            document.addEventListener('mousedown', handleClickOutside);
        }

        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, [isOpen]);

    // view
    return (
        <div className={containerClassNames} ref={dropdownRef}>
            {/* hidden native select for mobile devices */}
            <select
                aria-label="Select option"
                className="absolute inset-0 z-10 block min-w-[70px] cursor-pointer opacity-0 min-[900px]:hidden"
                value={renderedValue}
                onChange={handleNativeChange}
            >
                {options.map(option => (
                    <option key={option} value={option}>{option}</option>
                ))}
            </select>

            {/* styled visible dropdown box */}
            <button
                type="button"
                aria-haspopup="listbox"
                aria-expanded={isOpen}
                className="flex select-none items-center gap-1 py-0 text-xs font-medium text-slate-400 hover:text-slate-300"
                onClick={toggleDropdown}
            >
                {renderedValue}
                <Icon name="expand_more" className={`!text-[16px] transition-transform duration-200 ${isOpen ? "rotate-180" : ""}`} />
            </button>

            {/* styled dropdown selection options for desktop devices */}
            {isOpen && (
                <div
                    role="listbox"
                    className={`absolute left-0 z-[900] hidden max-h-[300px] min-w-[75px] overflow-y-auto rounded border border-slate-700 bg-slate-900 shadow-xl min-[900px]:block ${openAbove ? "bottom-full mb-1" : "top-full mt-1"}`}
                >
                    {options.map(option => (
                        <button
                            type="button"
                            role="option"
                            aria-selected={option === renderedValue}
                            key={option}
                            className="block w-full select-none whitespace-nowrap px-3 py-2 text-left text-xs font-medium text-slate-400 hover:bg-slate-700/40 hover:text-slate-200"
                            onClick={() => handleOptionClick(option)}
                        >
                            {option}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
});
