import type { ReactNode } from "react"
import { useId } from "react"

export type WideViewportProps = {
    width: string,
    children: ReactNode,
}

export function WideViewport({ width, children }: WideViewportProps) {
    const id = useId();
    const uniqueId = `wide-viewport-${id.replace(/:/g, '-')}`;

    return (
        <>
            <style dangerouslySetInnerHTML={{
                __html: `
                    @media not (min-width: ${width}) {
                        .${uniqueId} {
                            display: none !important;
                        }
                    }
                `
            }} />
            <div className={`block ${uniqueId}`}>
                {children}
            </div>
        </>
    );
}
