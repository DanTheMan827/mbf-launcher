/**
 * Global ambient type declarations for the MBF Launcher WebView bridge.
 *
 * These types are available to all TypeScript files in the web project
 * without any import statement.
 */

// ---------------------------------------------------------------------------
// Connection handle
// ---------------------------------------------------------------------------

/** Handle to an open ADB TCP connection proxied through the C# JSI bridge. */
interface MbfAdbConnection {
    /** Unique connection identifier (UUID). */
    readonly id: string;

    /** `true` once the connection has been closed from either side. */
    readonly closed: boolean;

    /**
     * Write raw bytes to the ADB connection.
     * @param data Bytes to send (Uint8Array or ArrayBuffer).
     * @returns Promise that resolves to `true` on success, `false` if the
     *          connection ID is unknown on the native side.
     */
    write(data: Uint8Array | ArrayBuffer): Promise<boolean>;

    /**
     * Register a callback for incoming data chunks.
     *
     * The callback receives one chunk per call.  It may be async; the bridge
     * awaits completion before acknowledging the chunk to C#, so a slow
     * callback naturally throttles the data stream (back-pressure).
     *
     * @returns Unsubscribe function – call it to stop receiving data.
     */
    onData(callback: (data: Uint8Array) => void | Promise<void>): () => void;

    /**
     * Register a callback for connection close events.
     * Fires when `close()` is called by JS or the ADB server closes the connection.
     * @returns Unsubscribe function.
     */
    onClose(callback: () => void): () => void;

    /**
     * Close the connection.
     * Fires `onClose` listeners immediately, then sends the native close command.
     * Safe to call multiple times; subsequent calls are no-ops.
     */
    close(): Promise<void>;
}

// ---------------------------------------------------------------------------
// Bridge singleton
// ---------------------------------------------------------------------------

/** MBF bridge API, available as `window.__mbfBridge` inside the MAUI WebView. */
interface MbfBridge {
    /** Always `true` – confirms the MBF Launcher context. */
    readonly isAvailable: true;

    /**
     * `true` on all platforms.  On Android this assumes a custom adbd instance
     * is reachable on the configured ADB port.
     */
    readonly isAdbAvailable: boolean;

    /**
     * Open a new ADB TCP connection via the C# JSI bridge.
     * @throws If ADB is not available or the connection fails.
     */
    connect(): Promise<MbfAdbConnection>;
}

// ---------------------------------------------------------------------------
// Native JSI object (set by addJavascriptInterface)
// ---------------------------------------------------------------------------

/** Low-level Android JSI object registered as `window.__mbfBridgeNative`. */
interface MbfBridgeNative {
    /** Initiates a TCP connection to the ADB server; result arrives via `__mbfBridgeDispatch`. */
    connectAdb(callbackId: string): void;
    /** Writes base64-encoded bytes; result arrives via `__mbfBridgeDispatch`. */
    writeAdb(connectionId: string, base64Data: string, callbackId: string): void;
    /** Releases one flow-control permit for the given connection. */
    ackAdb(connectionId: string): void;
    /** Closes the connection for the given ID. */
    closeAdb(connectionId: string): void;
}

// ---------------------------------------------------------------------------
// Window augmentation
// ---------------------------------------------------------------------------

interface Window {
    /** MBF bridge – present only when running inside MBF Launcher's WebView. */
    __mbfBridge?: MbfBridge;

    /**
     * Set by C# before bridge.js runs.
     * `false` if ADB is explicitly unavailable; absent or `true` otherwise.
     * @internal
     */
    __mbfIsAdbAvailable?: boolean;

    /** Low-level JSI object registered by Android. @internal */
    __mbfBridgeNative?: MbfBridgeNative;

    /** Callback router invoked by C# to deliver events. @internal */
    __mbfBridgeDispatch?: (type: string, ...args: unknown[]) => void;
}
