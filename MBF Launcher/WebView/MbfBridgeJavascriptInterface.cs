using Android.Webkit;
using Java.Interop;
using System.Collections.Concurrent;
using System.Linq;

namespace MBF_Launcher.WebView
{
    /// <summary>
    /// Android <c>JavascriptInterface</c> that exposes ADB connections to the
    /// WebView page.  It is registered as <c>window.__mbfBridgeNative</c> and is
    /// consumed by <c>bridge.js</c>, which presents the higher-level
    /// <c>window.__mbfBridge</c> API to the page.
    ///
    /// The actual socket transport is provided by an <see cref="IAdbSocketFactory"/>,
    /// allowing both real TCP connections (via <see cref="TcpAdbSocketFactory"/>) and
    /// virtual/simulated connections to be used interchangeably.
    ///
    /// Flow control
    /// ------------
    /// Each connection holds a <see cref="SemaphoreSlim"/> initialised with
    /// <see cref="FlowWindow"/> permits.  Before dispatching every incoming data
    /// chunk the read-loop acquires one permit (blocking when all permits are
    /// in-flight).  A permit is released only after the page calls
    /// <see cref="AckAdb"/>, which bridge.js does automatically once the
    /// <c>onData</c> callback settles.  Slow callbacks therefore stall the read
    /// loop, creating natural back-pressure.
    ///
    /// Dispatch path
    /// -------------
    /// Events are delivered to JS via <see cref="Android.Webkit.WebView.EvaluateJavascript"/>
    /// posted directly onto the WebView's Looper with <see cref="Android.Views.View.Post"/>,
    /// bypassing the extra MAUI main-thread marshal for lower latency.  Writes
    /// (<see cref="WriteAdb"/>) are handled synchronously and return their result
    /// directly to the JS caller, eliminating a full <c>evaluateJavascript</c>
    /// round-trip per write.
    /// </summary>
    [Android.Runtime.Preserve(AllMembers = true)]
    internal sealed class MbfBridgeJavascriptInterface : Java.Lang.Object
    {
        private const int FlowWindow = 8;
        private const int ReadBufferSize = 64 * 1024;

        private readonly Android.Webkit.WebView _nativeWebView;
        private readonly IAdbSocketFactory _socketFactory;
        private readonly ConcurrentDictionary<string, Connection> _connections = new();

        // Shared watcher connection that waits for the server to stop. This is
        // created when the first JS connection is opened and disposed when the
        // last JS connection is closed.
        private readonly object _watcherLock = new();
        private Connection? _watcherConn;

        public MbfBridgeJavascriptInterface(Android.Webkit.WebView nativeWebView, IAdbSocketFactory socketFactory)
        {
            _nativeWebView = nativeWebView;
            _socketFactory = socketFactory;
        }

        // ------------------------------------------------------------------
        // Methods called by JavaScript
        // ------------------------------------------------------------------

        /// <summary>
        /// Opens a new TCP connection to the ADB server.
        /// Resolves (via <c>window.__mbfBridgeDispatch</c>) with either
        /// <c>('connected', callbackId, connectionId)</c> or
        /// <c>('error', callbackId, message)</c>.
        /// </summary>
        [JavascriptInterface]
        [Export("connectAdb")]
        public void ConnectAdb(string callbackId)
        {
            _ = Task.Run(async () =>
            {
                var id = Guid.NewGuid().ToString();
                try
                {
                    var socket = await _socketFactory.ConnectAsync();
                    var conn = new Connection(id, socket);
                    _connections[id] = conn;

                    // Ensure the shared watcher exists when the first connection
                    // is opened (or when new connections appear after it was
                    // previously disposed).
                    _ = EnsureWatcherAsync();

                    DispatchFire($"window.__mbfBridgeDispatch('connected',{J(callbackId)},{J(id)})");
                    _ = ReadLoop(conn);
                }
                catch (Exception ex)
                {
                    DispatchFire($"window.__mbfBridgeDispatch('error',{J(callbackId)},{J(ex.Message)})");
                }
            });
        }

        /// <summary>
        /// Writes base64-encoded bytes to an existing connection and returns
        /// <c>"true"</c> on success or <c>"false"</c> on failure synchronously
        /// to the JS caller.  No <c>write_result</c> dispatch event is emitted,
        /// eliminating a full <c>evaluateJavascript</c> round-trip per write.
        /// </summary>
        [JavascriptInterface]
        [Export("writeAdb")]
        public string WriteAdb(string connectionId, string base64Data)
        {
            if (!_connections.TryGetValue(connectionId, out var conn))
                return "false";
            try
            {
                var bytes = Convert.FromBase64String(base64Data);
                conn.Stream.WriteAsync(bytes).GetAwaiter().GetResult();
                return "true";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MbfBridge] WriteAdb error on {connectionId}: {ex.Message}");
                _ = Task.Run(() => RemoveAndClose(connectionId));
                return "false";
            }
        }

        /// <summary>
        /// Releases one flow-control permit, allowing the read loop to dispatch
        /// the next pending data chunk.  Called automatically by bridge.js once
        /// an <c>onData</c> callback settles.
        /// </summary>
        [JavascriptInterface]
        [Export("ackAdb")]
        public void AckAdb(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var conn))
                conn.Semaphore.Release();
        }

        /// <summary>
        /// Closes an existing connection and fires <c>('closed', connectionId)</c>.
        /// </summary>
        [JavascriptInterface]
        [Export("closeAdb")]
        public void CloseAdb(string connectionId)
        {
            _ = Task.Run(() => RemoveAndClose(connectionId));
        }

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------

        private async Task ReadLoop(Connection conn)
        {
            var buf = new byte[ReadBufferSize];
            try
            {
                while (true)
                {
                    // Acquire a flow-control permit BEFORE reading.
                    // This blocks when JS has not yet acknowledged FlowWindow
                    // chunks, propagating back-pressure to the ADB socket —
                    // matching the Rust/Tauri acquire-then-read pattern.
                    // The cancellation token unblocks this wait when the
                    // connection is explicitly closed.
                    await conn.Semaphore.WaitAsync(conn.CancellationToken);

                    var n = await conn.Stream.ReadAsync(buf, conn.CancellationToken);
                    if (n == 0) break;

                    var b64 = Convert.ToBase64String(buf, 0, n);
                    // Fire-and-forget: post directly onto the WebView Looper
                    // without the extra MAUI main-thread marshal.  Flow control
                    // (back-pressure) is handled independently through the
                    // semaphore + ackAdb mechanism, so no await is needed here.
                    DispatchFire($"window.__mbfBridgeDispatch('data',{J(conn.Id)},{J(b64)})");
                }
            }
            catch (OperationCanceledException)
            {
                // Connection was explicitly closed; exit the loop cleanly.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MbfBridge] ReadLoop error on {conn.Id}: {ex.Message}");
            }
            finally
            {
                await RemoveAndClose(conn.Id);
            }
        }

        private Task RemoveAndClose(string id)
        {
            if (_connections.TryRemove(id, out var conn))
            {
                // Cancel the CancellationToken so WaitAsync in the read loop
                // unblocks immediately instead of waiting for JS to ack.
                conn.Cts.Cancel();
                conn.Socket.Close();
                DispatchFire($"window.__mbfBridgeDispatch('closed',{J(id)})");

                // If there are no more active JS connections, dispose the
                // shared watcher connection.
                if (_connections.IsEmpty)
                {
                    DisposeWatcher();
                }
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ensure the shared watcher connection exists. If it does not, create
        /// it and run the watcher loop which will observe the watcher socket
        /// and, on close, remove all active JS connections (updating state on
        /// the JS side).
        /// </summary>
        private Task EnsureWatcherAsync()
        {
            lock (_watcherLock)
            {
                if (_watcherConn != null)
                {
                    return Task.CompletedTask;
                }

                // Create watcher asynchronously (fire-and-forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var socket = await _socketFactory.ConnectAsync();
                        var watcher = new Connection("__watcher__", socket);

                        lock (_watcherLock)
                        {
                            _watcherConn = watcher;
                        }

                        // Start the watcher loop which will wait until the
                        // watcher stream closes (e.g. when the server stops).
                        await WatcherLoop(watcher);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MbfBridge] Watcher creation failed: {ex.Message}");
                        // If watcher cannot be created, we don't treat this as a
                        // fatal error for existing connections. JS-side should
                        // continue to operate; connections will fail individually
                        // if the server goes away.
                    }
                });

                return Task.CompletedTask;
            }
        }

        private async Task WatcherLoop(Connection watcher)
        {
            var buf = new byte[ReadBufferSize];
            try
            {
                // Read until the watcher stream ends. The watcher service that
                // the client requested should hold the connection open until
                // the server stops; when it closes, the ReadAsync will return 0
                // or throw and we then propagate closure to all active
                // JS connections.
                while (true)
                {
                    var n = await watcher.Stream.ReadAsync(buf, watcher.CancellationToken);
                    if (n == 0) break;
                    // Ignore data from watcher; it is only used as a sentinel.
                }
            }
            catch (OperationCanceledException)
            {
                // Watcher was disposed explicitly; nothing to do.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MbfBridge] WatcherLoop error: {ex.Message}");
            }
            finally
            {
                // When watcher closes, remove all active JS connections. Use a
                // snapshot of keys to avoid modifying the collection while
                // enumerating.
                var keys = _connections.Keys.ToArray();
                foreach (var k in keys)
                {
                    // RemoveAndClose will only dispatch if the connection was
                    // still present (TryRemove) so duplicate events are
                    // prevented.
                    await RemoveAndClose(k);
                }

                // Dispose the watcher if it wasn't already disposed.
                DisposeWatcher();
            }
        }

        private void DisposeWatcher()
        {
            lock (_watcherLock)
            {
                if (_watcherConn is null) return;

                try
                {
                    _watcherConn.Cts.Cancel();
                }
                catch { }

                try
                {
                    _watcherConn.Socket.Close();
                }
                catch { }

                _watcherConn = null;
            }
        }

        /// <summary>
        /// Posts <paramref name="script"/> directly onto the WebView's Looper
        /// for evaluation.  This avoids the extra round-trip through MAUI's
        /// <c>MainThread.InvokeOnMainThreadAsync</c> for lower latency.
        /// The call is fire-and-forget; no result is captured.
        /// </summary>
        private void DispatchFire(string script)
        {
            var nw = _nativeWebView;
            nw.Post(() => nw.EvaluateJavascript(script, null));
        }

        /// <summary>Serialises <paramref name="s"/> as a JSON string literal.</summary>
        private static string J(string s) =>
            System.Text.Json.JsonSerializer.Serialize(s);

        // ------------------------------------------------------------------
        // Per-connection state
        // ------------------------------------------------------------------

        private sealed class Connection
        {
            public string Id { get; }
            public IAdbSocket Socket { get; }
            public Stream Stream => Socket.Stream;

            /// <summary>Flow-control semaphore; starts full at <see cref="FlowWindow"/> permits.</summary>
            public SemaphoreSlim Semaphore { get; } = new(FlowWindow, FlowWindow);

            /// <summary>
            /// Cancellation source whose token is passed to both
            /// <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/> and
            /// <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/>
            /// in the read loop.  Cancelled by <c>RemoveAndClose</c> so that an
            /// explicit close unblocks the loop immediately.
            /// </summary>
            public CancellationTokenSource Cts { get; } = new();

            /// <summary>The token from <see cref="Cts"/>.</summary>
            public CancellationToken CancellationToken => Cts.Token;

            public Connection(string id, IAdbSocket socket)
            {
                Id = id;
                Socket = socket;
            }
        }
    }
}
