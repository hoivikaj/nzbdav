import { describe, expect, it } from "vitest";
import { EMPTY_OVERVIEW_STATS, mergeOverviewStats } from "./merge-overview-stats";
import type { OverviewStatsResponse } from "~/clients/backend-client.server";

function partial(overrides: Partial<OverviewStatsResponse> & { includedSections: string[] }): OverviewStatsResponse {
    return { ...EMPTY_OVERVIEW_STATS, ...overrides };
}

describe("mergeOverviewStats", () => {
    it("merges window section without wiping static data", () => {
        const withStatic = mergeOverviewStats(
            EMPTY_OVERVIEW_STATS,
            partial({
                includedSections: ["static"],
                catalogue: { fileCount: 42, totalBytes: 100, largestFileBytes: 50, addedLast7Days: 3 },
                indexers: [{ name: "NZB", completed: 1, failed: 0, bytesCompleted: 10, avgSeconds: 2, successRate: 1 }],
            }),
        );

        const withWindow = mergeOverviewStats(
            withStatic,
            partial({
                includedSections: ["window"],
                totalArticles: 99,
                throughput: [{ bucket: 1, articles: 5, errors: 0, bytesServed: 10 }],
                tiles: { activeReads: 2, articlesPerMinute: 10, errorsPerMinute: 0, bytesServedPerMinute: 1000 },
            }),
        );

        expect(withWindow.totalArticles).toBe(99);
        expect(withWindow.throughput).toHaveLength(1);
        expect(withWindow.catalogue.fileCount).toBe(42);
        expect(withWindow.indexers).toHaveLength(1);
        expect(withWindow.includedSections).toEqual(expect.arrayContaining(["static", "window"]));
    });

    it("merges detail latency without clearing window throughput", () => {
        const withWindow = mergeOverviewStats(
            EMPTY_OVERVIEW_STATS,
            partial({
                includedSections: ["window"],
                throughput: [{ bucket: 1, articles: 5, errors: 0, bytesServed: 10 }],
            }),
        );

        const withDetail = mergeOverviewStats(
            withWindow,
            partial({
                includedSections: ["detail"],
                latency: { p50Ms: 12, p95Ms: 40, p99Ms: 90, samples: 100, buckets: [] },
                errors: [{ status: "Missing", count: 3 }],
            }),
        );

        expect(withDetail.throughput).toHaveLength(1);
        expect(withDetail.latency.p50Ms).toBe(12);
        expect(withDetail.errors).toEqual([{ status: "Missing", count: 3 }]);
    });
});
