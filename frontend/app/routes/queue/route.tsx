import { Link } from "react-router";
import type { Route } from "./+types/route";
import { Alert } from "~/components/ui";
import { backendClient, type HistorySlot, type QueueSlot } from "~/clients/backend-client.server";
import { HistoryTable } from "./components/history-table/history-table";
import { QueueTable } from "./components/queue-table/queue-table";
import { useState, useRef } from "react";
import { useHistoryEvents, useQueueEvents } from "./controllers/events-controller";
import { initializeQueueHistoryWebsocket } from "./controllers/websocket-controller";
import { initializeUploadController } from "./controllers/nzb-upload-controller";
import { useQueueDropzone } from "./controllers/dropzone-controller";

const maxItems = 100;
export async function loader({ request }: Route.LoaderArgs) {
    const queuePromise = backendClient.getQueue(maxItems);
    const historyPromise = backendClient.getHistory(maxItems);
    const configPromise = backendClient.getConfig(["api.categories", "api.manual-category"])
    const queue = await queuePromise;
    const history = await historyPromise;
    const config = await configPromise;
    const categoriesValue = config
        .find(x => x.configName === "api.categories")
        ?.configValue ?? "uncategorized,audio,software,tv,movies";
    const manualCategory = config
        .find(x => x.configName === "api.manual-category")
        ?.configValue ?? "uncategorized";
    let categories = categoriesValue.split(',').map(x => x.trim());
    if (!categories.includes(manualCategory)) {
        categories = [manualCategory, ...categories];
    }

    return {
        queueSlots: queue?.slots || [],
        historySlots: history?.slots || [],
        totalQueueCount: queue?.noofslots || 0,
        totalHistoryCount: history?.noofslots || 0,
        categories: categories,
        manualCategory: manualCategory,
    }
}

export default function Queue(props: Route.ComponentProps) {
    const [queueSlots, setQueueSlots] = useState<PresentationQueueSlot[]>(props.loaderData.queueSlots);
    const [historySlots, setHistorySlots] = useState<PresentationHistorySlot[]>(props.loaderData.historySlots);
    const [uploadingFiles, setUploadingFiles] = useState<UploadingFile[]>([]);
    const uploadQueueRef = useRef<UploadingFile[]>([]);
    const manualCategoryRef = useRef<string>(props.loaderData.manualCategory);
    const isUploadingRef = useRef(false);
    const disableLiveView = queueSlots.length == maxItems || historySlots.length == maxItems;
    const combinedQueueSlots = [...uploadingFiles.map(file => file.queueSlot), ...queueSlots];

    // queue/history events
    const queueEvents = useQueueEvents(setUploadingFiles, setQueueSlots, uploadQueueRef);
    const historyEvents = useHistoryEvents(setHistorySlots);

    // websocket
    initializeQueueHistoryWebsocket(queueEvents, historyEvents, disableLiveView);

    // uploads
    const dropzone = useQueueDropzone(setUploadingFiles, uploadQueueRef, manualCategoryRef);
    initializeUploadController(isUploadingRef, uploadQueueRef, uploadingFiles, setUploadingFiles);

    // view
    return (
        <div className="min-h-full min-w-full px-4 py-4 text-sm text-slate-300 md:px-8">

            {/* warning */}
            {disableLiveView &&
                <Alert className="mb-8" variant="warning">
                    <b className="font-semibold">Attention</b>
                    <ul className="mb-0 list-disc pl-5">
                        <li className="mt-1">
                            Displaying the first {queueSlots.length} of {props.loaderData.totalQueueCount} queue items
                        </li>
                        <li className="mt-1">
                            Displaying the first {historySlots.length} of {props.loaderData.totalHistoryCount} history items
                        </li>
                        <li className="mt-1">
                            Live view is disabled. Manually <Link className="text-blue-400 hover:text-blue-300 hover:underline" to={'/queue'}>refresh</Link> the page for updates.
                        </li>
                        <li className="mt-1">
                            (This is a bandaid — Proper pagination will be added soon)
                        </li>
                    </ul>
                </Alert>
            }

            {/* queue */}
            <div className="mb-12 min-h-[413.9px] min-[450px]:min-h-[382.9px]">
                <div className="relative" {...dropzone.getRootProps()}>
                    {dropzone.isDragActive && <div className="pointer-events-none absolute inset-0 z-20 flex items-center justify-center rounded border-2 border-dashed border-blue-500 bg-blue-500/10" />}
                    <input {...dropzone.getInputProps()} />
                    <QueueTable
                        queueSlots={combinedQueueSlots}
                        totalQueueCount={props.loaderData.totalQueueCount + uploadingFiles.length}
                        categories={props.loaderData.categories}
                        manualCategoryRef={manualCategoryRef}
                        onIsSelectedChanged={queueEvents.onSelectQueueSlots}
                        onIsRemovingChanged={queueEvents.onRemovingQueueSlots}
                        onRemoved={queueEvents.onRemoveQueueSlots}
                        onUploadClicked={dropzone.open}
                    />
                </div>
            </div>

            {/* history */}
            {historySlots.length > 0 &&
                <HistoryTable
                    historySlots={historySlots}
                    totalHistoryCount={props.loaderData.totalHistoryCount}
                    onIsSelectedChanged={historyEvents.onSelectHistorySlots}
                    onIsRemovingChanged={historyEvents.onRemovingHistorySlots}
                    onRemoved={historyEvents.onRemoveHistorySlots}
                />
            }
        </div >
    );
}

export type PresentationHistorySlot = HistorySlot & {
    isSelected?: boolean,
    isRemoving?: boolean,
}

export type PresentationQueueSlot = QueueSlot & {
    isUploading?: boolean,
    isSelected?: boolean,
    isRemoving?: boolean,
    error?: string,
}

export type UploadingFile = {
    file: File,
    queueSlot: PresentationQueueSlot,
}