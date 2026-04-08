"use strict";
/**
 * bridge_test.ts
 *
 * Test page loaded by the MBF Launcher WebView to verify the bridge API.
 * Load this page by setting AppConfig.AppUrl to:
 *   file:///android_asset/bridge_test.html
 *
 * Tests covered:
 *   1. Bridge availability
 *   2. Single connection – ADB host:version query
 *   3. Multiple independent connections (unique IDs)
 *   4. Flow control / back-pressure (deliberate slow onData callback)
 *   5. close() idempotency
 */
// ---------------------------------------------------------------------------
// ADB protocol helpers
// ---------------------------------------------------------------------------
/** Encodes an ADB host-service query in the wire format: 4-hex-digit-length + service. */
function encodeAdbQuery(service) {
    const hex = service.length.toString(16).padStart(4, "0");
    return new TextEncoder().encode(hex + service);
}
/** Decodes received bytes as UTF-8 text (best-effort for display). */
function decodeText(data) {
    return new TextDecoder().decode(data);
}
function log(msg, level = "info") {
    const container = document.getElementById("log");
    const el = document.createElement("div");
    el.className = `log-entry log-${level}`;
    const ts = new Date().toISOString().slice(11, 23);
    el.textContent = `[${ts}] ${msg}`;
    container.appendChild(el);
    container.scrollTop = container.scrollHeight;
}
function clearLog() {
    document.getElementById("log").innerHTML = "";
}
// ---------------------------------------------------------------------------
// Individual tests
// ---------------------------------------------------------------------------
async function testBridgeAvailability() {
    log("── Test 1: bridge availability ──", "heading");
    if (!window.__mbfBridge) {
        log("FAIL: window.__mbfBridge is not defined. Is bridge.js loaded?", "fail");
        return false;
    }
    log(`PASS: window.__mbfBridge.isAvailable = ${window.__mbfBridge.isAvailable}`, "pass");
    log(`INFO: isAdbAvailable = ${window.__mbfBridge.isAdbAvailable}`);
    return true;
}
async function testVersionQuery() {
    log("── Test 2: host:version query ──", "heading");
    const bridge = window.__mbfBridge;
    let conn;
    try {
        conn = await bridge.connect();
        log(`PASS: connected, id = ${conn.id}`, "pass");
        // Accumulate all incoming chunks into a single response.
        const chunks = [];
        let resolve;
        const firstChunk = new Promise((r) => { resolve = r; });
        conn.onData((chunk) => {
            chunks.push(chunk);
            log(`DATA: ${decodeText(chunk).trim()}`, "data");
            resolve(); // signal that we got at least one chunk
        });
        conn.onClose(() => log("INFO: connection closed by server"));
        const ok = await conn.write(encodeAdbQuery("host:version"));
        if (!ok) {
            log("FAIL: write returned false", "fail");
            return;
        }
        log("PASS: write returned true", "pass");
        await Promise.race([
            firstChunk,
            new Promise((_, reject) => setTimeout(() => reject(new Error("timeout waiting for response")), 5000)),
        ]);
        const full = decodeText(mergeChunks(chunks));
        if (full.startsWith("OKAY")) {
            log(`PASS: received valid OKAY response: ${full.slice(0, 16)}`, "pass");
        }
        else {
            log(`WARN: unexpected response: ${full.slice(0, 32)}`);
        }
    }
    catch (e) {
        log(`FAIL: ${e.message}`, "fail");
    }
    finally {
        await (conn === null || conn === void 0 ? void 0 : conn.close());
    }
}
async function testMultipleConnections() {
    log("── Test 3: multiple independent connections ──", "heading");
    const bridge = window.__mbfBridge;
    let c1;
    let c2;
    try {
        [c1, c2] = await Promise.all([bridge.connect(), bridge.connect()]);
        log(`PASS: c1.id = ${c1.id}`, "pass");
        log(`PASS: c2.id = ${c2.id}`, "pass");
        if (c1.id === c2.id) {
            log("FAIL: connection IDs are not unique!", "fail");
        }
        else {
            log("PASS: IDs are unique", "pass");
        }
        // Both connections should accept writes independently.
        const [ok1, ok2] = await Promise.all([
            c1.write(encodeAdbQuery("host:version")),
            c2.write(encodeAdbQuery("host:version")),
        ]);
        log(`PASS: c1.write = ${ok1}, c2.write = ${ok2}`, "pass");
    }
    catch (e) {
        log(`FAIL: ${e.message}`, "fail");
    }
    finally {
        await Promise.all([c1 === null || c1 === void 0 ? void 0 : c1.close(), c2 === null || c2 === void 0 ? void 0 : c2.close()]);
        log("PASS: both connections closed", "pass");
    }
}
async function testFlowControl() {
    log("── Test 4: flow control / back-pressure ──", "heading");
    const bridge = window.__mbfBridge;
    let conn;
    try {
        conn = await bridge.connect();
        let received = 0;
        const slowDelay = 100; // ms – deliberately slow to exercise back-pressure
        conn.onData(async (chunk) => {
            received++;
            await sleep(slowDelay);
            log(`DATA [${received}]: ${chunk.length} B`, "data");
        });
        // Send the query and wait long enough to receive at least one chunk.
        await conn.write(encodeAdbQuery("host:version"));
        await sleep(2000);
        if (received > 0) {
            log(`PASS: received ${received} chunk(s) with ${slowDelay} ms/chunk delay`, "pass");
        }
        else {
            log("FAIL: no data received within 2 s", "fail");
        }
    }
    catch (e) {
        log(`FAIL: ${e.message}`, "fail");
    }
    finally {
        await (conn === null || conn === void 0 ? void 0 : conn.close());
    }
}
async function testCloseIdempotency() {
    log("── Test 5: close() idempotency ──", "heading");
    const bridge = window.__mbfBridge;
    let conn;
    try {
        conn = await bridge.connect();
        let closeFired = 0;
        conn.onClose(() => { closeFired++; });
        await conn.close();
        await conn.close(); // must be a no-op
        if (conn.closed) {
            log("PASS: conn.closed = true", "pass");
        }
        else {
            log("FAIL: conn.closed should be true after close()", "fail");
        }
        if (closeFired === 1) {
            log("PASS: onClose fired exactly once", "pass");
        }
        else {
            log(`FAIL: onClose fired ${closeFired} times (expected 1)`, "fail");
        }
    }
    catch (e) {
        log(`FAIL: ${e.message}`, "fail");
    }
}
// ---------------------------------------------------------------------------
// Test runner
// ---------------------------------------------------------------------------
async function runTests() {
    const btn = document.getElementById("run-btn");
    btn.disabled = true;
    clearLog();
    log("═══ MBF Bridge Test Suite ═══", "heading");
    const bridgeOk = await testBridgeAvailability();
    if (!bridgeOk) {
        btn.disabled = false;
        return;
    }
    if (!window.__mbfBridge.isAdbAvailable) {
        log("SKIP: isAdbAvailable is false – skipping TCP tests");
        btn.disabled = false;
        return;
    }
    await testVersionQuery();
    await testMultipleConnections();
    await testFlowControl();
    await testCloseIdempotency();
    log("═══ Done ═══", "heading");
    btn.disabled = false;
}
// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------
function sleep(ms) {
    return new Promise((r) => setTimeout(r, ms));
}
function mergeChunks(chunks) {
    const total = chunks.reduce((n, c) => n + c.length, 0);
    const out = new Uint8Array(total);
    let offset = 0;
    for (const c of chunks) {
        out.set(c, offset);
        offset += c.length;
    }
    return out;
}
// ---------------------------------------------------------------------------
// Page init
// ---------------------------------------------------------------------------
document.addEventListener("DOMContentLoaded", () => {
    var _a;
    const statusEl = document.getElementById("bridge-status");
    if ((_a = window.__mbfBridge) === null || _a === void 0 ? void 0 : _a.isAvailable) {
        statusEl.textContent = `✓ Bridge available  |  ADB: ${window.__mbfBridge.isAdbAvailable ? "yes" : "no"}`;
        statusEl.className = "status ok";
    }
    else {
        statusEl.textContent = "✗ Bridge not available (not running inside MBF Launcher?)";
        statusEl.className = "status error";
    }
    document.getElementById("run-btn").addEventListener("click", () => void runTests());
});
