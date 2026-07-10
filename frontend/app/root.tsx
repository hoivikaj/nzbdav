import {
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
  useLocation,
  useNavigation,
} from "react-router";

import "./app.css";
import type { Route } from "./+types/root";
import { IS_FRONTEND_AUTH_DISABLED } from "~/auth/authentication.server";
import { TopNavigation } from "./routes/_index/components/top-navigation/top-navigation";
import { LeftNavigation } from "./routes/_index/components/left-navigation/left-navigation";
import { PageLayout } from "./routes/_index/components/page-layout/page-layout";
import { Loading } from "./routes/_index/components/loading/loading";
import { getAppVersion } from "./utils/version.server";
import { backendClient } from "./clients/backend-client.server";

export async function loader({ request }: Route.LoaderArgs) {
  let path = new URL(request.url).pathname;
  if (path === "/login") return { useLayout: false };
  if (path === "/onboarding") return { useLayout: false };

  const providerConfig = await backendClient.getConfig(["usenet.providers"]);

  return {
    useLayout: true,
    version: await getAppVersion(),
    isFrontendAuthDisabled: IS_FRONTEND_AUTH_DISABLED,
    hasUsenetProviders: hasConfiguredUsenetProviders(
      providerConfig.find(item => item.configName === "usenet.providers")?.configValue
    ),
  };
}

function hasConfiguredUsenetProviders(configValue?: string): boolean {
  if (!configValue) return false;

  try {
    const config = JSON.parse(configValue);
    return Array.isArray(config?.Providers) && config.Providers.length > 0;
  } catch {
    return false;
  }
}


export function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" data-appearance-theme="dark">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <link rel="icon" href="/logo.svg" />
        <Meta />
        <Links />
      </head>
      <body>
        {children}
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}

export default function App({ loaderData }: Route.ComponentProps) {
  const { useLayout, version, isFrontendAuthDisabled, hasUsenetProviders } = loaderData;
  const location = useLocation();
  const navigation = useNavigation();
  const isNavigating = Boolean(navigation.location);

  // display loading animiation during top-level page transitions,
  // but allow the `/explore` page to handle it's own loading screen.
  const isCurrentExplorePage = location.pathname.startsWith("/explore");
  const isNextExplorePage = navigation.location?.pathname?.startsWith("/explore");
  const showLoading = isNavigating && !(isCurrentExplorePage && isNextExplorePage);

  if (useLayout) {
    return (
      <PageLayout
        topNavComponent={TopNavigation}
        bodyChild={showLoading ? <Loading /> : <Outlet />}
        leftNavChild={
          <LeftNavigation
            version={version}
            isFrontendAuthDisabled={isFrontendAuthDisabled}
            hasUsenetProviders={hasUsenetProviders} />
        } />
    );
  }

  return <Outlet />;
}