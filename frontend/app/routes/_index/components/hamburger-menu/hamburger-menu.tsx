import { Icon } from "~/components/ui";

export type HamburgerMenuProps = {
    isOpen: boolean
    onClick: () => void,
}

export function HamburgerMenu(props: HamburgerMenuProps) {
    return (
        <button
            type="button"
            aria-label={props.isOpen ? "Close navigation" : "Open navigation"}
            aria-expanded={props.isOpen}
            onClick={props.onClick}
            className="flex h-10 w-10 items-center justify-center rounded-full bg-white/5 text-slate-200 hover:bg-white/10 md:hidden"
        >
            <Icon name={props.isOpen ? "close" : "menu"} className="!text-[26px]" />
        </button>
    );
}