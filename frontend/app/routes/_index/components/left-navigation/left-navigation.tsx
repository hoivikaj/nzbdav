import { Form, Link, useLocation, useNavigation } from "react-router";
import type React from "react";
import { LiveUsenetConnections } from "../live-usenet-connections/live-usenet-connections";
import { Icon } from "~/components/ui";

export type LeftNavigationProps = {
    version?: string,
    isFrontendAuthDisabled?: boolean,
    hasUsenetProviders?: boolean,
}

export function LeftNavigation({ version, isFrontendAuthDisabled, hasUsenetProviders }: LeftNavigationProps) {
    return (
        <div className="flex h-full flex-col px-3 py-5 text-slate-400">
            <nav className="flex flex-col gap-1">
                <Item target="/queue" icon="list_alt">Queue & History</Item>
                <Item target="/explore" icon="folder_open">DAV Explore</Item>
                <Item target="/health" icon="health_and_safety">Health</Item>
                <Item target="/settings" icon="settings">Settings</Item>
            </nav>
            <LiveUsenetConnections hasUsenetProviders={!!hasUsenetProviders} />

            <footer className="mt-auto border-t border-slate-800 pt-4 text-xs text-slate-500">
                <div className="mb-3 flex items-center gap-3">
                    <Link
                        to="https://github.com/nzbdav/nzbdav"
                        className="flex items-center gap-1 hover:text-blue-400"
                    >
                        <Icon name="code" className="!text-[16px]" />
                        GitHub
                    </Link>
                    <Link
                        to="https://github.com/nzbdav/nzbdav/releases"
                        className="hover:text-blue-400"
                    >
                        Changelog
                    </Link>
                </div>
                <div className="font-mono text-[11px]">v{version || "unknown"}</div>
                {!isFrontendAuthDisabled && (
                    <Form method="post" action="/logout">
                        <input name="confirm" value="true" type="hidden" />
                        <button
                            className="mt-3 flex w-full items-center gap-2 rounded-md px-2 py-2 text-left text-sm text-slate-400 hover:bg-white/10 hover:text-white"
                            type="submit"
                        >
                            <Icon name="logout" className="!text-[18px]" />
                            Logout
                        </button>
                    </Form>
                )}
            </footer>
        </div>
    );
}

function Item({ target, icon, children }: { target: string, icon: string, children: React.ReactNode }) {
    const location = useLocation();
    const navigation = useNavigation();
    const pathname = navigation.location?.pathname ?? location.pathname;
    const isSelected = pathname.startsWith(target);
    return (
        <Link
            to={target}
            className={`group flex items-center gap-3 rounded-md px-3 py-2.5 text-sm font-medium transition-all ${
                isSelected
                    ? "bg-blue-500/15 text-blue-400"
                    : "text-slate-400 hover:bg-white/10 hover:text-white"
            }`}
        >
            <Icon name={icon} filled={isSelected} className="!text-[21px] transition-transform group-hover:scale-105" />
            {children}
        </Link>
    );
}