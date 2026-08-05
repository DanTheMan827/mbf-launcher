import { Adb, AdbServerClient } from "@yume-chan/adb";
import { MbfAdbServerConnector } from "./adb-server-connector";

// ── UI helpers ───────────────────────────────────────────────────────────────

function log(msg: string, cls: "info" | "pass" | "fail" | "section" = "info"): void {
    const container = document.getElementById("log")!;
    const line = document.createElement("div");
    line.className = cls;
    line.textContent = msg;
    container.appendChild(line);
    container.scrollTop = container.scrollHeight;
    console.log(`[${cls.toUpperCase()}] ${msg}`);
}

const pass    = (msg: string) => log(`✓ ${msg}`, "pass");
const fail    = (msg: string) => log(`✗ ${msg}`, "fail");
const section = (msg: string) => log(`── ${msg} ──`, "section");

function assert(condition: boolean, label: string): void {
    if (condition) { pass(label); }
    else           { throw new Error(`Assertion failed: ${label}`); }
}

// ── Test constants ────────────────────────────────────────────────────────────

const TEST_FILE    = `/data/local/tmp/mbf_virtual_test_${Date.now()}.txt`;
const TEST_CONTENT = "Hello from MBF Virtual Device! 🎮\n";

// ── Test suite ────────────────────────────────────────────────────────────────

async function runAllTests(): Promise<void> {
    // ── Bridge ──────────────────────────────────────────────────────────────
    section("Bridge availability");
    assert(typeof window.__mbfBridge !== "undefined", "window.__mbfBridge is available");

    // ── Server version ───────────────────────────────────────────────────────
    section("ADB server version");
    const client = new AdbServerClient(new MbfAdbServerConnector());
    const version = await client.getVersion();
    assert(version === 41, `Server reports version 41 (got ${version})`);

    // ── Device listing ───────────────────────────────────────────────────────
    section("Device listing (host:devices-l)");
    const devices = await client.getDevices();
    assert(devices.length === 1, `Exactly one device listed (got ${devices.length})`);

    const dev = devices[0];
    assert(dev.serial  === "mbf-virtual",      `Serial is "mbf-virtual" (got "${dev.serial}")`);
    assert(dev.state   === "device",           `State is "device" (got "${dev.state}")`);
    assert(dev.model   === "MBF_Virtual_Device", `Model is "MBF_Virtual_Device" (got "${dev.model}")`);
    assert(dev.transportId === BigInt(1),      `Transport ID is 1 (got ${dev.transportId})`);
    log(`  product=${dev.product}  model=${dev.model}  device=${dev.device}`, "info");

    // ── Transport + banner ───────────────────────────────────────────────────
    section("Transport creation and banner");
    const transport = await client.createTransport({ serial: "mbf-virtual" });
    assert(transport.serial === "mbf-virtual",         "Transport serial is \"mbf-virtual\"");
    assert(transport.banner.model === "MBF_Virtual_Device", "Banner model is \"MBF_Virtual_Device\"");
    log(`  Banner features: ${transport.banner.features.join(", ")}`, "info");

    const adb = new Adb(transport);

    // ── Shell commands (exec:) ───────────────────────────────────────────────
    section("Shell commands (exec:)");
    const echoOut = await adb.subprocess.noneProtocol.spawnWaitText(["echo", "hello_mbf"]);
    assert(echoOut.trim() === "hello_mbf", `echo output is "hello_mbf" (got "${echoOut.trim()}")`);

    const lsOut = await adb.subprocess.noneProtocol.spawnWaitText(["ls", "/system/bin/sh"]);
    assert(lsOut.includes("/system/bin/sh"), "ls can see /system/bin/sh");

    // ── getprop (real or simulated fallback) ─────────────────────────────────
    section("getprop (real binary or simulated fallback)");

    const abi = await adb.getProp("ro.product.cpu.abi");
    assert(abi.length > 0, `ro.product.cpu.abi is non-empty (got "${abi}")`);
    log(`  ro.product.cpu.abi = ${abi}`, "info");

    const release = await adb.getProp("ro.build.version.release");
    assert(release.length > 0, `ro.build.version.release is non-empty (got "${release}")`);
    log(`  ro.build.version.release = ${release}`, "info");

    const sdkStr = await adb.getProp("ro.build.version.sdk");
    assert(/^\d+$/.test(sdkStr), `ro.build.version.sdk is numeric (got "${sdkStr}")`);
    log(`  ro.build.version.sdk = ${sdkStr}`, "info");

    // ── Interactive shell (shell:) ────────────────────────────────────────────
    section("Interactive shell (shell:)");
    const ptyProc = await adb.subprocess.noneProtocol.pty();
    const encoder = new TextEncoder();
    const decoder = new TextDecoder();

    const ptyReader = ptyProc.output.getReader();
    const ptyWriter = ptyProc.stdin.getWriter();

    await ptyWriter.write(encoder.encode("echo hello_interactive\n"));

    let ptyOut = "";
    const ptyTimeout = new Promise<never>((_, rej) =>
        setTimeout(() => rej(new Error("Interactive shell timed out after 5 s")), 5000));

    while (!ptyOut.includes("hello_interactive")) {
        const { done, value } = await Promise.race([ptyReader.read(), ptyTimeout]);
        if (done) break;
        ptyOut += decoder.decode(value, { stream: true });
    }

    assert(ptyOut.includes("hello_interactive"),
        "Interactive shell echoed \"hello_interactive\"");

    await ptyWriter.close().catch(() => { /* ignore */ });
    await ptyReader.cancel().catch(() => { /* ignore */ });
    await ptyProc.kill().catch(() => { /* ignore */ });

    // ── File upload (sync SEND) ───────────────────────────────────────────────
    section("File upload (sync SEND)");
    const contentBytes = encoder.encode(TEST_CONTENT);
    const syncUp = await adb.sync();
    await syncUp.write({
        filename:   TEST_FILE,
        file: new ReadableStream<Uint8Array>({
            start(c) { c.enqueue(contentBytes); c.close(); },
        }),
        permission: 0o644,
        mtime:      Math.floor(Date.now() / 1000),
    });
    await syncUp.dispose();
    pass(`Uploaded ${contentBytes.length} B → ${TEST_FILE}`);

    // ── File download (sync RECV) ─────────────────────────────────────────────
    section("File download (sync RECV)");
    const syncDown  = await adb.sync();
    const chunks: Uint8Array[] = [];
    const dlReader  = syncDown.read(TEST_FILE).getReader();
    let   dlResult: ReadableStreamReadResult<Uint8Array>;
    while (!(dlResult = await dlReader.read()).done) {
        chunks.push(dlResult.value);
    }
    await syncDown.dispose();

    const totalLen   = chunks.reduce((s, c) => s + c.length, 0);
    const downloaded = new Uint8Array(totalLen);
    let   off = 0;
    for (const c of chunks) { downloaded.set(c, off); off += c.length; }
    const downloadedText = decoder.decode(downloaded);

    assert(downloadedText === TEST_CONTENT,
        `Downloaded content matches uploaded content (${totalLen} B)`);

    // ── Cleanup ───────────────────────────────────────────────────────────────
    section("Cleanup");
    await adb.subprocess.noneProtocol.spawnWaitText(["rm", "-f", TEST_FILE]);
    pass(`Removed ${TEST_FILE}`);

    await adb.close();
    pass("ADB connection closed cleanly");
}

// ── Entry point ───────────────────────────────────────────────────────────────

document.addEventListener("DOMContentLoaded", () => {
    const btn = document.getElementById("run") as HTMLButtonElement;

    btn.addEventListener("click", async () => {
        btn.disabled = true;
        document.getElementById("log")!.innerHTML = "";

        const t0 = Date.now();
        try {
            await runAllTests();
            section("All tests passed 🎉");
            log(`Completed in ${Date.now() - t0} ms`, "pass");
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : String(err);
            fail(`FAILED: ${msg}`);
            console.error(err);
        } finally {
            btn.disabled = false;
        }
    });
});
