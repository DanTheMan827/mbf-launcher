using Android.Webkit;
using Java.Interop;
using System.Collections.Concurrent;

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
    /// </summary>
    [Android.Runtime.Preserve(AllMembers = true)]
    internal sealed class MbfBridgeJavascriptInterface : Java.Lang.Object
    {
        private const int FlowWindow = 8;
        private const int ReadBufferSize = 1 * 1024 * 1024;

        private readonly Microsoft.Maui.Controls.WebView _webView;
        private readonly IAdbSocketFactory _socketFactory;
        private readonly ConcurrentDictionary<string, Connection> _connections = new();

        public MbfBridgeJavascriptInterface(Microsoft.Maui.Controls.WebView webView, IAdbSocketFactory socketFactory)
        {
            _webView = webView;
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
                    await Dispatch($"window.__mbfBridgeDispatch('connected',{J(callbackId)},{J(id)})");
                    _ = ReadLoop(conn);
                }
                catch (Exception ex)
                {
                    await Dispatch($"window.__mbfBridgeDispatch('error',{J(callbackId)},{J(ex.Message)})");
                }
            });
        }

        /// <summary>
        /// Writes base64-encoded bytes to an existing connection.
        /// Resolves with <c>('write_result', callbackId, true|false)</c>.
        /// </summary>
        [JavascriptInterface]
        [Export("writeAdb")]
        public void WriteAdb(string connectionId, string base64Data, string callbackId)
        {
            _ = Task.Run(async () =>
            {
                if (!_connections.TryGetValue(connectionId, out var conn))
                {
                    await Dispatch($"window.__mbfBridgeDispatch('write_result',{J(callbackId)},false)");
                    return;
                }
                try
                {
                    var bytes = Convert.FromBase64String(base64Data);
                    await conn.Stream.WriteAsync(bytes);
                    await Dispatch($"window.__mbfBridgeDispatch('write_result',{J(callbackId)},true)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MbfBridge] WriteAdb error on {connectionId}: {ex.Message}");
                    await Dispatch($"window.__mbfBridgeDispatch('write_result',{J(callbackId)},false)");
                    await RemoveAndClose(connectionId);
                }
            });
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
                    await Dispatch($"window.__mbfBridgeDispatch('data',{J(conn.Id)},{J(b64)})");
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

        private async Task RemoveAndClose(string id)
        {
            if (_connections.TryRemove(id, out var conn))
            {
                // Cancel the CancellationToken so WaitAsync in the read loop
                // unblocks immediately instead of waiting for JS to ack.
                conn.Cts.Cancel();
                conn.Socket.Close();
                await Dispatch($"window.__mbfBridgeDispatch('closed',{J(id)})");
            }
        }

        /// <summary>Evaluates <paramref name="script"/> in the WebView on the main thread.</summary>
        private Task Dispatch(string script) =>
            MainThread.InvokeOnMainThreadAsync(() => _webView.EvaluateJavaScriptAsync(script));

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
