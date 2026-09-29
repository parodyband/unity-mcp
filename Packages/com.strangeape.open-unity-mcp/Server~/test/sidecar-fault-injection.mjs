#!/usr/bin/env node
// Hermetic fault-injection test for the Open Unity MCP sidecar.
//
// This does NOT need a Unity editor. It stands up a controllable mock of the
// in-editor HTTP server on a loopback port, points the sidecar at it, and drives
// exact outage scenarios so the sidecar's recovery state machine is proven
// deterministically (the live e2e can only prove it when reload timing happens to
// overlap the request window; this proves it every run).
//
// Scenarios:
//   1. Healthy request -> normal result.
//   2. Connection refused, then server returns -> request retried transparently,
//      client sees a normal result, no error.
//   3. Mid-flight reset on tools/call, then healthy -> SUCCESS result carrying the
//      reloadInterrupted envelope (NOT resent, NOT an error).
//   4. Mid-flight reset on an idempotent method (tools/list) -> retried
//      transparently, normal result.
//   5. Server never returns (status file = stopped) -> JSON-RPC error (editor gone).
//   6. initialize capabilities.tools.listChanged rewritten to true, and a
//      notifications/tools/list_changed is emitted after a recovery.
//   7. Access token: the sidecar reads the token from the status file and attaches
//      it (Authorization: Bearer + X-Open-Unity-Mcp-Token) so the mock, which now
//      REQUIRES the token on /mcp, accepts every request.
//   8. Token rotation: the mock rotates its token and rejects the stale one with a
//      401; the sidecar silently re-reads the status file and resends once, so the
//      client sees a normal result (no 401 surfaced).
//   9. Client notifications are handled in the sidecar, never forwarded.
//  10. ping is answered immediately while a tools/call waits out an outage.
//  11. A request cancelled while queued never reaches Unity and gets no response.
//  12. A request with a progressToken gets heartbeats until its response.
//  13. A stale 'stopped' status from another project does not abort a reload wait.
//  14. The sidecar follows the project /health reports (status file and token).
//  15. Unity not running at startup: initialize and tools/list are answered at
//      once, and list_changed is emitted when Unity appears.
//  16. A cold start serves the catalog cached from the live editor.
//
// Usage: node test/sidecar-fault-injection.mjs

import http from 'node:http';
import net from 'node:net';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SIDECAR = path.join(__dirname, '..', 'open-unity-mcp-sidecar.js');

// ---------------------------------------------------------------------------
// Controllable mock of the in-editor server
// ---------------------------------------------------------------------------

// mode drives how the next /mcp POST behaves:
//   'up'         -> normal JSON-RPC response
//   'refuse'     -> the listener is stopped entirely (connect fails: ECONNREFUSED)
//   'midflight'  -> read the request fully, then destroy the socket (reset after send)
class MockUnity {
  constructor() {
    this.mode = 'up';
    this.port = 0;
    this.server = null;
    this.mcpHits = 0;
    this.healthHits = 0;
    // Access-token enforcement (mirrors the real in-editor server). When
    // requireToken is on, /mcp POSTs without the matching token get a 401.
    this.token = null;
    this.requireToken = false;
    this.lastAuthHeader = null;
    this.lastXTokenHeader = null;
    this.unauthorizedHits = 0;
    // Reported by /health like the real editor (null mimics editors before 0.17.0).
    this.projectPath = null;
    // Tool names of every tools/call that reached the mock.
    this.calledTools = [];
    // Delay applied by 'slowOnce' before answering the next /mcp POST.
    this.slowMs = 1000;
  }

  async start() {
    this.server = http.createServer((req, res) => this._onRequest(req, res));
    await new Promise((resolve) => this.server.listen(0, '127.0.0.1', resolve));
    this.port = this.server.address().port;
  }

  _onRequest(req, res) {
    if (req.url === '/health') {
      this.healthHits++;
      // Health answers whenever the listener is up. In 'refuse' mode the listener
      // is stopped, so this handler is unreachable — which is the point.
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(this.projectPath ? { ok: true, projectPath: this.projectPath } : { ok: true }));
      return;
    }

    if (req.url !== '/mcp') {
      res.writeHead(404); res.end('nope'); return;
    }

    // Capture the auth headers the sidecar sent (used by the token scenarios).
    this.lastAuthHeader = req.headers['authorization'] || null;
    this.lastXTokenHeader = req.headers['x-open-unity-mcp-token'] || null;

    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => {
      this.mcpHits++;
      // 'midflightOnce' resets the socket on the NEXT /mcp POST only, then heals
      // to 'up' — a deterministic single mid-flight reset with no manual timing.
      if (this.mode === 'midflightOnce') {
        this.mode = 'up';
        req.socket.destroy();
        return;
      }
      if (this.mode === 'midflight') {
        // Request fully received (tool "may have run"), now kill the socket with
        // no response — a mid-flight reset on every POST until told otherwise.
        req.socket.destroy();
        return;
      }

      // Token gate (mirrors the real server: reject BEFORE running any tool).
      if (this.requireToken) {
        const bearer = this.lastAuthHeader && this.lastAuthHeader.startsWith('Bearer ')
          ? this.lastAuthHeader.slice('Bearer '.length)
          : null;
        const presented = bearer || this.lastXTokenHeader || null;
        if (presented !== this.token) {
          this.unauthorizedHits++;
          res.writeHead(401, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32001, message: 'Missing or invalid access token.' } }));
          return;
        }
      }

      let msg;
      try { msg = JSON.parse(body); } catch (e) { msg = {}; }
      const hasId = msg.id !== undefined && msg.id !== null;
      if (!hasId) {
        // notification: 202, no body (mirrors the real server)
        res.writeHead(202); res.end(); return;
      }

      if (msg.method === 'tools/call') this.calledTools.push(msg.params && msg.params.name);
      const result = this._resultFor(msg);
      const answer = () => {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result }));
      };
      if (this.mode === 'slowOnce') {
        this.mode = 'up';
        setTimeout(answer, this.slowMs);
      } else {
        answer();
      }
    });
  }

  _resultFor(msg) {
    if (msg.method === 'initialize') {
      return {
        protocolVersion: '2025-06-18',
        capabilities: { tools: { listChanged: false } },
        serverInfo: { name: 'mock-unity', version: '0.0.0' }
      };
    }
    if (msg.method === 'tools/list') {
      return { tools: [{ name: 'unity.get_project_info' }] };
    }
    if (msg.method === 'tools/call') {
      return { content: [{ type: 'text', text: 'ok:' + (msg.params && msg.params.name) }], isError: false };
    }
    return {};
  }

  // Simulate the listener disappearing (domain reload): close the server so new
  // connections are refused. Existing keep-alive sockets are destroyed too.
  async goDown() {
    this.mode = 'refuse';
    await new Promise((resolve) => this.server.close(resolve));
    // Also drop any lingering sockets.
    this.server.closeAllConnections?.();
  }

  // Bring the listener back on the SAME port (like the editor rebinding).
  async comeUp() {
    this.server = http.createServer((req, res) => this._onRequest(req, res));
    await new Promise((resolve, reject) => {
      this.server.once('error', reject);
      this.server.listen(this.port, '127.0.0.1', resolve);
    });
    this.mode = 'up';
  }

  setMode(mode) { this.mode = mode; }

  async stop() {
    try { this.server && await new Promise((r) => this.server.close(r)); } catch (e) {}
  }
}

// ---------------------------------------------------------------------------
// Sidecar driver
// ---------------------------------------------------------------------------

class Sidecar {
  constructor(port, project, timeoutMs, stateDir = path.join(project, 'state')) {
    this.pending = new Map();
    this.notifications = [];
    this.responses = [];
    this.nextId = 1;
    const args = [SIDECAR, '--port', String(port), '--project', project, '--timeout', String(timeoutMs)];
    // Keep the catalog cache out of the real home directory, and speed up the
    // progress heartbeat so its scenario runs in well under a second.
    this.child = spawn(process.execPath, args, {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, OPEN_UNITY_MCP_STATE_DIR: stateDir, OPEN_UNITY_MCP_PROGRESS_INTERVAL_MS: '200' }
    });
    createInterface({ input: this.child.stdout }).on('line', (l) => this._onLine(l));
    createInterface({ input: this.child.stderr }).on('line', (l) => process.stderr.write('  sidecar> ' + l + '\n'));
  }

  _onLine(line) {
    const t = line.trim();
    if (!t) return;
    let m;
    try { m = JSON.parse(t); } catch (e) { process.stderr.write('  !! bad stdout: ' + t + '\n'); return; }
    if (m.id !== undefined && m.id !== null) this.responses.push(m);
    if (m.id !== undefined && m.id !== null && this.pending.has(m.id)) {
      const p = this.pending.get(m.id); this.pending.delete(m.id); p.resolve({ message: m, ms: Date.now() - p.started });
      return;
    }
    if (m.method && (m.id === undefined || m.id === null)) this.notifications.push(m);
  }

  // Sends a request; returns its id and a promise for the response.
  send(method, params, timeoutMs = 30000) {
    const id = this.nextId++;
    const body = { jsonrpc: '2.0', id, method };
    if (params !== undefined) body.params = params;
    const started = Date.now();
    const response = new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error('client timeout ' + method)); }, timeoutMs);
      this.pending.set(id, { started, resolve: (v) => { clearTimeout(timer); resolve(v); }, reject: (e) => { clearTimeout(timer); reject(e); } });
      this.child.stdin.write(JSON.stringify(body) + '\n');
    });
    return { id, response };
  }

  request(method, params, timeoutMs = 30000) {
    return this.send(method, params, timeoutMs).response;
  }

  notify(method, params) {
    const body = { jsonrpc: '2.0', method };
    if (params !== undefined) body.params = params;
    this.child.stdin.write(JSON.stringify(body) + '\n');
  }

  async waitForNotification(method, timeoutMs) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (this.notifications.some((n) => n.method === method)) return true;
      await sleep(50);
    }
    return false;
  }

  close() { try { this.child.stdin.end(); } catch (e) {} try { this.child.kill(); } catch (e) {} }
}

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

const results = [];
function record(name, ok, ms, detail) {
  results.push({ name, ok, ms: ms ?? 0, detail: detail || '' });
  console.log(`  [${ok ? 'PASS' : 'FAIL'}] ${name}${detail ? ' - ' + detail : ''}`);
}
function isResult(m) { return m && m.result !== undefined && m.error === undefined; }
function isError(m) { return m && m.error !== undefined; }
function isReloadEnvelope(m) {
  return !!(m && m.result && m.result._meta && m.result._meta['com.strangeape.open-unity-mcp/reloadInterrupted']);
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function writeStatus(project, obj) {
  const dir = path.join(project, 'Temp', 'OpenUnityMcp');
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'server-status.json'), JSON.stringify(obj));
}

async function main() {
  console.log('Open Unity MCP sidecar fault-injection (hermetic mock)');
  const project = fs.mkdtempSync(path.join(os.tmpdir(), 'oum-sidecar-fi-'));
  const mock = new MockUnity();
  await mock.start();
  // Enforce a token from the start so every scenario also proves the sidecar
  // attaches the token it read from the status file.
  const TOKEN_A = 'a'.repeat(64);
  mock.token = TOKEN_A;
  mock.requireToken = true;
  writeStatus(project, { state: 'running', port: mock.port, token: TOKEN_A, timestamp: Date.now() });
  console.log('  mock port=' + mock.port + ' project=' + project);
  console.log('  token enforcement ON (token in status file)');
  console.log('');

  const sidecar = new Sidecar(mock.port, project, 8000);

  try {
    // Scenario 1: healthy + initialize capability rewrite.
    const init = await sidecar.request('initialize', { protocolVersion: '2025-06-18' });
    const lc = init.message.result?.capabilities?.tools?.listChanged;
    record('healthy initialize', isResult(init.message) && lc === true, init.ms, 'listChanged rewritten=' + lc);

    const t = await sidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} });
    record('healthy tools/call', isResult(t.message) && !isError(t.message), t.ms, JSON.stringify(t.message.result?.content?.[0]?.text));

    // Scenario 2: connect-level outage on a tools/call, then recover -> retried,
    // normal result, no error. (Connect-level is safe to retry even for tools/call.)
    writeStatus(project, { state: 'reloading', port: mock.port, token: mock.token, timestamp: Date.now() });
    await mock.goDown();
    const hitsBefore = mock.mcpHits;
    const pending = sidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} }, 20000);
    await sleep(600); // sidecar is now polling /health with the listener down
    await mock.comeUp();
    writeStatus(project, { state: 'running', port: mock.port, token: mock.token, timestamp: Date.now() });
    const r2 = await pending;
    record('connect-outage tools/call retried', isResult(r2.message) && !isError(r2.message) && !isReloadEnvelope(r2.message), r2.ms,
      'delivered live result after recovery');

    // Scenario 3: mid-flight reset on tools/call -> verify-and-retry envelope,
    // and the tool is NOT resent. midflightOnce resets exactly one POST then heals,
    // so the POST count directly proves whether the sidecar resent it.
    const hitsPre3 = mock.mcpHits;
    mock.setMode('midflightOnce');
    const r3 = await sidecar.request('tools/call', { name: 'unity.write_asset_text', arguments: { path: 'x' } }, 20000);
    const posts3 = mock.mcpHits - hitsPre3;
    record('midflight tools/call NOT resent', isResult(r3.message) && isReloadEnvelope(r3.message) && posts3 === 1, r3.ms,
      'envelope returned; /mcp POSTs=' + posts3 + ' (1 = not resent)');
    record('midflight tools/call is a success (not error)', isResult(r3.message) && !isError(r3.message), 0,
      'isError=' + !!r3.message.error);

    // Scenario 4: mid-flight reset on an idempotent method -> retried transparently.
    // midflightOnce resets the first POST, heals, and the sidecar's retry succeeds.
    const hitsPre4 = mock.mcpHits;
    mock.setMode('midflightOnce');
    const r4 = await sidecar.request('tools/list', {}, 20000);
    const posts4 = mock.mcpHits - hitsPre4;
    record('midflight idempotent tools/list retried', isResult(r4.message) && !isError(r4.message) && !isReloadEnvelope(r4.message) && posts4 === 2, r4.ms,
      'live result, tools=' + (r4.message.result?.tools?.length ?? 0) + ', /mcp POSTs=' + posts4 + ' (2 = resent)');

    // Scenario 7: the sidecar attached the access token on the last live request.
    // Every prior /mcp POST already passed the mock's requireToken gate (else they
    // would have 401'd and the scenarios above would have failed), but assert the
    // headers explicitly for clarity.
    record('access token attached on forward',
      mock.lastAuthHeader === 'Bearer ' + TOKEN_A && mock.lastXTokenHeader === TOKEN_A, 0,
      'authorization=' + JSON.stringify(mock.lastAuthHeader) + ' x-token-present=' + (mock.lastXTokenHeader === TOKEN_A));

    // Scenario 8: token rotation. The editor rebinds with a NEW token and rejects
    // the stale one with a 401. The sidecar should silently re-read the status file
    // and resend once, so the client never sees a 401 — just a normal result.
    const TOKEN_B = 'b'.repeat(64);
    const unauthorizedBefore = mock.unauthorizedHits;
    mock.token = TOKEN_B; // rotate; sidecar still holds TOKEN_A in its cache
    writeStatus(project, { state: 'running', port: mock.port, token: TOKEN_B, timestamp: Date.now() });
    const r8 = await sidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} }, 20000);
    const rejected = mock.unauthorizedHits - unauthorizedBefore;
    record('rotated token: 401 re-read and resent silently',
      isResult(r8.message) && !isError(r8.message) && rejected === 1 &&
        mock.lastAuthHeader === 'Bearer ' + TOKEN_B, r8.ms,
      '401s=' + rejected + ' (1 = one rejection then re-read), client saw result');

    // Scenario 5: server genuinely gone (status=stopped) -> JSON-RPC error.
    await mock.goDown();
    writeStatus(project, { state: 'stopped', port: mock.port, timestamp: Date.now() });
    const r5 = await sidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} }, 20000);
    record('deadline/gone -> JSON-RPC error', isError(r5.message), r5.ms,
      'code=' + (r5.message.error && r5.message.error.code));

    // Scenario 6: recovery notification emitted at least once across the run.
    const listChangedNotes = sidecar.notifications.filter((n) => n.method === 'notifications/tools/list_changed').length;
    record('list_changed emitted after recovery', listChangedNotes >= 1, 0, 'count=' + listChangedNotes);

    await mock.comeUp();
    writeStatus(project, { state: 'running', port: mock.port, token: mock.token, timestamp: Date.now() });

    // Scenario 9: client notifications stay in the sidecar. The editor ignores them,
    // and forwarding one while Unity is down would hold the queue for the timeout.
    const hitsPre9 = mock.mcpHits;
    sidecar.notify('notifications/initialized');
    await sleep(200);
    record('client notifications not forwarded', mock.mcpHits === hitsPre9, 0, '/mcp POSTs=' + (mock.mcpHits - hitsPre9));

    // Scenario 10: ping is answered by the sidecar even while a tools/call is
    // queued waiting out a reload.
    writeStatus(project, { state: 'reloading', port: mock.port, token: mock.token, timestamp: Date.now() });
    await mock.goDown();
    const stuck = sidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} }, 20000);
    await sleep(300);
    const ping = await sidecar.request('ping', {}, 5000);
    await mock.comeUp();
    writeStatus(project, { state: 'running', port: mock.port, token: mock.token, timestamp: Date.now() });
    const unstuck = await stuck;
    record('ping answered during an outage', isResult(ping.message) && ping.ms < 1000 && isResult(unstuck.message), ping.ms,
      'ping ' + ping.ms + 'ms while tools/call waited ' + unstuck.ms + 'ms');

    // Scenario 11: a request cancelled while queued behind a slow one never
    // reaches Unity and gets no response.
    mock.slowMs = 800;
    mock.setMode('slowOnce');
    const slow = sidecar.send('tools/call', { name: 'unity.slow_tool', arguments: {} }, 20000);
    const doomed = sidecar.send('tools/call', { name: 'unity.cancelled_tool', arguments: {} }, 20000);
    doomed.response.catch(() => {});
    await sleep(100);
    sidecar.notify('notifications/cancelled', { requestId: doomed.id, reason: 'test' });
    await slow.response;
    await sleep(400);
    const doomedAnswered = sidecar.responses.some((m) => m.id === doomed.id);
    record('cancelled queued request never runs',
      !mock.calledTools.includes('unity.cancelled_tool') && !doomedAnswered, 0,
      'reached Unity=' + mock.calledTools.includes('unity.cancelled_tool') + ' answered=' + doomedAnswered);

    // Scenario 12: a request carrying a progressToken gets heartbeats while it is
    // pending, and none after its response.
    mock.slowMs = 900;
    mock.setMode('slowOnce');
    const withProgress = await sidecar.request('tools/call',
      { name: 'unity.get_project_info', arguments: {}, _meta: { progressToken: 'heartbeat' } }, 20000);
    const beatsAtResponse = sidecar.notifications.filter((n) => n.method === 'notifications/progress' && n.params.progressToken === 'heartbeat').length;
    await sleep(500);
    const beatsLater = sidecar.notifications.filter((n) => n.method === 'notifications/progress' && n.params.progressToken === 'heartbeat').length;
    record('progress heartbeat while pending', isResult(withProgress.message) && beatsAtResponse >= 2 && beatsLater === beatsAtResponse,
      withProgress.ms, 'beats=' + beatsAtResponse + ' after=' + (beatsLater - beatsAtResponse));
  } finally {
    sidecar.close();
  }

  // Scenario 13: a stale 'stopped' status left by another project must not abort
  // the wait for the editor this sidecar is talking to (the old code gave up
  // instantly, so every reload looked like the editor had quit).
  mock.requireToken = false;
  mock.projectPath = null;
  const otherProject = fs.mkdtempSync(path.join(os.tmpdir(), 'oum-sidecar-fi-other-'));
  writeStatus(otherProject, { state: 'stopped', port: mock.port, timestamp: Date.now() - 60000 });
  const staleSidecar = new Sidecar(mock.port, otherProject, 8000);
  try {
    await staleSidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} });
    await mock.goDown();
    const during = staleSidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} }, 20000);
    await sleep(600);
    await mock.comeUp();
    const r13 = await during;
    record('stale stopped status ignored', isResult(r13.message) && !isReloadEnvelope(r13.message), r13.ms,
      isError(r13.message) ? 'error: ' + r13.message.error.message.slice(0, 60) : 'waited out the outage');
  } finally {
    staleSidecar.close();
  }

  // Scenario 14: the sidecar follows the project /health reports, so it reads that
  // project's token even though --project names a different one.
  const liveProject = fs.mkdtempSync(path.join(os.tmpdir(), 'oum-sidecar-fi-live-'));
  const TOKEN_C = 'c'.repeat(64);
  writeStatus(liveProject, { state: 'running', port: mock.port, token: TOKEN_C, timestamp: Date.now() });
  mock.projectPath = liveProject;
  mock.token = TOKEN_C;
  mock.requireToken = true;
  const followSidecar = new Sidecar(mock.port, otherProject, 8000);
  try {
    await sleep(300);
    const r14 = await followSidecar.request('tools/call', { name: 'unity.get_project_info', arguments: {} });
    record('follows the project /health reports', isResult(r14.message) && mock.lastXTokenHeader === TOKEN_C, r14.ms,
      'token from live project=' + (mock.lastXTokenHeader === TOKEN_C));
  } finally {
    followSidecar.close();
    mock.requireToken = false;
    mock.projectPath = null;
  }

  // Scenario 15: Unity not running when the client starts. The handshake and
  // tool list are answered at once, and list_changed follows when Unity appears,
  // instead of the client timing out its handshake and dropping the server.
  const offlineState = path.join(otherProject, 'offline-state');
  await mock.goDown();
  writeStatus(otherProject, { state: 'stopped', port: mock.port, timestamp: Date.now() });
  const offline = new Sidecar(mock.port, otherProject, 8000, offlineState);
  try {
    const init15 = await offline.request('initialize', { protocolVersion: '2025-06-18' }, 5000);
    const tools15 = await offline.request('tools/list', {}, 5000);
    const offlineNames = (tools15.message.result?.tools || []).map((t) => t.name);
    record('offline handshake answered immediately',
      isResult(init15.message) && init15.message.result.protocolVersion === '2025-06-18' &&
        init15.message.result.capabilities?.tools?.listChanged === true && init15.ms < 2000 &&
        isResult(tools15.message) && offlineNames.includes('unity.run_code'),
      init15.ms, 'tools=' + offlineNames.length + ' (session tools only)');

    await mock.comeUp();
    const announced = await offline.waitForNotification('notifications/tools/list_changed', 8000);
    const live15 = await offline.request('tools/list', {}, 5000);
    const liveNames = (live15.message.result?.tools || []).map((t) => t.name);
    record('list_changed once Unity appears', announced && liveNames.includes('unity.get_project_info'), 0,
      'announced=' + announced + ' live tools=' + liveNames.length);
  } finally {
    offline.close();
  }

  // Scenario 16: the next cold start serves the catalog cached from the live
  // editor, so the client sees the real tools before Unity is even open.
  await mock.goDown();
  const cached = new Sidecar(mock.port, otherProject, 8000, offlineState);
  try {
    await cached.request('initialize', { protocolVersion: '2025-11-25' }, 5000);
    const tools16 = await cached.request('tools/list', {}, 5000);
    const cachedNames = (tools16.message.result?.tools || []).map((t) => t.name);
    record('cold start serves cached catalog', cachedNames.includes('unity.get_project_info') &&
      cachedNames.filter((n) => n === 'unity.run_code').length === 1, tools16.ms, 'tools=' + cachedNames.join(','));
  } finally {
    cached.close();
    await mock.stop();
    for (const dir of [project, otherProject, liveProject]) {
      try { fs.rmSync(dir, { recursive: true, force: true }); } catch (e) {}
    }
  }

  console.log('');
  console.log('Behavior matrix');
  console.log('  ' + 'scenario'.padEnd(46) + 'result   ms');
  console.log('  ' + '-'.repeat(64));
  for (const r of results) console.log('  ' + r.name.padEnd(46) + (r.ok ? 'PASS' : 'FAIL').padEnd(9) + String(r.ms).padStart(6));
  const failed = results.filter((r) => !r.ok);
  console.log('');
  console.log(failed.length === 0 ? 'ALL PASS' : (failed.length + ' FAILED'));
  process.exit(failed.length === 0 ? 0 : 1);
}

main().catch((e) => { console.error('FI ERROR: ' + (e && e.stack ? e.stack : e)); process.exit(1); });
