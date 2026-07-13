import { memo } from "react";
import { Form, useNavigate } from "react-router";
import type { RequiredTopNavProps } from "../page-layout/page-layout";
import { LiveUsenetConnections } from "../live-usenet-connections/live-usenet-connections";
import { Icon } from "~/components/ui";

export type TopNavigationProps = RequiredTopNavProps & {
  version?: string,
  updateAvailable?: { latestVersion: string; releaseUrl: string } | null,
  isFrontendAuthDisabled?: boolean,
  hasUsenetProviders?: boolean,
};

export const TopNavigation = memo(function TopNavigation(props: TopNavigationProps) {
  const {
    isHamburgerMenuOpen,
    drawerToggleId,
    version,
    updateAvailable,
    isFrontendAuthDisabled,
    hasUsenetProviders,
  } = props;
  const navigate = useNavigate();
  const displayVersion = version || "unknown";
  const hasUpdate = Boolean(updateAvailable);

  return (
    <>
      <div className="navbar-start gap-1 px-2 md:px-4">
        <label
          htmlFor={drawerToggleId}
          aria-label={isHamburgerMenuOpen ? "Close navigation" : "Open navigation"}
          aria-expanded={isHamburgerMenuOpen}
          className="btn btn-ghost btn-square btn-sm lg:hidden"
        >
          <Icon name={isHamburgerMenuOpen ? "close" : "menu"} className="!text-[24px]" />
        </label>
        <button
          type="button"
          className="btn btn-ghost gap-3 px-2"
          onClick={() => navigate("/")}
        >
          <img className="h-8 w-7" src="/logo.svg" alt="" />
          <span className="text-xl font-bold tracking-tight">Nzb DAV</span>
        </button>
      </div>

      <div className="navbar-end items-center gap-2 px-2 md:px-4">
        <LiveUsenetConnections hasUsenetProviders={!!hasUsenetProviders} />
        <div className="dropdown dropdown-end">
          <button
            type="button"
            tabIndex={0}
            className={
              hasUpdate
                ? "btn btn-primary btn-sm gap-2"
                : "btn btn-ghost h-9 min-h-9 gap-2 rounded-full border border-base-content/10 bg-base-200/70 px-3 hover:border-base-content/20 hover:bg-base-200"
            }
            aria-label={hasUpdate ? "Update available" : "App menu"}
          >
            {hasUpdate ? (
              <>
                <Icon name="arrow_circle_up" className="!text-[20px]" />
                <span className="text-sm font-semibold">Update available</span>
              </>
            ) : (
              <>
                <span className="inline-flex items-center gap-2">
                  <span className="text-[10px] font-semibold uppercase tracking-[0.14em] text-base-content/40">
                    Stable
                  </span>
                  <span className="h-3 w-px bg-base-content/15" aria-hidden="true" />
                  <span className="font-mono text-sm tracking-tight text-base-content/80">
                    {displayVersion}
                  </span>
                </span>
                <Icon name="expand_more" className="!text-[18px] text-base-content/50" />
              </>
            )}
          </button>
          <ul
            tabIndex={0}
            className="dropdown-content menu z-50 mt-2 w-64 rounded-box border border-base-content/10 bg-base-200 p-2 shadow-lg"
          >
            <li className="menu-title">
              <span className="flex items-center justify-between gap-2">
                <span>NzbDav Stable</span>
                <span className="font-mono font-normal normal-case tracking-normal">
                  {displayVersion}
                </span>
              </span>
            </li>
            {updateAvailable && (
              <li>
                <a
                  href={updateAvailable.releaseUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="bg-primary/15 font-medium text-primary"
                >
                  <Icon name="arrow_circle_up" className="!text-[18px]" />
                  Update to v{updateAvailable.latestVersion}
                </a>
              </li>
            )}
            <li>
              <a
                href="https://github.com/nzbdav/nzbdav"
                target="_blank"
                rel="noreferrer"
              >
                <Icon name="code" className="!text-[18px]" />
                GitHub
              </a>
            </li>
            <li>
              <a
                href="https://github.com/nzbdav/nzbdav/releases"
                target="_blank"
                rel="noreferrer"
              >
                <Icon name="history" className="!text-[18px]" />
                Changelog
              </a>
            </li>
            {!isFrontendAuthDisabled && (
              <>
                <div className="divider my-1" />
                <li>
                  <Form method="post" action="/logout" className="contents">
                    <input name="confirm" value="true" type="hidden" />
                    <button type="submit">
                      <Icon name="logout" className="!text-[18px]" />
                      Logout
                    </button>
                  </Form>
                </li>
              </>
            )}
          </ul>
        </div>
      </div>
    </>
  );
});
