/**
 * bridge.ts
 *
 * Wraps the low-level Android JavascriptInterface (`window.__mbfBridgeNative`)
 * in the high-level `window.__mbfBridge` API described in mbf-bridge.d.ts.
 *
 * This script is:
 *   - Included directly by bridge_test.html via <script src="bridge.js">.
 *   - Also injected by C# (BrowserPage) after every successful navigation, so
 *     it is available in pages that do not bundle it themselves.
 *
 * The script is idempotent: if `window.__mbfBridge` already exists (e.g. from
 * a previous injection) it returns immediately without re-registering anything.
 */
(function () {
    // Idempotency guard – safe to inject more than once.
    if (window.__mbfBridge) return;
    if (!window.__mbfBridgeNative) return;

    // -----------------------------------------------------------------------
    // Internal state
    // -----------------------------------------------------------------------

    type Callback<T> = { resolve: (value: T) => void; reject: (reason: unknown) => void };

    /** Pending one-shot callbacks keyed by a random callbackId. */
    const pending = new Map<string, Callback<unknown>>();

    /** Per-connection onData subscriber lists. */
    const dataHandlers = new Map<string, Array<(data: Uint8Array) => void | Promise<void>>>();

    /** Per-connection onClose subscriber lists. */
    const closeHandlers = new Map<string, Array<() => void>>();

    /** Set of connection IDs that have already been closed. */
    const closedSet = new Set<string>();

    // -----------------------------------------------------------------------
    // Binary helpers
    // -----------------------------------------------------------------------

    function b64ToBytes(b64: string): Uint8Array {
        const binary = atob(b64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    function bytesToB64(data: Uint8Array | ArrayBuffer): string {
        const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
        let binary = "";
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary);
    }

    // -----------------------------------------------------------------------
    // Dispatch router – called by C# via evaluateJavascript
    // -----------------------------------------------------------------------

    window.__mbfBridgeDispatch = function (type: string, ...args: unknown[]): void {
        switch (type) {
            case "connected": {
                const [callbackId, connectionId] = args as [string, string];
                const cb = pending.get(callbackId);
                pending.delete(callbackId);
                cb?.resolve(connectionId);
                break;
            }
            case "error": {
                const [callbackId, message] = args as [string, string];
                const cb = pending.get(callbackId);
                pending.delete(callbackId);
                cb?.reject(new Error(message));
                break;
            }
            case "data": {
                const [connectionId, b64] = args as [string, string];
                const bytes = b64ToBytes(b64);
                const handlers = dataHandlers.get(connectionId) ?? [];
                // Await all handlers, then release the C# flow-control permit.
                Promise.all(handlers.map((h) => h(bytes))).finally(() => {
                    window.__mbfBridgeNative!.ackAdb(connectionId);
                });
                break;
            }
            case "closed": {
                const [connectionId] = args as [string];
                if (!closedSet.has(connectionId)) {
                    closedSet.add(connectionId);
                    const handlers = closeHandlers.get(connectionId) ?? [];
                    closeHandlers.delete(connectionId);
                    dataHandlers.delete(connectionId);
                    handlers.forEach((h) => h());
                }
                break;
            }
            case "write_result": {
                const [callbackId, success] = args as [string, boolean];
                const cb = pending.get(callbackId);
                pending.delete(callbackId);
                cb?.resolve(success);
                break;
            }
        }
    };

    // -----------------------------------------------------------------------
    // Connection factory
    // -----------------------------------------------------------------------

    function makeConnection(connectionId: string): MbfAdbConnection {
        dataHandlers.set(connectionId, []);
        closeHandlers.set(connectionId, []);

        const conn: MbfAdbConnection = {
            get id() {
                return connectionId;
            },
            get closed() {
                return closedSet.has(connectionId);
            },

            write(data: Uint8Array | ArrayBuffer): Promise<boolean> {
                const b64 = bytesToB64(data);
                const callbackId = crypto.randomUUID();
                return new Promise<boolean>((resolve, reject) => {
                    pending.set(callbackId, { resolve: resolve as Callback<unknown>["resolve"], reject });
                    window.__mbfBridgeNative!.writeAdb(connectionId, b64, callbackId);
                });
            },

            onData(callback: (data: Uint8Array) => void | Promise<void>): () => void {
                const handlers = dataHandlers.get(connectionId)!;
                handlers.push(callback);
                return () => {
                    const idx = handlers.indexOf(callback);
                    if (idx >= 0) handlers.splice(idx, 1);
                };
            },

            onClose(callback: () => void): () => void {
                if (closedSet.has(connectionId)) {
                    // Already closed; fire synchronously and return a no-op.
                    callback();
                    return () => { /* no-op */ };
                }
                const handlers = closeHandlers.get(connectionId)!;
                handlers.push(callback);
                return () => {
                    const idx = handlers.indexOf(callback);
                    if (idx >= 0) handlers.splice(idx, 1);
                };
            },

            close(): Promise<void> {
                if (closedSet.has(connectionId)) return Promise.resolve();
                closedSet.add(connectionId);
                const onCloseList = closeHandlers.get(connectionId) ?? [];
                closeHandlers.delete(connectionId);
                dataHandlers.delete(connectionId);
                onCloseList.forEach((h) => h());
                window.__mbfBridgeNative!.closeAdb(connectionId);
                return Promise.resolve();
            },
        };

        return conn;
    }

    // -----------------------------------------------------------------------
    // Public bridge singleton
    // -----------------------------------------------------------------------

    window.__mbfBridge = {
        isAvailable: true,

        get isAdbAvailable(): boolean {
            return window.__mbfIsAdbAvailable !== false;
        },

        connect(): Promise<MbfAdbConnection> {
            if (!this.isAdbAvailable) {
                return Promise.reject(new Error("ADB is not available"));
            }
            const callbackId = crypto.randomUUID();
            return new Promise<MbfAdbConnection>((resolve, reject) => {
                pending.set(callbackId, {
                    resolve: (id) => resolve(makeConnection(id as string)),
                    reject,
                });
                window.__mbfBridgeNative!.connectAdb(callbackId);
            });
        },
    };
})();
