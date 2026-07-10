import { useCallback, useEffect, useState } from "react";
import { useNavigation } from "react-router";

export type PageLayoutProps = {
    topNavComponent: (props: RequiredTopNavProps) => React.ReactNode,
    leftNavChild: React.ReactNode,
    bodyChild: React.ReactNode,
}

export type RequiredTopNavProps = {
    isHamburgerMenuOpen: boolean,
    onHamburgerMenuClick: () => void,
}

export function PageLayout(props: PageLayoutProps) {
    // data
    const [isHamburgerMenuOpen, setIsHamburgerMenuOpen] = useState(false);
    const isNavigating = Boolean(useNavigation().location);

    // close hamburger-menu when done navigating
    useEffect(() => {
        !isNavigating && setIsHamburgerMenuOpen(false);
    }, [isNavigating, setIsHamburgerMenuOpen]);

    // events
    const onHamburgerMenuClick = useCallback(function () {
        setIsHamburgerMenuOpen(!isHamburgerMenuOpen)
    }, [setIsHamburgerMenuOpen, isHamburgerMenuOpen]);

    const onBodyClick = useCallback(function () {
        setIsHamburgerMenuOpen(false);
    }, [setIsHamburgerMenuOpen]);

    return (
        <div className="flex h-dvh min-w-0 flex-col overflow-hidden bg-gray-900 text-white">
            <div className="z-40 h-16 shrink-0 border-b border-slate-800 bg-gray-900">
                <props.topNavComponent
                    isHamburgerMenuOpen={isHamburgerMenuOpen}
                    onHamburgerMenuClick={onHamburgerMenuClick} />
            </div>
            <div className="relative flex min-h-0 flex-1">
                <aside
                    className={`absolute inset-y-0 left-0 z-30 w-[250px] border-r border-slate-800 bg-gray-900 transition-transform duration-200 md:relative md:translate-x-0 ${
                        isHamburgerMenuOpen ? "translate-x-0" : "-translate-x-full"
                    }`}
                >
                    {props.leftNavChild}
                </aside>
                {isHamburgerMenuOpen && (
                    <button
                        aria-label="Close navigation"
                        className="absolute inset-0 z-20 bg-slate-950/60 backdrop-blur-[2px] md:hidden"
                        onClick={onBodyClick}
                    />
                )}
                <main
                    className="yes-scrollbar min-w-0 flex-1 overflow-y-auto bg-[var(--app-bg)]"
                    onClick={onBodyClick}
                >
                    {props.bodyChild}
                </main>
            </div>
        </div>
    );
}