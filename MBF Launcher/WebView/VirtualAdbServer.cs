using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;

namespace MBF_Launcher.WebView
{
    /// <summary>
    /// Simulates an ADB host server (the role normally played by <c>adb.exe</c> on
    /// port 5037) using an in-process, in-memory transport — no TCP listener, no
    /// encryption, and no RSA authentication.
    ///
    /// Authentication is intentionally omitted: the ADB auth handshake is an
    /// internal concern between the real ADB daemon and the real ADB server.
    /// The host protocol layer (which this class implements) never involves
    /// encryption or key exchange.
    ///
    /// A single "paired and connected" virtual device named
    /// <see cref="DeviceModel"/> is presented.  Shell commands and file-sync
    /// operations execute in the context of the running Android application, so
    /// they have full access to the app's storage and any paths the process can
    /// reach.
    /// </summary>
    internal sealed class VirtualAdbServer : IAdbSocketFactory, IDisposable
    {
        // ── Device identity ───────────────────────────────────────────────────

        /// <summary>Serial number shown to ADB clients.</summary>
        public const string DeviceSerial = "mbf-virtual";

        /// <summary>Model name shown in <c>adb devices -l</c> output (spaces → underscores).</summary>
        public const string DeviceModel = "MBF_Virtual_Device";

        private const string DeviceProduct = "mbflauncher";
        private const int AdbVersion = 41;        // ADB protocol version 0x29
        private const int MaxSyncChunk = 64 * 1024; // bytes per DATA chunk

        /// <summary>
        /// Feature flags advertised to clients.  Kept minimal so that only the
        /// corresponding protocol variants need to be implemented.
        /// </summary>
        private const string Features = "cmd,fixed_push_mkdir";

        /// <summary>
        /// Fallback value for <c>ro.build.version.release</c> when
        /// <c>Android.OS.Build.VERSION.Release</c> is unexpectedly null.
        /// </summary>
        private const string DefaultAndroidRelease = "14";

        // Known 64-bit ABI prefixes as defined by the Android NDK.
        private static readonly string[] Abi64Prefixes = ["arm64", "x86_64", "riscv64", "mips64"];

        /// <summary>
        /// Primary ABI of the device running the app (e.g. <c>arm64-v8a</c>).
        /// Used as the <c>device:</c> field in <c>host:devices-l</c> so that
        /// clients see the correct architecture.
        /// </summary>
        private static string DeviceAbi =>
            Android.OS.Build.SupportedAbis?.FirstOrDefault() ?? "arm64-v8a";

        // ── Disconnect signal ─────────────────────────────────────────────────

        /// <summary>
        /// Cancelled when this server instance is disposed, which represents the
        /// virtual device being disconnected.  Any pending
        /// <c>wait-for-*-disconnect</c> service calls unblock at that point.
        /// </summary>
        private readonly CancellationTokenSource _disconnectCts = new();
        private bool _disposed;

        // ── IAdbSocketFactory ─────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task<IAdbSocket> ConnectAsync()
        {
            // Two unidirectional pipes create one bidirectional in-memory channel.
            var c2s = new Pipe();   // client → server
            var s2c = new Pipe();   // server → client

            // Client-facing stream (handed to MbfBridgeJavascriptInterface):
            //   reads from s2c.Reader, writes to c2s.Writer.
            var clientStream = new DuplexStream(
                s2c.Reader.AsStream(leaveOpen: false),
                c2s.Writer.AsStream(leaveOpen: false));

            var socket = new VirtualAdbSocket(clientStream);

            // Server handler runs as a background task.  Disposing serverStream
            // when the handler exits completes both pipes, which causes the
            // client-side read to return EOF and the bridge's ReadLoop to fire
            // RemoveAndClose.
            _ = Task.Run(async () =>
            {
                using var serverStream = new DuplexStream(
                    c2s.Reader.AsStream(leaveOpen: false),
                    s2c.Writer.AsStream(leaveOpen: false));
                try
                {
                    await HandleConnectionAsync(serverStream);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VirtualAdbServer] handler error: {ex.Message}");
                }
            });

            return Task.FromResult<IAdbSocket>(socket);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        /// <summary>
        /// Signals the virtual device as disconnected, unblocking any pending
        /// <c>wait-for-*-disconnect</c> service handlers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _disconnectCts.Cancel();
            _disconnectCts.Dispose();
        }

        // ── Top-level connection handler ──────────────────────────────────────

        private async Task HandleConnectionAsync(Stream stream)
        {
            var service = await ReadServiceAsync(stream);
            if (service is null)
                return;
            await DispatchHostServiceAsync(stream, service);
        }

        // ── Host-level service dispatcher ─────────────────────────────────────

        private async Task DispatchHostServiceAsync(Stream stream, string service)
        {
            Directory.CreateDirectory($"/data/data/{SharedData.PackageID}/files");
            // ── Informational queries ────────────────────────────────────────

            if (service == "host:version")
            {
                await WriteOkayDataAsync(stream, AdbVersion.ToString("x4"));
                return;
            }

            if (service == "host:devices")
            {
                await WriteOkayDataAsync(stream, $"{DeviceSerial}\tdevice\n");
                return;
            }

            if (service == "host:devices-l")
            {
                var line = $"{DeviceSerial} device " +
                           $"product:{DeviceProduct} " +
                           $"model:{DeviceModel} " +
                           $"device:{DeviceAbi} transport_id:1\n";
                await WriteOkayDataAsync(stream, line);
                return;
            }

            if (service == "host:features" ||
                service == "host:host-features" ||
                service == $"host-serial:{DeviceSerial}:features" ||
                service == "host-transport:any:features")
            {
                await WriteOkayDataAsync(stream, Features);
                return;
            }

            // ── Wait-for services ────────────────────────────────────────────

            // wait-for-*-device / wait-for-*-connect: device is already present,
            // so both OKAYs are sent immediately.
            if (service.EndsWith(":wait-for-any-device") ||
                service.EndsWith(":wait-for-any-connect") ||
                service == "host:wait-for-any-device" ||
                service == "host:wait-for-any-connect")
            {
                await WriteOkayAsync(stream);   // request acknowledged
                await WriteOkayAsync(stream);   // condition immediately satisfied
                return;
            }

            // wait-for-*-disconnect: hold the connection open until Dispose() is
            // called (= virtual device disconnected), then send the second OKAY.
            if (service.EndsWith(":wait-for-any-disconnect") ||
                service == "host:wait-for-any-disconnect")
            {
                await WriteOkayAsync(stream);   // request acknowledged
                try
                {
                    await Task.Delay(Timeout.Infinite, _disconnectCts.Token);
                }
                catch (OperationCanceledException) { /* virtual device disconnected – expected */ }
                await WriteOkayAsync(stream);   // condition satisfied
                return;
            }

            // ── Transport switch ─────────────────────────────────────────────

            // "tport" variants: send OKAY + 8-byte little-endian transport ID,
            // then read and dispatch the next service on the same connection.
            if (service == "host:tport:any" ||
                service == $"host:tport:serial:{DeviceSerial}")
            {
                await WriteOkayAsync(stream);
                var transportIdBytes = new byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(transportIdBytes, 1);
                await stream.WriteAsync(transportIdBytes);
                var next = await ReadServiceAsync(stream);
                if (next is not null)
                    await DispatchDeviceServiceAsync(stream, next);
                return;
            }

            // Classic and transport-id variants: send OKAY only (no 8-byte ID).
            if (service == "host:transport-any" ||
                service == "host:transport-local" ||
                service == $"host:transport:{DeviceSerial}" ||
                service.StartsWith("host:transport-id:"))
            {
                await WriteOkayAsync(stream);
                var next = await ReadServiceAsync(stream);
                if (next is not null)
                    await DispatchDeviceServiceAsync(stream, next);
                return;
            }

            await WriteFailAsync(stream, $"unknown host service: {service}");
        }

        // ── Device-level service dispatcher ───────────────────────────────────

        private async Task DispatchDeviceServiceAsync(Stream stream, string service)
        {
            service = service.Replace("/data/local/tmp", $"/data/data/{SharedData.PackageID}/files");
            // Device features (can be queried after transport switch)
            if (service == "host:features" || service == "features")
            {
                await WriteOkayDataAsync(stream, Features);
                return;
            }

            // exec:getprop — try the real binary; fall back to Build-derived values.
            if (service == "exec:getprop" || service.StartsWith("exec:getprop "))
            {
                var key = service.Length > 13 ? service[13..].Trim() : string.Empty;
                await WriteOkayAsync(stream);
                await RunGetpropAsync(stream, key);
                return;
            }

            // exec: — PTY-less command execution (same impl as legacy shell here).
            if (service == "exec:" || service.StartsWith("exec:"))
            {
                var cmd = service.Length > 5 ? service[5..] : string.Empty;
                await WriteOkayAsync(stream);
                await RunShellAsync(stream, cmd);
                return;
            }

            // Legacy shell: "shell:{cmd}" or "shell:" (interactive)
            if (service == "shell:" || service.StartsWith("shell:"))
            {
                var cmd = service.Length > 6 ? service[6..] : string.Empty;
                await WriteOkayAsync(stream);
                await RunShellAsync(stream, cmd);
                return;
            }

            // File sync
            if (service == "sync:")
            {
                await WriteOkayAsync(stream);
                await HandleSyncAsync(stream);
                return;
            }

            await WriteFailAsync(stream, $"unknown service: {service}");
        }

        // ── getprop with fallback ──────────────────────────────────────────────

        /// <summary>
        /// Tries <c>/system/bin/getprop</c> first.  On any failure (SELinux
        /// denial, missing binary, empty output, non-zero exit) falls back to
        /// values derived from <c>Android.OS.Build</c>.
        /// </summary>
        private static async Task RunGetpropAsync(Stream stream, string key)
        {
            // 1. Try the real binary.
            if (await TryRunRealGetpropAsync(stream, key))
                return;

            // 2. Fall back to simulated values.
            var value = GetSimulatedProp(key);
            if (value is not null)
                await stream.WriteAsync(Encoding.UTF8.GetBytes(value + "\n"));
            // Unknown key → write nothing (matches real getprop behaviour).
        }

        private static async Task<bool> TryRunRealGetpropAsync(Stream stream, string key)
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "/system/bin/getprop",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                if (!string.IsNullOrEmpty(key))
                    proc.StartInfo.ArgumentList.Add(key);

                proc.Start();
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(output));
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a simulated <c>getprop</c> value for <paramref name="key"/>,
        /// or <c>null</c> if the key is not known.  An empty key returns all
        /// known properties in <c>[key]: [value]</c> format.
        /// </summary>
        private static string? GetSimulatedProp(string key)
        {
            var abis = Android.OS.Build.SupportedAbis?.ToArray() ?? new[] { "arm64-v8a" };
            var abis64 = abis.Where(Is64BitAbi).ToArray();
            var abis32 = abis.Where(a => !Is64BitAbi(a)).ToArray();

            return key switch
            {
                "ro.product.cpu.abi" => abis.FirstOrDefault() ?? "arm64-v8a",
                "ro.product.cpu.abilist" => string.Join(",", abis),
                "ro.product.cpu.abilist64" => string.Join(",", abis64),
                "ro.product.cpu.abilist32" => string.Join(",", abis32),
                "ro.product.model" => Android.OS.Build.Model ?? DeviceModel,
                "ro.product.name" => Android.OS.Build.Product ?? DeviceProduct,
                "ro.product.manufacturer" => Android.OS.Build.Manufacturer ?? "unknown",
                "ro.hardware" => Android.OS.Build.Hardware ?? "unknown",
                "ro.build.id" => Android.OS.Build.Id ?? "unknown",
                "ro.build.version.sdk" => ((int)Android.OS.Build.VERSION.SdkInt).ToString(),
                "ro.build.version.release" => Android.OS.Build.VERSION.Release ?? DefaultAndroidRelease,
                "" => BuildAllSimulatedProps(abis, abis64, abis32),
                _ => null,
            };
        }

        /// <summary>
        /// Returns <c>true</c> for well-known 64-bit ABI names as defined by the Android NDK.
        /// </summary>
        private static bool Is64BitAbi(string abi) =>
            Array.Exists(Abi64Prefixes, prefix => abi.StartsWith(prefix, StringComparison.Ordinal));

        private static string BuildAllSimulatedProps(
            string[] abis, string[] abis64, string[] abis32)
        {
            var props = new (string Key, string? Value)[]
            {
                ("ro.product.cpu.abi",       abis.FirstOrDefault()),
                ("ro.product.cpu.abilist",   string.Join(",", abis)),
                ("ro.product.cpu.abilist64", string.Join(",", abis64)),
                ("ro.product.cpu.abilist32", string.Join(",", abis32)),
                ("ro.product.model",         Android.OS.Build.Model),
                ("ro.product.name",          Android.OS.Build.Product),
                ("ro.product.manufacturer",  Android.OS.Build.Manufacturer),
                ("ro.hardware",              Android.OS.Build.Hardware),
                ("ro.build.id",              Android.OS.Build.Id),
                ("ro.build.version.sdk",     ((int)Android.OS.Build.VERSION.SdkInt).ToString()),
                ("ro.build.version.release", Android.OS.Build.VERSION.Release ?? DefaultAndroidRelease),
            };
            var sb = new StringBuilder();
            foreach (var (k, v) in props)
                if (v is not null)
                    sb.AppendLine($"[{k}]: [{v}]");
            return sb.ToString();
        }

        // ── Shell execution ────────────────────────────────────────────────────

        private static async Task RunShellAsync(Stream stream, string cmd)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "/system/bin/sh",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            if (!string.IsNullOrEmpty(cmd))
            {
                process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add(cmd.Replace("/data/local/tmp", $"/data/data/{SharedData.PackageID}/files"));
            }

            process.Start();

            // Forward stdin: socket → process (supports interactive shells)
            var stdinTask = Task.Run(async () =>
            {
                try
                { await stream.CopyToAsync(process.StandardInput.BaseStream); }
                catch { /* socket closed */ }
                finally { try { process.StandardInput.Close(); } catch { } }
            });

            // Forward stdout + stderr: process → socket (merged)
            var stdoutTask = CopyAndSuppressAsync(process.StandardOutput.BaseStream, stream);
            var stderrTask = CopyAndSuppressAsync(process.StandardError.BaseStream, stream);

            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
        }

        private static async Task CopyAndSuppressAsync(Stream src, Stream dst)
        {
            try
            { await src.CopyToAsync(dst); }
            catch { /* destination closed */ }
        }

        // ── File sync protocol ─────────────────────────────────────────────────

        private static async Task HandleSyncAsync(Stream stream)
        {
            var idBuf = new byte[4];
            var valBuf = new byte[4];

            while (true)
            {
                if (await ReadExactAsync(stream, idBuf, 4) < 4)
                    return;
                if (await ReadExactAsync(stream, valBuf, 4) < 4)
                    return;

                var id = Encoding.ASCII.GetString(idBuf);
                var value = BitConverter.ToUInt32(valBuf, 0);   // little-endian

                switch (id)
                {
                    case "STAT":
                        {
                            var path = await ReadUtf8Async(stream, (int)value);
                            if (path is null)
                                return;
                            path = path.Replace("/data/local/tmp", $"/data/data/{SharedData.PackageID}/files");
                            await HandleSyncStatAsync(stream, path);
                            break;
                        }
                    case "LIST":
                        {
                            var path = await ReadUtf8Async(stream, (int)value);
                            if (path is null)
                                return;
                            path = path.Replace("/data/local/tmp", $"/data/data/{SharedData.PackageID}/files");
                            await HandleSyncListAsync(stream, path);
                            break;
                        }
                    case "SEND":
                        {
                            var arg = await ReadUtf8Async(stream, (int)value);
                            if (arg is null)
                                return;
                            arg = arg.Replace("/data/local/tmp", $"/data/data/{SharedData.PackageID}/files");
                            await HandleSyncSendAsync(stream, arg);
                            break;
                        }
                    case "RECV":
                        {
                            var path = await ReadUtf8Async(stream, (int)value);
                            if (path is null)
                                return;
                            path = path.Replace("/data/local/tmp", $"/data/data/{SharedData.PackageID}/files");
                            await HandleSyncRecvAsync(stream, path);
                            break;
                        }
                    case "QUIT":
                        return;
                    default:
                        Debug.WriteLine($"[VirtualAdbServer] Unknown sync command: {id}");
                        return;
                }
            }
        }

        // STAT → "STAT" + mode(LE4) + size(LE4) + mtime(LE4)
        private static async Task HandleSyncStatAsync(Stream stream, string path)
        {
            uint mode = 0, size = 0, mtime = 0;
            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    mode = 0x81A4u;   // S_IFREG | 0644
                    size = (uint)Math.Min(fi.Length, uint.MaxValue);
                    mtime = ToUnixTime(fi.LastWriteTimeUtc);
                }
                else if (Directory.Exists(path))
                {
                    mode = 0x41EDu;   // S_IFDIR | 0755
                    mtime = ToUnixTime(new DirectoryInfo(path).LastWriteTimeUtc);
                }
            }
            catch { /* unreadable → return zeroes (not found) */ }

            await WriteSyncIdAsync(stream, "STAT");
            await stream.WriteAsync(BitConverter.GetBytes(mode));
            await stream.WriteAsync(BitConverter.GetBytes(size));
            await stream.WriteAsync(BitConverter.GetBytes(mtime));
        }

        // LIST → zero or more "DENT" entries, terminated by "DONE"
        private static async Task HandleSyncListAsync(Stream stream, string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                    {
                        try
                        {
                            uint entMode, entSize = 0, entMtime;
                            var nameBytes = Encoding.UTF8.GetBytes(Path.GetFileName(entry));

                            if (File.Exists(entry))
                            {
                                var fi = new FileInfo(entry);
                                entMode = 0x81A4u;
                                entSize = (uint)Math.Min(fi.Length, uint.MaxValue);
                                entMtime = ToUnixTime(fi.LastWriteTimeUtc);
                            }
                            else
                            {
                                entMode = 0x41EDu;
                                entMtime = ToUnixTime(new DirectoryInfo(entry).LastWriteTimeUtc);
                            }

                            await WriteSyncIdAsync(stream, "DENT");
                            await stream.WriteAsync(BitConverter.GetBytes(entMode));
                            await stream.WriteAsync(BitConverter.GetBytes(entSize));
                            await stream.WriteAsync(BitConverter.GetBytes(entMtime));
                            await stream.WriteAsync(BitConverter.GetBytes((uint)nameBytes.Length));
                            await stream.WriteAsync(nameBytes);
                        }
                        catch { /* skip unreadable entry */ }
                    }
                }
            }
            catch { /* can't enumerate – just send DONE */ }

            await WriteSyncIdAsync(stream, "DONE");
            await stream.WriteAsync(new byte[12]);  // mode=0, size=0, mtime=0
        }

        // SEND: receive DATA chunks from client, write to file → reply "OKAY"
        private static async Task HandleSyncSendAsync(Stream stream, string arg)
        {
            // arg is "path,mode_decimal"
            var comma = arg.LastIndexOf(',');
            var destPath = comma >= 0 ? arg[..comma] : arg;

            try
            {
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var file = File.Open(destPath, FileMode.Create, FileAccess.Write);
                uint mtime = 0;
                var chunkIdBuf = new byte[4];
                var chunkLenBuf = new byte[4];

                while (true)
                {
                    if (await ReadExactAsync(stream, chunkIdBuf, 4) < 4)
                        break;
                    if (await ReadExactAsync(stream, chunkLenBuf, 4) < 4)
                        break;

                    var chunkId = Encoding.ASCII.GetString(chunkIdBuf);
                    var chunkLen = BitConverter.ToUInt32(chunkLenBuf, 0);

                    if (chunkId == "DATA")
                    {
                        var data = await ReadBytesAsync(stream, (int)chunkLen);
                        if (data is null)
                            break;
                        await file.WriteAsync(data);
                    }
                    else if (chunkId == "DONE")
                    {
                        mtime = chunkLen;   // DONE's "length" field carries the mtime
                        break;
                    }
                    else
                        break;
                }

                file.Close();

                if (mtime > 0)
                {
                    try
                    {
                        File.SetLastWriteTimeUtc(destPath,
                            DateTimeOffset.FromUnixTimeSeconds(mtime).UtcDateTime);
                    }
                    catch { /* ignore mtime errors */ }
                }

                await WriteSyncIdAsync(stream, "OKAY");
                await stream.WriteAsync(new byte[4]);
            }
            catch (Exception ex)
            {
                var msgBytes = Encoding.UTF8.GetBytes(ex.Message);
                await WriteSyncIdAsync(stream, "FAIL");
                await stream.WriteAsync(BitConverter.GetBytes((uint)msgBytes.Length));
                await stream.WriteAsync(msgBytes);
            }
        }

        // RECV: send file to client as DATA chunks, end with "DONE"
        private static async Task HandleSyncRecvAsync(Stream stream, string path)
        {
            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"No such file: {path}");

                using var file = File.OpenRead(path);
                var buf = new byte[MaxSyncChunk];

                while (true)
                {
                    var n = await file.ReadAsync(buf);
                    if (n == 0)
                        break;
                    await WriteSyncIdAsync(stream, "DATA");
                    await stream.WriteAsync(BitConverter.GetBytes((uint)n));
                    await stream.WriteAsync(buf.AsMemory(0, n));
                }

                await WriteSyncIdAsync(stream, "DONE");
                await stream.WriteAsync(new byte[4]);
            }
            catch (Exception ex)
            {
                var msgBytes = Encoding.UTF8.GetBytes(ex.Message);
                await WriteSyncIdAsync(stream, "FAIL");
                await stream.WriteAsync(BitConverter.GetBytes((uint)msgBytes.Length));
                await stream.WriteAsync(msgBytes);
            }
        }

        // ── Protocol helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Reads a 4-hex-digit length prefix then that many ASCII bytes as a service name.
        /// Returns <c>null</c> on EOF or parse failure.
        /// </summary>
        private static async Task<string?> ReadServiceAsync(Stream stream)
        {
            var lenBuf = new byte[4];
            if (await ReadExactAsync(stream, lenBuf, 4) < 4)
                return null;
            if (!int.TryParse(Encoding.ASCII.GetString(lenBuf),
                    System.Globalization.NumberStyles.HexNumber, null, out var len))
                return null;
            if (len <= 0)
                return string.Empty;
            var data = await ReadBytesAsync(stream, len);
            return data is null ? null : Encoding.ASCII.GetString(data);
        }

        private static Task WriteOkayAsync(Stream s) =>
            s.WriteAsync("OKAY"u8.ToArray()).AsTask();

        private static async Task WriteOkayDataAsync(Stream stream, string data)
        {
            var dataBytes = Encoding.ASCII.GetBytes(data);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"OKAY{dataBytes.Length:x4}"));
            await stream.WriteAsync(dataBytes);
        }

        private static async Task WriteFailAsync(Stream stream, string message)
        {
            var msgBytes = Encoding.ASCII.GetBytes(message);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"FAIL{msgBytes.Length:x4}"));
            await stream.WriteAsync(msgBytes);
        }

        private static Task WriteSyncIdAsync(Stream s, string id) =>
            s.WriteAsync(Encoding.ASCII.GetBytes(id)).AsTask();

        private static async Task<string?> ReadUtf8Async(Stream stream, int byteCount)
        {
            var raw = await ReadBytesAsync(stream, byteCount);
            return raw is null ? null : Encoding.UTF8.GetString(raw);
        }

        private static async Task<byte[]?> ReadBytesAsync(Stream stream, int count)
        {
            if (count <= 0)
                return Array.Empty<byte>();
            var buf = new byte[count];
            return await ReadExactAsync(stream, buf, count) < count ? null : buf;
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                var n = await stream.ReadAsync(buffer, total, count - total);
                if (n == 0)
                    break;
                total += n;
            }
            return total;
        }

        private static uint ToUnixTime(DateTime utc) =>
            (uint)((DateTimeOffset)utc).ToUnixTimeSeconds();
    }
}
