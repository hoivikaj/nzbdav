import { useCallback } from "react";
import { Icon } from "~/components/ui";

type LinkClickEvent = React.MouseEvent<HTMLAnchorElement, MouseEvent>;
interface EmptyQueueProps {
    onUploadClicked?: () => void;
}

export function EmptyQueue(props: EmptyQueueProps) {
    const onUploadClicked = useCallback((e: LinkClickEvent) => {
        e.preventDefault();
        props.onUploadClicked?.call(null);
    }, [props.onUploadClicked]);

    return (
        <div className="flex min-h-[300px] -translate-y-5 flex-col items-center justify-center text-center text-slate-400">
            <Icon name="celebration" className="mb-4 !text-[48px] text-slate-500" />
            <div className="mb-2 text-lg font-semibold text-slate-300">Empty Queue!</div>
            <div className="mx-auto max-w-[400px] text-xs leading-relaxed">
                <a className="text-blue-400 hover:text-blue-300 hover:underline" href="#" onClick={onUploadClicked}>Upload an nzb file</a> to get started
            </div>
        </div>
    );
}