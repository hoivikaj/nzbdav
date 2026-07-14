import { describe, expect, it, vi } from "vitest";
import WebSocket from "ws";
import {
    disconnectBrowserClients,
    MAX_TOPICS_PER_SOCKET,
    parseSubscriptionTopics,
} from "./websocket.server";

describe("parseSubscriptionTopics", () => {
    it("accepts a flat state/stream map", () => {
        expect(parseSubscriptionTopics(JSON.stringify({ ls: "state", cxs: "stream" }))).toEqual({
            ls: "state",
            cxs: "stream",
        });
    });

    it("rejects arrays, non-objects, and invalid kinds", () => {
        expect(parseSubscriptionTopics("[]")).toBeNull();
        expect(parseSubscriptionTopics('"ls"')).toBeNull();
        expect(parseSubscriptionTopics(JSON.stringify({ ls: "wat" }))).toBeNull();
        expect(parseSubscriptionTopics("{")).toBeNull();
    });

    it("rejects more than MAX_TOPICS_PER_SOCKET topics", () => {
        const topics: Record<string, "state"> = {};
        for (let i = 0; i < MAX_TOPICS_PER_SOCKET + 1; i++) {
            topics[`t${i}`] = "state";
        }
        expect(parseSubscriptionTopics(JSON.stringify(topics))).toBeNull();
    });
});

describe("disconnectBrowserClients", () => {
    it("clears stale state and reconnects each browser once", () => {
        const close = vi.fn();
        const client = {
            readyState: WebSocket.OPEN,
            close,
        } as unknown as WebSocket;
        const subscriptions = new Map([
            ["ls", new Set([client])],
            ["cxs", new Set([client])],
        ]);
        const lastMessage = new Map([
            ["ls", "live"],
            ["cxs", "connections"],
        ]);

        disconnectBrowserClients(subscriptions, lastMessage);

        expect(lastMessage.size).toBe(0);
        expect(close).toHaveBeenCalledOnce();
        expect(close).toHaveBeenCalledWith(1012, "Backend websocket reconnecting");
    });
});
