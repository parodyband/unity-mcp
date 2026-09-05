#!/usr/bin/env node
// End-to-end acceptance test for the Open Unity MCP sidecar.
//
// Spawns the sidecar as a child process, talks NDJSON JSON-RPC to it over
// stdin/stdout, and proves that a tools/call issued while Unity is performing a
// real domain reload rides out the outage with no transport error surfaced.
//
// Requires a live Unity editor with the Open Unity MCP server running on --port.
// This forces an actual recompile, so the editor WILL domain-reload during the
// run; that is the whole point.
//
// Usage: node test/sidecar-e2e.mjs [--port <n>] [--project <path>]

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SIDECAR = path.join(__dirname, '..', 'open-unity-mcp-sidecar.js');

function parseArgs(argv) {
  const cfg = { port: parseInt(process.env.OPEN_UNITY_MCP_PORT || '', 10) || 8080, project: process.cwd() };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--port' && i + 1 < argv.length) cfg.port = parseInt(argv[++i], 10) || cfg.port;
    else if (argv[i] === '--project' && i + 1 < argv.length) cfg.project = argv[++i];
  }
  return cfg;
}

const CFG = parseArgs(process.argv.slice(2));

// ---------------------------------------------------------------------------
// Sidecar client
// ---------------------------------------------------------------------------

class SidecarClient {
  constructor(port, project) {
    this.pending = new Map();
    this.notifications = [];
    this.nextId = 1;
    this.child = spawn(process.execPath, [SIDECAR, '--port', String(port), '--project', project], {
      stdio: ['pipe', 'pipe', 'pipe']
    });

    this.rl = createInterface({ input: this.child.stdout });
    this.rl.on('line', (line) => this._onLine(line));

    // Surface sidecar stderr with a prefix so its diagnostics are visible.
    createInterface({ input: this.child.stderr }).on('line', (l) => {
      process.stderr.write('  sidecar> ' + l + '\n');
    });

    this.child.on('exit', (code) => {
      for (const [, p] of this.pending) p.reject(new Error('sidecar exited (code ' + code + ')'));
      this.pending.clear();
    });
  }

  _onLine(line) {
    const trimmed = line.trim();
    if (!trimmed) return;
    let msg;
    try {
      msg = JSON.parse(trimmed);
    } catch (err) {
      process.stderr.write('  !! non-JSON on sidecar stdout: ' + trimmed + '\n');
      return;
    }

    if (msg.id !== undefined && msg.id !== null && this.pending.has(msg.id)) {
      const p = this.pending.get(msg.id);
      this.pending.delete(msg.id);
      p.resolve(msg);
      return;
    }

    if (msg.method && (msg.id === undefined || msg.id === null)) {
      this.notifications.push(msg);
      return;
    }
  }

  // Sends a request and resolves with the JSON-RPC response object. Rejects only
  // on an overall timeout — a JSON-RPC error is a normal resolution here so the
  // caller can assert on it.
  request(method, params, timeoutMs = 150000) {
    const id = this.nextId++;
    const body = { jsonrpc: '2.0', id, method };
    if (params !== undefined) body.params = params;
    const started = Date.now();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('timeout after ' + (Date.now() - started) + 'ms waiting for ' + method));
      }, timeoutMs);
      this.pending.set(id, {
        resolve: (m) => { clearTimeout(timer); resolve({ message: m, ms: Date.now() - started }); },
        reject: (e) => { clearTimeout(timer); reject(e); }
      });
      this.child.stdin.write(JSON.stringify(body) + '\n');
    });
  }

  notify(method, params) {
    const body = { jsonrpc: '2.0', method };
    if (params !== undefined) body.params = params;
    this.child.stdin.write(JSON.stringify(body) + '\n');
  }

  close() {
    try { this.child.stdin.end(); } catch (e) {}
    try { this.child.kill(); } catch (e) {}
  }
}

// ---------------------------------------------------------------------------
// Assertions / helpers
// ---------------------------------------------------------------------------

const results = [];
function record(scenario, ok, ms, detail) {
  results.push({ scenario, ok, ms, detail });
  const tag = ok ? 'PASS' : 'FAIL';
  console.log(`  [${tag}] ${scenario} (${ms}ms) ${detail || ''}`);
}

function assert(cond, message) {
  if (!cond) throw new Error('assertion failed: ' + message);
}

function isResult(m) { return m && m.result !== undefined && m.error === undefined; }
function isError(m) { return m && m.error !== undefined; }
function isReloadEnvelope(m) {
  return !!(m && m.result && m.result._meta && m.result._meta['com.strangeape.open-unity-mcp/reloadInterrupted']);
}

function toolText(m) {
  try {
    const c = m.result && m.result.content;
    if (Array.isArray(c)) return c.map((x) => x && x.text ? x.text : '').join('\n');
  } catch (e) {}
  return '';
}

// Extracts assemblyLoadSequence from a sidecar tools/call get_compilation_status
// result whose content text is the JSON status blob. Returns null if the result
// was the reload envelope (no live status) or unparseable.
function liveSequenceFrom(m) {
  if (isReloadEnvelope(m)) return null;
  try {
    const parsed = JSON.parse(toolText(m));
    return typeof parsed.assemblyLoadSequence === 'number' ? parsed.assemblyLoadSequence : null;
  } catch (e) {
    return null;
  }
}

// Reads the editor's compilation status directly over HTTP (bypassing the
// sidecar) so we can observe the true assemblyLoadSequence. Waits for health
// first so this itself never fails across the reload.
async function liveStatus(port) {
  await waitHealthy(port, 90000);
  const body = JSON.stringify({ jsonrpc: '2.0', id: 999, method: 'tools/call', params: { name: 'unity.get_compilation_status', arguments: { includeConsole: false } } });
  try {
    const headers = { 'Content-Type': 'application/json' };
    try {
      const status = JSON.parse(fs.readFileSync(path.join(CFG.project, 'Temp', 'OpenUnityMcp', 'server-status.json'), 'utf8'));
      if (status.token) headers.Authorization = 'Bearer ' + status.token;
    } catch {}
    const r = await fetch('http://127.0.0.1:' + port + '/mcp', { method: 'POST', headers, body });
    const j = await r.json();
    return toolTextFromRaw(j);
  } catch (e) {
    return null;
  }
}

function toolTextFromRaw(j) {
  try {
    const c = j.result && j.result.content;
    if (Array.isArray(c)) return JSON.parse(c.map((x) => x && x.text ? x.text : '').join('\n'));
  } catch (e) {}
  return null;
}

function compilationSequence(statusObj) {
  return statusObj && typeof statusObj.assemblyLoadSequence === 'number' ? statusObj.assemblyLoadSequence : -1;
}

async function health(port) {
  try {
    const r = await fetch('http://127.0.0.1:' + port + '/health');
    return r.ok;
  } catch (e) {
    return false;
  }
}

async function waitHealthy(port, deadlineMs) {
  const deadline = Date.now() + deadlineMs;
  while (Date.now() < deadline) {
    if (await health(port)) return true;
    await new Promise((r) => setTimeout(r, 300));
  }
  return false;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function main() {
  console.log('Open Unity MCP sidecar e2e');
  console.log('  sidecar : ' + SIDECAR);
  console.log('  port    : ' + CFG.port);
  console.log('  project : ' + CFG.project);
  console.log('');

  if (!(await health(CFG.port))) {
    console.error('Editor health check failed on port ' + CFG.port + '. Start the Unity MCP server first.');
    process.exit(2);
  }

  const client = new SidecarClient(CFG.port, CFG.project);
  const unique = process.pid + '_' + Date.now();
  const dummyAsset = 'Assets/OpenUnityMcpSidecarE2E_' + unique + '.cs';
  let dummyCreated = false;

  try {
    // --- Phase 1: healthy round-trips -------------------------------------
    console.log('Phase 1 - requests while healthy');

    const init = await client.request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'sidecar-e2e', version: '1.0.0' } });
    assert(isResult(init.message), 'initialize returned a result');
    const listChanged = init.message.result?.capabilities?.tools?.listChanged;
    record('initialize (healthy)', isResult(init.message), init.ms, 'listChanged=' + listChanged);
    assert(listChanged === true, 'sidecar rewrote capabilities.tools.listChanged to true');

    client.notify('notifications/initialized');

    const tools = await client.request('tools/list', {});
    const toolCount = tools.message.result?.tools?.length ?? 0;
    assert(isResult(tools.message), 'tools/list returned a result');
    record('tools/list (healthy)', isResult(tools.message), tools.ms, toolCount + ' tools');

    const info = await client.request('tools/call', { name: 'unity.get_project_info', arguments: {} });
    assert(isResult(info.message), 'unity.get_project_info returned a result');
    assert(!info.message.result?.isError, 'unity.get_project_info result is not an error');
    record('tools/call get_project_info (healthy)', isResult(info.message) && !info.message.result?.isError, info.ms,
      'text ' + toolText(info.message).length + ' chars');

    const saved = await client.request('tools/call', { name: 'unity.run_code', arguments: {
      code: 'state.marker=42; state.epoch=(await unity.scene.query({limit:1})).editorEpoch; emit(state.epoch);'
    } });
    assert(isResult(saved.message) && !saved.message.result.isError, 'persistent session initialized');
    const sessionId = saved.message.result.structuredContent.sessionId;

    // --- Phase 2: request across a real domain reload ---------------------
    console.log('');
    console.log('Phase 2 - request during a domain reload');

    // Touch a trivial script so the compilation is guaranteed to do real work
    // and drive a domain reload. write_asset_text of a .cs is itself a
    // reload-triggering op, so this doubles as the trigger.
    const stamp = Date.now();
    const dummy = 'namespace OpenUnityMcpSidecarE2E { internal static class Dummy_' + unique + ' { public const long Stamp = ' + stamp + 'L; } }\n';
    const write = await client.request('tools/call', {
      name: 'unity.write_asset_text',
      arguments: { path: dummyAsset, text: dummy, createDirectories: true, refresh: true }
    });
    dummyCreated = true;
    // write_asset_text may itself be interrupted by the reload it triggers; either
    // a clean result or the verify-and-retry envelope is acceptable here.
    const writeReload = !!(write.message.result?._meta && write.message.result._meta['com.strangeape.open-unity-mcp/reloadInterrupted']);
    record('tools/call write_asset_text (trigger)', isResult(write.message), write.ms,
      writeReload ? 'reload-interrupted envelope' : 'applied cleanly');

    // Record the assembly load sequence before we force a reload so we can prove
    // a real domain reload actually occurred during the burst below.
    const seqBefore = compilationSequence(await liveStatus(CFG.port));

    // Explicitly request compilation to be certain a reload is in flight.
    const compileReq = await client.request('tools/call', { name: 'unity.request_script_compilation', arguments: {} });
    const compileReload = isReloadEnvelope(compileReq.message);
    record('tools/call request_script_compilation (trigger)', isResult(compileReq.message), compileReq.ms,
      compileReload ? 'reload-interrupted envelope' : 'accepted');

    // IMMEDIATELY issue a BURST of follow-up calls that spans the reload window.
    // A fast project reloads in ~2s, so a single call can slip in before the
    // domain actually unloads; a burst guarantees at least one call lands while
    // the server is down. EVERY response must be a successful JSON-RPC result with
    // NO transport error surfaced — that is the proof the sidecar rode out the
    // outage. We also confirm the reload really happened (sequence incremented).
    const burst = [];
    const burstDeadline = Date.now() + 60000;
    let rodeOutEnvelope = 0;
    let maxMs = 0;
    let anyError = false;
    while (Date.now() < burstDeadline) {
      const s = await client.request('tools/call', { name: 'unity.get_compilation_status', arguments: { includeConsole: false } });
      const ok = isResult(s.message) && !isError(s.message);
      if (!ok) anyError = true;
      if (isReloadEnvelope(s.message)) rodeOutEnvelope++;
      maxMs = Math.max(maxMs, s.ms);
      burst.push(s);
      // Stop once we can see the reload has completed (sequence advanced) AND we
      // have a live post-reload status back, so the burst brackets the outage.
      const live = liveSequenceFrom(s.message);
      if (live !== null && live > seqBefore) break;
      await new Promise(resolve => setTimeout(resolve, 50));
    }

    const seqAfter = compilationSequence(await liveStatus(CFG.port));
    const reloadHappened = seqAfter > seqBefore;
    assert(!anyError, 'every burst response was a JSON-RPC result (no transport error surfaced)');
    assert(reloadHappened, 'a real domain reload occurred during the burst (assemblyLoadSequence advanced from ' + seqBefore + ' to ' + seqAfter + ')');
    record('burst across reload: no transport errors', !anyError, maxMs,
      burst.length + ' calls, seq ' + seqBefore + '->' + seqAfter + ', envelopes=' + rodeOutEnvelope + ', slowest=' + maxMs + 'ms');

    // --- Phase 3: confirm recovery ----------------------------------------
    console.log('');
    console.log('Phase 3 - after recovery');

    // The editor should be healthy again; a fresh call must return a live result.
    assert(await waitHealthy(CFG.port, 90000), 'editor healthy after reload');
    const after = await client.request('tools/call', { name: 'unity.get_compilation_status', arguments: { includeConsole: false } });
    assert(isResult(after.message) && !isError(after.message), 'post-recovery status is a live result');
    record('tools/call get_compilation_status (recovered)', isResult(after.message), after.ms,
      'notifications seen=' + client.notifications.filter((n) => n.method === 'notifications/tools/list_changed').length);
    const restored = await client.request('tools/call', { name: 'unity.run_code', arguments: {
      code: 'emit({marker:state.marker,epochChanged:state.epoch!==(await unity.scene.query({limit:1})).editorEpoch});'
    } });
    const restoredPayload = restored.message.result?.structuredContent;
    const stateSurvived = restoredPayload?.sessionId === sessionId && restoredPayload?.output?.[0]?.marker === 42 && restoredPayload?.output?.[0]?.epochChanged === true;
    assert(stateSurvived, 'same session retained variables while Unity epoch changed');
    record('persistent state survives real reload', stateSurvived, restored.ms, 'marker=42, Unity epoch changed, session ID unchanged');
  } finally {
    // Clean up the dummy asset regardless of outcome.
    if (dummyCreated) {
      try {
        if (await waitHealthy(CFG.port, 30000)) {
          await client.request('tools/call', { name: 'unity.delete_asset', arguments: { path: dummyAsset } }, 60000);
          console.log('');
          console.log('  cleaned up ' + dummyAsset);
        }
      } catch (e) {
        console.error('  cleanup failed for ' + dummyAsset + ': ' + e.message);
      }
    }
    client.close();
  }

  // --- Behavior matrix ---------------------------------------------------
  console.log('');
  console.log('Behavior matrix');
  console.log('  ' + 'scenario'.padEnd(48) + 'result   ms');
  console.log('  ' + '-'.repeat(66));
  for (const r of results) {
    console.log('  ' + r.scenario.padEnd(48) + (r.ok ? 'PASS' : 'FAIL').padEnd(9) + String(r.ms).padStart(6));
  }

  const failed = results.filter((r) => !r.ok);
  console.log('');
  console.log(failed.length === 0 ? 'ALL PASS' : (failed.length + ' FAILED'));
  process.exit(failed.length === 0 ? 0 : 1);
}

main().catch((err) => {
  console.error('');
  console.error('E2E ERROR: ' + (err && err.stack ? err.stack : err));
  process.exit(1);
});
