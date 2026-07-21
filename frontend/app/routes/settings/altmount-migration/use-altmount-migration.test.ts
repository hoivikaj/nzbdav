import { describe, expect, it } from "vitest";
import { isMigrationWorkActive, type SessionStatus } from "./use-altmount-migration";

describe("isMigrationWorkActive", () => {
    it.each<SessionStatus>(["scanning", "running", "paused", "linking", "applying"])(
        "blocks destructive wizard actions while status is %s",
        (status) => expect(isMigrationWorkActive(status)).toBe(true),
    );

    it.each<SessionStatus>(["idle", "connected", "mapped", "scanned", "complete", "cancelled", "linked"])(
        "allows destructive wizard actions after status is %s",
        (status) => expect(isMigrationWorkActive(status)).toBe(false),
    );

    it("treats an unloaded status as inactive", () => {
        expect(isMigrationWorkActive(undefined)).toBe(false);
    });
});
