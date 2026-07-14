import WebSocket, { WebSocketServer } from 'ws';
import { isAuthenticated } from "../app/auth/authentication.server";
import type { IncomingMessage } from 'http';
import { logger } from "./logger";

export const MAX_WEBSOCKET_PAYLOAD_BYTES = 64 * 1024;
export const MAX_TOPICS_PER_SOCKET = 100;

const TOPIC_KINDS = new Set(["state", "stream"]);

/** Validate browser subscription payloads: flat Record<string, "state" | "stream">. */
export function parseSubscriptionTopics(raw: string): Record<string, "state" | "stream"> | null {
    let parsed: unknown;
    try {
        parsed = JSON.parse(raw);
    } catch {
        return null;
    }
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return null;

    const topics: Record<string, "state" | "stream"> = {};
    for (const [topic, kind] of Object.entries(parsed as Record<string, unknown>)) {
        if (typeof topic !== "string" || topic.length === 0) return null;
        if (kind !== "state" && kind !== "stream") return null;
        if (!TOPIC_KINDS.has(kind)) return null;
        topics[topic] = kind;
    }
    if (Object.keys(topics).length > MAX_TOPICS_PER_SOCKET) return null;
    return topics;
}

function initializeWebsocketServer(wss: WebSocketServer) {
    // keep track of socket subscriptions
    const websockets = new Map<WebSocket, Record<string, "state" | "stream">>();
    const subscriptions = new Map<string, Set<WebSocket>>();
    const lastMessage = new Map<string, string>();
    initializeWebsocketClient(subscriptions, lastMessage);

    // authenticate new websocket sessions
    wss.on("connection", (ws: WebSocket, request: IncomingMessage) => {
        // Buffer early frames and attach handlers before awaiting auth so the
        // browser's immediate onopen subscription is not dropped.
        let authenticated = false;
        let closed = false;
        const pendingMessages: WebSocket.MessageEvent[] = [];

        const applySubscription = (event: WebSocket.MessageEvent) => {
            const topics = parseSubscriptionTopics(event.data.toString());
            if (!topics) {
                ws.close(1003, "Could not process topic subscription. If recently updated, try refreshing the page.");
                return;
            }

            const previous = websockets.get(ws);
            if (previous) {
                for (const topic of Object.keys(previous)) {
                    subscriptions.get(topic)?.delete(ws);
                }
            }

            websockets.set(ws, topics);
            for (const topic of Object.keys(topics)) {
                const topicSubscriptions = subscriptions.get(topic);
                if (topicSubscriptions) topicSubscriptions.add(ws);
                else subscriptions.set(topic, new Set<WebSocket>([ws]));
                if (topics[topic] === 'state') {
                    const messageToSend = lastMessage.get(topic);
                    if (messageToSend) ws.send(messageToSend);
                }
            }
        };

        ws.onmessage = (event: WebSocket.MessageEvent) => {
            if (!authenticated) {
                pendingMessages.push(event);
                return;
            }
            applySubscription(event);
        };

        ws.onclose = () => {
            closed = true;
            pendingMessages.length = 0;
            const topics = websockets.get(ws);
            if (topics) {
                websockets.delete(ws);
                for (const topic of Object.keys(topics)) {
                    const topicSubscriptions = subscriptions.get(topic);
                    if (topicSubscriptions) topicSubscriptions.delete(ws);
                }
            }
        };

        void (async () => {
            try {
                if (!await isAuthenticated(request)) {
                    logger.debug(`Rejected unauthenticated websocket connection from ${request.socket.remoteAddress ?? "unknown IP"}`);
                    ws.close(1008, "Unauthorized");
                    return;
                }
                if (closed) return;
                authenticated = true;
                for (const event of pendingMessages.splice(0)) {
                    applySubscription(event);
                }
            } catch (error) {
                logger.error("Error authenticating websocket session", error);
                ws.close(1011, "Internal server error");
            }
        })();
    });
}

export function initializeWebsocketClient(subscriptions: Map<string, Set<WebSocket>>, lastMessage: Map<string, string>) {
    let reconnectRetryDelay = 1000;
    let reconnectTimeout: NodeJS.Timeout | null = null;
    let connected = false;
    let connectionFailures = 0;
    let lastFailureLogAt = 0;
    let loggedStartupWait = false;
    const startedAt = Date.now();
    const startupGraceMs = 30_000;
    const url = getBackendWebsocketUrl();

    function logConnectionFailure(message: string, error?: unknown) {
        const now = Date.now();
        connectionFailures += 1;

        // During the first ~30s after frontend start (e.g. Docker starts the
        // frontend before --db-migration binds the backend port), connection
        // refusals are expected. Log once at info without the error stack.
        if (now - startedAt < startupGraceMs) {
            if (!loggedStartupWait) {
                logger.info("Waiting for backend to start...");
                loggedStartupWait = true;
                lastFailureLogAt = now;
            }
            return;
        }

        if (connectionFailures === 1 || now - lastFailureLogAt >= 60_000) {
            logger.warn(`${message}; retrying in ${reconnectRetryDelay} ms`, error);
            lastFailureLogAt = now;
        }
    }

    function connect() {
        const socket = new WebSocket(url);

        socket.on('error', (error: Error) => {
            if (!connected) {
                logConnectionFailure(`Could not connect to backend websocket at ${url}`, error);
            } else {
                logger.warn("Backend websocket error", error);
            }
        });

        socket.onopen = () => {
            const reconnected = connectionFailures > 0;
            connected = true;
            connectionFailures = 0;
            lastFailureLogAt = 0;
            logger.info(reconnected ? "Backend websocket reconnected" : "Backend websocket connected");
            if (reconnectTimeout) {
                clearTimeout(reconnectTimeout);
                reconnectTimeout = null;
            }

            socket.send(Buffer.from(process.env.FRONTEND_BACKEND_API_KEY!, "utf-8"), { binary: false });
        };

        socket.onmessage = (event: WebSocket.MessageEvent) => {
            try {
                const rawMessage = event.data.toString();
                const topicMessage: unknown = JSON.parse(rawMessage);
                if (!topicMessage || typeof topicMessage !== "object") return;

                const { Topic: topic, Message: message } = topicMessage as Record<string, unknown>;
                if (typeof topic !== "string" || typeof message !== "string") return;

                lastMessage.set(topic, rawMessage);
                const subscribed = subscriptions.get(topic) || [];
                subscribed.forEach(client => {
                    if (client.readyState === client.OPEN) {
                        client.send(rawMessage);
                    }
                });
            } catch (error) {
                logger.error("Ignoring malformed backend websocket message", error);
            }
        };

        socket.onclose = (event: WebSocket.CloseEvent) => {
            if (connected) {
                connected = false;
                logConnectionFailure(
                    `Backend websocket closed (code ${event.code}, reason: ${event.reason || "none"})`,
                );
            } else if (connectionFailures === 0) {
                logConnectionFailure(`Could not connect to backend websocket at ${url}`);
            }
            disconnectBrowserClients(subscriptions, lastMessage);
            scheduleReconnect();
        };
    }

    function scheduleReconnect() {
        if (reconnectTimeout) clearTimeout(reconnectTimeout);

        reconnectTimeout = setTimeout(() => {
            connect();
        }, reconnectRetryDelay);
    }

    connect();
}

export function disconnectBrowserClients(
    subscriptions: Map<string, Set<WebSocket>>,
    lastMessage: Map<string, string>,
) {
    lastMessage.clear();
    const clients = new Set<WebSocket>();
    subscriptions.forEach(topicClients => topicClients.forEach(client => clients.add(client)));
    clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.close(1012, "Backend websocket reconnecting");
        }
    });
}

function getBackendWebsocketUrl() {
    const host = process.env.BACKEND_URL!;
    return `${host.replace(/\/$/, '')}/ws`.replace(/^http/, 'ws');
}

export const websocketServer = {
    initialize: initializeWebsocketServer
}