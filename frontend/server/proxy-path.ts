const BACKEND_PATH_PREFIXES = [
  "/api",
  "/view",
  "/.ids",
  "/nzbs",
  "/content",
  "/completed-symlinks",
  "/adapters/",
];

export function shouldProxyToBackend(method: string, pathname: string): boolean {
  const decodedPath = decodeURIComponent(pathname);
  const normalizedMethod = method.toUpperCase();

  return normalizedMethod === "PROPFIND"
    || normalizedMethod === "OPTIONS"
    || BACKEND_PATH_PREFIXES.some((prefix) => decodedPath.startsWith(prefix));
}
