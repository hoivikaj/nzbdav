import { useCallback, useEffect } from "react";
import type { HistoryEvents, QueueEvents } from "./events-controller";
import { useWebsocketTopics } from "~/utils/shared-websocket";

const topicNames = {
    queueItemStatus: 'qs',
    queueItemPercentage: 'qp',
    queueItemProviders: 'qpv',
    queueItemAdded: 'qa',
    queueItemRemoved: 'qr',
    queueItemMoved: 'qm',
    historyItemAdded: 'ha',
    historyItemRemoved: 'hr',
};

const topicSubscriptions = {
    [topicNames.queueItemStatus]: 'state',
    [topicNames.queueItemPercentage]: 'state',
    [topicNames.queueItemProviders]: 'state',
    [topicNames.queueItemAdded]: 'event',
    [topicNames.queueItemRemoved]: 'event',
    [topicNames.queueItemMoved]: 'event',
    [topicNames.historyItemAdded]: 'event',
    [topicNames.historyItemRemoved]: 'event',
} as const;

export function useQueueHistoryWebsocket(
    queueEvents: QueueEvents,
    historyEvents: HistoryEvents,
    isQueueLive: boolean,
    isHistoryLive: boolean,
) {
    const onWebsocketMessage = useCallback((topic: string, message: string) => {
        if (topic == topicNames.queueItemAdded) {
            if (isQueueLive) queueEvents.onAddQueueSlot(JSON.parse(message));
        }
        else if (topic == topicNames.queueItemRemoved) {
            if (isQueueLive) queueEvents.onRemoveQueueSlots(new Set<string>(message.split(',')));
        }
        else if (topic == topicNames.queueItemMoved) {
            queueEvents.onMoveQueueSlotsToTop(new Set<string>(message.split(',').filter(Boolean)));
        }
        else if (topic == topicNames.queueItemStatus)
            queueEvents.onChangeQueueSlotStatus(message);
        else if (topic == topicNames.queueItemPercentage)
            queueEvents.onChangeQueueSlotPercentage(message);
        else if (topic == topicNames.queueItemProviders)
            queueEvents.onChangeQueueSlotProviders(message);
        else if (topic == topicNames.historyItemAdded) {
            if (isHistoryLive) historyEvents.onAddHistorySlot(JSON.parse(message));
        }
        else if (topic == topicNames.historyItemRemoved) {
            if (isHistoryLive) historyEvents.onRemoveHistorySlots(new Set<string>(message.split(',')));
        }
    }, [
        queueEvents,
        historyEvents,
        isQueueLive,
        isHistoryLive
    ]);

    useWebsocketTopics(topicSubscriptions, onWebsocketMessage);
}
