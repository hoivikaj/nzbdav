import { describe, expect, it } from "vitest";
import { shouldProxyToBackend } from "./proxy-path";

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
  ])("proxies backend path %s", (path) => {
    expect(shouldProxyToBackend("GET", path)).toBe(true);
  });

  it("checks decoded paths", () => {
    expect(shouldProxyToBackend("GET", "/%61pi/get-config")).toBe(true);
  });

  it.each(["/", "/login", "/settings", "/assets/app.js"])(
    "leaves frontend path %s to React Router",
    (path) => {
      expect(shouldProxyToBackend("GET", path)).toBe(false);
    },
  );
});
