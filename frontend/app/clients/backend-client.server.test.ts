import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { backendClient } from "./backend-client.server";

const fetchMock = vi.fn<typeof fetch>();

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  vi.stubEnv("BACKEND_URL", "http://backend");
  vi.stubEnv("FRONTEND_BACKEND_API_KEY", "test-api-key");
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  vi.clearAllMocks();
});

describe("BackendClient", () => {
  it("gets onboarding status with the backend API key", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ isOnboarding: true }));

    await expect(backendClient.isOnboarding()).resolves.toBe(true);
    expect(fetchMock).toHaveBeenCalledWith("http://backend/api/is-onboarding", {
      method: "GET",
      headers: {
        "Content-Type": "application/json",
        "x-api-key": "test-api-key",
      },
    });
  });

  it.each([
    ["createAccount", "create-account", "status", true],
    ["authenticate", "authenticate", "authenticated", true],
  ] as const)(
    "%s posts credentials as form data",
    async (method, endpoint, resultKey, result) => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ [resultKey]: result }));

      await expect(backendClient[method]("alice", "secret")).resolves.toBe(result);
      const [url, init] = fetchMock.mock.calls[0];
      const form = init?.body as FormData;

      expect(url).toBe(`http://backend/api/${endpoint}`);
      expect(init?.method).toBe("POST");
      expect(init?.headers).toEqual({ "x-api-key": "test-api-key" });
      expect(Object.fromEntries(form.entries())).toEqual({
        username: "alice",
        password: "secret",
        type: "admin",
      });
    },
  );

  it("gets queue and history payloads", async () => {
    const queue = { slots: [], noofslots: 0 };
    const history = { slots: [], noofslots: 0 };
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ queue }))
      .mockResolvedValueOnce(jsonResponse({ history }));

    await expect(backendClient.getQueue(25)).resolves.toEqual(queue);
    await expect(backendClient.getHistory(10)).resolves.toEqual(history);
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "http://backend/api?mode=queue&limit=25",
      "http://backend/api?mode=history&pageSize=10",
    ]);
  });

  it("gets, updates, and defaults config items", async () => {
    const configItems = [{ configName: "one", configValue: "value" }];
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ configItems }))
      .mockResolvedValueOnce(jsonResponse({}))
      .mockResolvedValueOnce(jsonResponse({ status: true }));

    await expect(backendClient.getConfig(["one"])).resolves.toEqual(configItems);
    const getForm = fetchMock.mock.calls[0][1]?.body as FormData;
    expect(getForm.getAll("config-keys")).toEqual(["one"]);

    await expect(backendClient.getConfig(["missing"])).resolves.toEqual([]);

    await expect(backendClient.updateConfig(configItems)).resolves.toBe(true);
    const updateForm = fetchMock.mock.calls[2][1]?.body as FormData;
    expect(updateForm.get("one")).toBe("value");
  });

  it("lists WebDAV directories", async () => {
    const items = [{ name: "movie", isDirectory: true, size: null }];
    fetchMock.mockResolvedValueOnce(jsonResponse({ items }));

    await expect(backendClient.listWebdavDirectory("/view")).resolves.toEqual(items);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://backend/api/list-webdav-directory");
    expect((init?.body as FormData).get("directory")).toBe("/view");
  });

  it("adds an NZB using the configured manual category", async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse({
        configItems: [{ configName: "api.manual-category", configValue: "movies" }],
      }))
      .mockResolvedValueOnce(jsonResponse({ nzo_ids: ["nzo-1"] }));
    const file = new File(["nzb"], "movie.nzb");

    await expect(backendClient.addNzb(file)).resolves.toBe("nzo-1");
    const [url, init] = fetchMock.mock.calls[1];
    expect(url).toBe("http://backend/api?mode=addfile&cat=movies&priority=0&pp=0");
    expect((init?.body as FormData).get("nzbFile")).toBeInstanceOf(File);
  });

  it("gets health queue and history with optional page sizes", async () => {
    const queue = { uncheckedCount: 0, items: [] };
    const history = { stats: [], items: [] };
    fetchMock
      .mockResolvedValueOnce(jsonResponse(queue))
      .mockResolvedValueOnce(jsonResponse(history));

    await expect(backendClient.getHealthCheckQueue(30)).resolves.toEqual(queue);
    await expect(backendClient.getHealthCheckHistory()).resolves.toEqual(history);
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "http://backend/api/get-health-check-queue?pageSize=30",
      "http://backend/api/get-health-check-history",
    ]);
  });

  it("includes the backend error when a request fails", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ error: "unavailable" }, 503));

    await expect(backendClient.getQueue(1)).rejects.toThrow(
      "Failed to get queue: unavailable",
    );
  });
});
