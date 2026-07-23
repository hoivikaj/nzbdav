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

/** Match path on segment boundaries so `/apifoo` does not match `/api`. */
export function matchesBackendPathPrefix(decodedPath: string): boolean {
  return BACKEND_PATH_PREFIXES.some((prefix) => {
    const normalized = prefix.endsWith("/") ? prefix.slice(0, -1) : prefix;
    return decodedPath === normalized || decodedPath.startsWith(normalized + "/");
  });
}

/** True when the path is under `/api` (decoded, segment-bounded). */
export function isBackendApiPath(pathname: string): boolean {
  const decodedPath = safeDecodePath(pathname);
  if (decodedPath === null) return false;
  return decodedPath === "/api" || decodedPath.startsWith("/api/");
}

export function shouldProxyToBackend(method: string, pathname: string): boolean {
  const normalizedMethod = method.toUpperCase();
  if (normalizedMethod === "PROPFIND" || normalizedMethod === "OPTIONS") {
    return true;
  }

  const decodedPath = safeDecodePath(pathname);
  if (decodedPath === null) return false;

  return matchesBackendPathPrefix(decodedPath);
}

/** True when compression should be skipped for backend-proxied media/API paths. */
export function shouldSkipCompression(pathname: string): boolean {
  const decodedPath = safeDecodePath(pathname);
  if (decodedPath === null) return false;
  return matchesBackendPathPrefix(decodedPath);
}

// React Router streams SSR HTML and calls res.flush() after every chunk.
// Express `compression` maps that to zlib.Gzip.flush(), which stacks 'drain'
// listeners under backpressure and trips MaxListenersExceededWarning (11+).
// Skip Gzip for document navigations; static assets still compress normally.
export function isHtmlDocumentRequest(req: {
  headers: { accept?: string | string[] };
}): boolean {
  const accept = req.headers.accept;
  const value = Array.isArray(accept) ? accept.join(",") : (accept ?? "");
  return /\btext\/html\b/i.test(value);
}
