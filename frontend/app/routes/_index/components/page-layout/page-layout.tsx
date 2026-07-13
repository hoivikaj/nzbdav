import { useCallback, useEffect, useId, useState } from "react";
import { useNavigation } from "react-router";

export type PageLayoutProps = {
    topNavComponent: (props: RequiredTopNavProps) => React.ReactNode,
    leftNavChild: React.ReactNode,
    bodyChild: React.ReactNode,
}

export type RequiredTopNavProps = {
    isHamburgerMenuOpen: boolean,
    onHamburgerMenuClick: () => void,
    drawerToggleId: string,
}

export function PageLayout(props: PageLayoutProps) {
    const drawerToggleId = useId();
    const [isHamburgerMenuOpen, setIsHamburgerMenuOpen] = useState(false);
    const isNavigating = Boolean(useNavigation().location);

    useEffect(() => {
        if (!isNavigating) setIsHamburgerMenuOpen(false);
    }, [isNavigating]);

    const onHamburgerMenuClick = useCallback(() => {
        setIsHamburgerMenuOpen((open) => !open);
    }, []);

    return (
        <div className="flex h-dvh flex-col overflow-hidden bg-base-300 text-base-content">
            <div className="navbar z-40 h-16 min-h-16 shrink-0 border-b border-base-content/10 bg-base-300 px-0">
                <props.topNavComponent
                    isHamburgerMenuOpen={isHamburgerMenuOpen}
                    onHamburgerMenuClick={onHamburgerMenuClick}
                    drawerToggleId={drawerToggleId}
                />
            </div>

            <div className="drawer min-h-0 flex-1 lg:drawer-open">
                <input
                    id={drawerToggleId}
                    type="checkbox"
                    className="drawer-toggle"
                    checked={isHamburgerMenuOpen}
                    onChange={(event) => setIsHamburgerMenuOpen(event.target.checked)}
                />
                <div className="drawer-content flex min-h-0 min-w-0 flex-col overflow-hidden">
                    <main className="yes-scrollbar min-h-0 min-w-0 flex-1 overflow-y-auto bg-base-300">
                        {props.bodyChild}
                    </main>
                </div>
                <div className="drawer-side z-50">
                    <label
                        htmlFor={drawerToggleId}
                        aria-label="Close navigation"
                        className="drawer-overlay"
                    />
                    <aside className="flex h-full min-h-0 w-64 max-w-[85vw] flex-col border-r border-base-content/10 bg-base-300">
                        {props.leftNavChild}
                    </aside>
                </div>
            </div>
        </div>
    );
}
