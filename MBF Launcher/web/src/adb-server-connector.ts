import { AdbServerClient } from "@yume-chan/adb";
import { MaybeConsumable } from "@yume-chan/stream-extra";

/**
 * Implements {@link AdbServerClient.ServerConnector} over the
 * {@link window.__mbfBridge} API, routing each ADB host-protocol connection
 * through the native C# {@link MbfBridgeJavascriptInterface}.
 */
export class MbfAdbServerConnector implements AdbServerClient.ServerConnector {
    async connect(): Promise<AdbServerClient.ServerConnection> {
        const conn = await window.__mbfBridge!.connect();

        let resolveClose!: (v: undefined) => void;
        const closed = new Promise<undefined>(res => { resolveClose = res; });
        conn.onClose(() => resolveClose(undefined));

        const readable = new ReadableStream<Uint8Array>({
            start(controller) {
                conn.onData(chunk => controller.enqueue(chunk));
                conn.onClose(() => { try { controller.close(); } catch { /* already closed */ } });
            },
        });

        const writable = new WritableStream<MaybeConsumable<Uint8Array>>({
            write(chunk): Promise<void> {
                return MaybeConsumable.tryConsume(chunk, data => conn.write(data)).then(() => {});
            },
            close(): Promise<void> { return conn.close(); },
            abort(): Promise<void> { return conn.close(); },
        });

        return {
            readable,
            writable,
            get closed() { return closed; },
            close: () => conn.close(),
        };
    }

    addReverseTunnel(): never {
        throw new Error("Reverse tunnels are not supported by MbfAdbServerConnector");
    }

    removeReverseTunnel(): never {
        throw new Error("Reverse tunnels are not supported by MbfAdbServerConnector");
    }

    clearReverseTunnels(): never {
        throw new Error("Reverse tunnels are not supported by MbfAdbServerConnector");
    }
}
