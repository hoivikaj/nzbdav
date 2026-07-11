import { describe, expect, it, vi } from "vitest";
import { isMaskedSecret } from "./config-mask";
import { formatFileSize } from "./file-size";
import { getExploreContentLink, getLeafDirectoryName } from "./path";
import { className, classNames } from "./styling";
import { receiveMessage } from "./websocket-util";

describe("formatFileSize", () => {
  it.each([
    [undefined, "unknown size"],
    [null, "unknown size"],
    [0, "0 B"],
    [1024, "1 KB"],
    [1536, "1.5 KB"],
    [1024 ** 3, "1 GB"],
  ])("formats %s bytes as %s", (bytes, expected) => {
    expect(formatFileSize(bytes)).toBe(expected);
  });
});

describe("getLeafDirectoryName", () => {
  it.each([
    ["/view/movies/Alien", "Alien"],
    ["/view/movies/Alien/", "Alien"],
    ["C:\\media\\Alien\\", "Alien"],
    ["Alien", "Alien"],
  ])("gets the leaf from %s", (path, expected) => {
    expect(getLeafDirectoryName(path)).toBe(expected);
  });
});

describe("getExploreContentLink", () => {
  it("builds an explore content URL", () => {
    expect(getExploreContentLink("/completed/movies/Alien", "movies"))
      .toBe("/explore/content/movies/Alien");
  });

  it("encodes category and folder segments", () => {
    expect(getExploreContentLink("/completed/tv shows/Show Name", "tv shows"))
      .toBe("/explore/content/tv%20shows/Show%20Name");
  });

  it("returns null when storage or category is missing", () => {
    expect(getExploreContentLink(null, "movies")).toBeNull();
    expect(getExploreContentLink("/completed/movies/Alien", null)).toBeNull();
    expect(getExploreContentLink("", "movies")).toBeNull();
  });
});

describe("secret masking", () => {
  it("recognizes only masked secret values", () => {
    expect(isMaskedSecret("__NZBDAV_SECRET_MASK_V1__:abc")).toBe(true);
    expect(isMaskedSecret("abc")).toBe(false);
    expect(isMaskedSecret(undefined)).toBe(false);
  });
});

describe("class name helpers", () => {
  const values: (string | false | null | undefined)[] = [
    "card",
    false,
    null,
    undefined,
    "active",
  ];

  it("joins truthy class names", () => {
    expect(classNames(values)).toBe("card active");
  });

  it("returns a className property", () => {
    expect(className(values)).toEqual({ className: "card active" });
  });
});

describe("receiveMessage", () => {
  it("parses a websocket message and forwards its values", () => {
    const onMessage = vi.fn();
    const handler = receiveMessage(onMessage);

    handler({
      data: JSON.stringify({ Topic: "queue", Message: "updated" }),
    } as MessageEvent);

    expect(onMessage).toHaveBeenCalledOnce();
    expect(onMessage).toHaveBeenCalledWith("queue", "updated");
  });
});
