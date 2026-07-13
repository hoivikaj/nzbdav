import { describe, expect, it } from "vitest";
import { safeDecodePath, shouldProxyToBackend, shouldSkipCompression } from "./proxy-path";

describe("safeDecodePath", () => {
  it("decodes valid percent-encoding", () => {
    expect(safeDecodePath("/%61pi/get-config")).toBe("/api/get-config");
  });

  it.each(["/%zz", "/view%", "/%E0%A4%A"])(
    "returns null for malformed path %s",
    (path) => {
      expect(safeDecodePath(path)).toBeNull();
    },
  );
});

describe("shouldProxyToBackend", () => {
  it.each(["PROPFIND", "propfind", "OPTIONS", "options"])(
    "proxies %s requests regardless of path",
    (method) => {
      expect(shouldProxyToBackend(method, "/unrelated")).toBe(true);
    },
  );

  it.each([
    "/api",
    "/api/get-config",
    "/view",
    "/view/movies",
    "/.ids/item",
    "/nzbs/file.nzb",
    "/content/file.mkv",
    "/completed-symlinks/movie",
    "/adapters/addon/profile-token/manifest.json",
    "/adapters/newznab/profile-token/api",
  ])("proxies backend path %s", (path) => {
    expect(shouldProxyToBackend("GET", path)).toBe(true);
  });

  it("checks decoded paths", () => {
    expect(shouldProxyToBackend("GET", "/%61pi/get-config")).toBe(true);
  });

  it.each(["/%zz", "/view%"])(
    "does not proxy malformed path %s",
    (path) => {
      expect(shouldProxyToBackend("GET", path)).toBe(false);
    },
  );

  it.each(["/", "/login", "/settings", "/assets/app.js"])(
    "leaves frontend path %s to React Router",
    (path) => {
      expect(shouldProxyToBackend("GET", path)).toBe(false);
    },
  );
});

describe("shouldSkipCompression", () => {
  it("skips compression for backend paths", () => {
    expect(shouldSkipCompression("/view/movies")).toBe(true);
    expect(shouldSkipCompression("/api/get-config")).toBe(true);
  });

  it("does not skip for frontend paths or malformed encoding", () => {
    expect(shouldSkipCompression("/login")).toBe(false);
    expect(shouldSkipCompression("/%zz")).toBe(false);
  });
});
