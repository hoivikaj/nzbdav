import { describe, expect, it, vi } from "vitest";
import { isMaskedSecret } from "./config-mask";
import { formatFileSize } from "./file-size";
import { getLeafDirectoryName } from "./path";
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
