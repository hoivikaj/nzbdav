const BACKEND_PATH_PREFIXES = [
  "/api",
  "/view",
  "/.ids",
  "/nzbs",
  "/content",
  "/completed-symlinks",
  "/adapters/",
];

/** Decode a path; return null on malformed percent-encoding instead of throwing. */
export function safeDecodePath(path: string): string | null {
  try {
    return decodeURIComponent(path);
  } catch {
    return null;
  }
}

export function shouldProxyToBackend(method: string, pathname: string): boolean {
  const normalizedMethod = method.toUpperCase();
  if (normalizedMethod === "PROPFIND" || normalizedMethod === "OPTIONS") {
    return true;
  }

  const decodedPath = safeDecodePath(pathname);
  if (decodedPath === null) return false;

  return BACKEND_PATH_PREFIXES.some((prefix) => decodedPath.startsWith(prefix));
}

/** True when compression should be skipped for backend-proxied media/API paths. */
export function shouldSkipCompression(pathname: string): boolean {
  const decodedPath = safeDecodePath(pathname);
  if (decodedPath === null) return false;
  return BACKEND_PATH_PREFIXES.some((prefix) => decodedPath.startsWith(prefix));
}
