import { describe, expect, it } from "vitest";
import { shouldWarnCleartextCredentials } from "./cleartext-credentials";

describe("shouldWarnCleartextCredentials", () => {
  it("warns when SSL is off and a username is set", () => {
    expect(shouldWarnCleartextCredentials(false, "user")).toBe(true);
  });

  it("does not warn when SSL is on", () => {
    expect(shouldWarnCleartextCredentials(true, "user")).toBe(false);
  });

  it("does not warn when username is empty or whitespace", () => {
    expect(shouldWarnCleartextCredentials(false, "")).toBe(false);
    expect(shouldWarnCleartextCredentials(false, "   ")).toBe(false);
  });
});
