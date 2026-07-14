import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getBuildCommit } from "./version.server";

describe("getBuildCommit", () => {
  const originalEnv = process.env.NZBDAV_COMMIT_SHA;
  let tempGitDir: string;

  beforeEach(async () => {
    delete process.env.NZBDAV_COMMIT_SHA;
    tempGitDir = await mkdtemp(join(tmpdir(), "nzbdav-git-"));
  });

  afterEach(async () => {
    if (originalEnv === undefined) {
      delete process.env.NZBDAV_COMMIT_SHA;
    } else {
      process.env.NZBDAV_COMMIT_SHA = originalEnv;
    }
    await rm(tempGitDir, { recursive: true, force: true });
    vi.restoreAllMocks();
  });

  it("prefers NZBDAV_COMMIT_SHA env over local git", async () => {
    process.env.NZBDAV_COMMIT_SHA = "ABCDEF0123456789abcdef0123456789abcdef01";
    await writeFile(join(tempGitDir, "HEAD"), "ref: refs/heads/main\n");
    await mkdir(join(tempGitDir, "refs", "heads"), { recursive: true });
    await writeFile(
      join(tempGitDir, "refs", "heads", "main"),
      "1111111111111111111111111111111111111111\n",
    );

    await expect(getBuildCommit(tempGitDir)).resolves.toEqual({
      sha: "abcdef0123456789abcdef0123456789abcdef01",
      branch: "main",
      source: "env",
    });
  });

  it("rejects invalid NZBDAV_COMMIT_SHA values", async () => {
    process.env.NZBDAV_COMMIT_SHA = "not-a-sha";
    await expect(getBuildCommit(tempGitDir)).resolves.toBeUndefined();
  });

  it("resolves SHA from refs/heads/main", async () => {
    await writeFile(join(tempGitDir, "HEAD"), "ref: refs/heads/main\n");
    await mkdir(join(tempGitDir, "refs", "heads"), { recursive: true });
    await writeFile(
      join(tempGitDir, "refs", "heads", "main"),
      "abcdef0123456789abcdef0123456789abcdef01\n",
    );

    await expect(getBuildCommit(tempGitDir)).resolves.toEqual({
      sha: "abcdef0123456789abcdef0123456789abcdef01",
      branch: "main",
      source: "git",
    });
  });

  it("resolves SHA from packed-refs when loose ref is missing", async () => {
    await writeFile(join(tempGitDir, "HEAD"), "ref: refs/heads/main\n");
    await writeFile(
      join(tempGitDir, "packed-refs"),
      [
        "# pack-refs with: peeled fully-peeled sorted",
        "abcdef0123456789abcdef0123456789abcdef01 refs/heads/main",
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb refs/heads/other",
        "",
      ].join("\n"),
    );

    await expect(getBuildCommit(tempGitDir)).resolves.toEqual({
      sha: "abcdef0123456789abcdef0123456789abcdef01",
      branch: "main",
      source: "git",
    });
  });

  it("returns undefined for detached HEAD", async () => {
    await writeFile(
      join(tempGitDir, "HEAD"),
      "abcdef0123456789abcdef0123456789abcdef01\n",
    );

    await expect(getBuildCommit(tempGitDir)).resolves.toBeUndefined();
  });

  it("returns undefined for non-main branches", async () => {
    await writeFile(join(tempGitDir, "HEAD"), "ref: refs/heads/feature/foo\n");
    await mkdir(join(tempGitDir, "refs", "heads", "feature"), { recursive: true });
    await writeFile(
      join(tempGitDir, "refs", "heads", "feature", "foo"),
      "abcdef0123456789abcdef0123456789abcdef01\n",
    );

    await expect(getBuildCommit(tempGitDir)).resolves.toBeUndefined();
  });

  it("returns undefined when .git is missing", async () => {
    await expect(getBuildCommit(join(tempGitDir, "missing"))).resolves.toBeUndefined();
  });
});
