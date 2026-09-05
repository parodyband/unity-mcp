#!/usr/bin/env node
'use strict';

// Open Unity MCP sidecar.
//
// A persistent stdio MCP endpoint that forwards JSON-RPC to the in-editor HTTP
// server at http://127.0.0.1:<port>/mcp and rides out Unity domain reloads so
// the MCP client never sees a connection error.
//
// Transport: newline-delimited JSON-RPC (NDJSON) on stdin/stdout. One JSON
// message per line. This is NOT LSP Content-Length framing. All logging goes to
// stderr; stdout carries protocol bytes only.
//
// Requires Node 18+ (built-in global fetch, AbortController). Zero npm deps.

const fs = require('fs');
const path = require('path');
const readline = require('readline');
const { UnitySession, SESSION_TOOLS, INSTRUCTIONS, toolResult } = require('./unity-session');

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

function parseArgs(argv) {
  const config = {
    port: parseInt(process.env.OPEN_UNITY_MCP_PORT || '', 10) || 8080,
    project: process.cwd(),
    // How long to wait for the editor to come back before giving up on a
    // request. Covers compile + domain reload of a large project.
    timeoutMs: 90000,
    codeEnabled: true
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--no-code') {
      config.codeEnabled = false;
    } else if (arg === '--port' && i + 1 < argv.length) {
      const value = parseInt(argv[++i], 10);
      if (Number.isFinite(value) && value > 0) {
        config.port = value;
      }
    } else if (arg === '--project' && i + 1 < argv.length) {
      config.project = argv[++i];
    } else if (arg === '--timeout' && i + 1 < argv.length) {
      const value = parseInt(argv[++i], 10);
      if (Number.isFinite(value) && value > 0) {
        config.timeoutMs = value;
      }
    }
  }

  return config;
}

const CONFIG = parseArgs(process.argv.slice(2));
const MCP_URL = 'http://127.0.0.1:' + CONFIG.port + '/mcp';
const HEALTH_URL = 'http://127.0.0.1:' + CONFIG.port + '/health';
const STATUS_FILE = path.join(CONFIG.project, 'Temp', 'OpenUnityMcp', 'server-status.json');

// Methods whose retry is always safe: they do not mutate editor state, so
// resending them after a reload can only return the same answer. tools/call is
// deliberately excluded — a tool may have already run before the socket died.
const IDEMPOTENT_METHODS = new Set([
  'initialize',
  'ping',
  'tools/list',
  'resources/list',
  'resources/read',
  'prompts/list',
  'prompts/get'
]);

// Backoff bounds for the health poll while the editor is rebooting.
const POLL_MIN_MS = 250;
const POLL_MAX_MS = 500;
// A single forwarding attempt should not hang forever if the editor accepts the
// connection but never answers (e.g. main thread blocked mid-import). The outer
// deadline still bounds the whole request; this just bounds one attempt so we
// fall back into the recovery loop instead of stalling.
const ATTEMPT_TIMEOUT_MS = 60000;
// Connecting to a live loopback server is near-instant; a slow connect means the
// listener is gone. Keep this short so ECONNREFUSED-style outages are detected
// quickly and we drop into the health poll.
const CONNECT_TIMEOUT_MS = 2000;

// ---------------------------------------------------------------------------
// Logging (stderr only)
// ---------------------------------------------------------------------------

function log(message) {
  process.stderr.write('[open-unity-mcp-sidecar] ' + message + '\n');
}

// ---------------------------------------------------------------------------
// Stdout writer (protocol only, newline-delimited)
// ---------------------------------------------------------------------------

function writeMessage(obj) {
  process.stdout.write(JSON.stringify(obj) + '\n');
}

// ---------------------------------------------------------------------------
// Failure classification
// ---------------------------------------------------------------------------

// Only explicit connection failures such as ECONNREFUSED prove non-delivery.
// All other transport errors are uncertain, including timeouts before headers.
//
// Node surfaces these differently depending on where in the lifecycle they land.
// fetch() wraps the underlying error, so inspect both the wrapper and its cause.
function isConnectLevelError(err) {
  if (!err) {
    return false;
  }

  const codes = collectErrorCodes(err);
  if (codes.has('ECONNREFUSED') ||
      codes.has('ENOTFOUND') ||
      codes.has('EHOSTUNREACH') ||
      codes.has('ENETUNREACH') ||
      codes.has('EADDRNOTAVAIL')) {
    return true;
  }

  return false;
}

function collectErrorCodes(err) {
  const codes = new Set();
  let current = err;
  let guard = 0;
  while (current && guard < 8) {
    if (typeof current.code === 'string') {
      codes.add(current.code);
    }
    current = current.cause;
    guard++;
  }
  return codes;
}

// ---------------------------------------------------------------------------
// Status file
// ---------------------------------------------------------------------------

// Reads <project>/Temp/OpenUnityMcp/server-status.json if present. Lets the
// sidecar distinguish "reloading, hold" from "stopped/dead". Best-effort: any
// read/parse error yields null and we fall back to health polling alone.
function readStatusFile() {
  try {
    const text = fs.readFileSync(STATUS_FILE, 'utf8');
    const parsed = JSON.parse(text);
    if (parsed && typeof parsed === 'object') {
      return parsed;
    }
  } catch (err) {
    // Missing/unreadable/corrupt status file is expected in plenty of states
    // (fresh project, mid-write). Health polling is the source of truth.
  }
  return null;
}

// The status file is only a hint. It says "stopped" only on a clean editor quit;
// a crash leaves it "running" or "reloading" forever. So we treat "stopped" as a
// strong dead signal, but never treat "running"/"reloading" as proof of life —
// only a successful /health response does that.
function statusSaysStopped(status) {
  return status !== null && status.state === 'stopped';
}

// ---------------------------------------------------------------------------
// Access token
// ---------------------------------------------------------------------------

// The in-editor server writes its access token into the status file on every
// start (whether or not enforcement is on). We attach it to every /mcp forward
// so that enforcement can be toggled on in Unity without any client change. The
// token is cached and refreshed after a recovery and on a 401 (see below).
let cachedToken = null;

function readTokenFromStatusFile() {
  const status = readStatusFile();
  if (status && typeof status.token === 'string' && status.token.length > 0) {
    return status.token;
  }
  return null;
}

function refreshToken() {
  const token = readTokenFromStatusFile();
  if (token) {
    cachedToken = token;
  }
  return cachedToken;
}

// ---------------------------------------------------------------------------
// Health polling
// ---------------------------------------------------------------------------

async function pollHealthOnce() {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), CONNECT_TIMEOUT_MS + 500);
  try {
    const response = await fetch(HEALTH_URL, {
      method: 'GET',
      signal: controller.signal
    });
    // Any HTTP answer means the listener is back, even a non-200.
    await response.text().catch(() => {});
    return response.ok;
  } catch (err) {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

function nextBackoff(current) {
  const grown = Math.min(POLL_MAX_MS, Math.floor(current * 1.5));
  return Math.max(POLL_MIN_MS, grown);
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// Waits until /health answers or the deadline passes. Returns true if the
// editor came back, false if the deadline expired or the status file reports a
// clean shutdown (in which case waiting further is pointless).
async function waitForServer(deadline) {
  let backoff = POLL_MIN_MS;
  while (Date.now() < deadline) {
    if (await pollHealthOnce()) {
      return true;
    }

    // A clean quit will never come back on its own; bail early so the caller can
    // return a "restart the editor" error instead of burning the full deadline.
    if (statusSaysStopped(readStatusFile())) {
      log('status file reports the editor stopped; abandoning wait.');
      return false;
    }

    await sleep(backoff);
    backoff = nextBackoff(backoff);
  }
  return false;
}

// ---------------------------------------------------------------------------
// Forwarding
// ---------------------------------------------------------------------------

// Posts one JSON-RPC body to the editor. Returns { ok:true, status, body } on an
// HTTP response (any status), or { ok:false, phase, error } on a transport
// failure, where phase is 'connect' | 'midflight' | 'unknown' so the caller can
// decide whether a retry is safe.
async function forwardOnce(rawBody) {
  const controller = new AbortController();
  // Only explicit connection failures prove non-delivery. A header/read timeout
  // may occur after Unity has already started or completed the mutation.
  const attemptTimer = setTimeout(() => {
    controller.__oumReadTimeout = true;
    controller.abort();
  }, ATTEMPT_TIMEOUT_MS);

  try {
    const headers = { 'Content-Type': 'application/json' };
    // Attach the access token if we have one. Sending it unconditionally is
    // harmless when the editor is not enforcing, and means enforcement can be
    // turned on in Unity with no client-side change.
    if (cachedToken) {
      headers['Authorization'] = 'Bearer ' + cachedToken;
      headers['X-Open-Unity-Mcp-Token'] = cachedToken;
    }

    const response = await fetch(MCP_URL, {
      method: 'POST',
      headers: headers,
      body: rawBody,
      signal: controller.signal
    });

    const text = await response.text();
    return { ok: true, status: response.status, body: text };
  } catch (err) {
    // Tag the error with which timer fired so classification can use it.
    if (controller.__oumReadTimeout) {
      err.__oumReadTimeout = true;
    }

    const phase = isConnectLevelError(err) ? 'connect' : 'midflight';

    return { ok: false, phase: phase, error: err };
  } finally {
    clearTimeout(attemptTimer);
  }
}

// ---------------------------------------------------------------------------
// Recovery messaging
// ---------------------------------------------------------------------------

// A SUCCESSFUL JSON-RPC result (not an error) for a tools/call that was
// interrupted mid-flight by a reload. A structured success keeps agent loops
// alive; a transport error would kill them. This is the uLoopMCP pattern.
function reloadInterruptedResult(id, toolName) {
  const tool = toolName ? ('`' + toolName + '`') : 'the tool';
  const text =
    'The connection was interrupted while ' + tool + ' was in flight (for example during a domain reload). ' +
    'The operation MAY OR MAY NOT have ' +
    'applied — the sidecar did not resend it, because re-running a mutation across a ' +
    'reload can duplicate its effects.\n\n' +
    'Wait for editor availability and verify the current state before retrying: ' +
    'call unity.get_compilation_status, and/or re-read the asset or inspect the object ' +
    'you were changing. If the change did not take effect, issue the call again.';

  return {
    jsonrpc: '2.0',
    id: id,
    result: {
      content: [{ type: 'text', text: text }],
      isError: false,
      _meta: {
        'com.strangeape.open-unity-mcp/reloadInterrupted': true,
        'com.strangeape.open-unity-mcp/verifyBeforeRetry': true
      }
    }
  };
}

// A JSON-RPC error for when the editor is genuinely gone (clean quit or the
// health poll never recovered within the deadline).
function editorGoneError(id) {
  return {
    jsonrpc: '2.0',
    id: id,
    error: {
      code: -32001,
      message:
        'The Unity editor appears to be closed: the Open Unity MCP server on port ' +
        CONFIG.port + ' did not come back within ' + Math.round(CONFIG.timeoutMs / 1000) +
        's. Open the Unity project and start the server (Tools > Open Unity MCP > Start ' +
        'Server, or enable Auto Start in Preferences > Open Unity MCP), then retry.'
    }
  };
}

// ---------------------------------------------------------------------------
// Request handling
// ---------------------------------------------------------------------------


function extractToolName(message) {
  try {
    if (message && message.params && typeof message.params.name === 'string') {
      return message.params.name;
    }
  } catch (err) {
    // ignore
  }
  return null;
}

// Rewrites an initialize *result* so that capabilities.tools.listChanged is true.
// We emit notifications/tools/list_changed after a recovery, which is only
// spec-legal if we advertised the capability. Best-effort string-free mutation
// on the parsed object.
function patchInitializeCapabilities(parsedBody) {
  try {
    const result = parsedBody && parsedBody.result;
    if (!result || typeof result !== 'object') {
      return;
    }
    if (!result.capabilities || typeof result.capabilities !== 'object') {
      result.capabilities = {};
    }
    if (!result.capabilities.tools || typeof result.capabilities.tools !== 'object') {
      result.capabilities.tools = {};
    }
    result.capabilities.tools.listChanged = true;
  } catch (err) {
    // Leave the body untouched on any surprise.
  }
}

// Emit a readiness signal after the editor comes back so clients refresh their
// tool list immediately instead of timing out on their next call.
function announceRecovery() {
  // The editor may have rebound with a freshly regenerated token; re-read it so
  // subsequent forwards carry the current secret.
  refreshToken();
  writeMessage({ jsonrpc: '2.0', method: 'notifications/tools/list_changed' });
}

// Handles a single parsed JSON-RPC message from the client.
async function handleEditorMessage(message, reply = writeMessage) {
  const hasId = Object.prototype.hasOwnProperty.call(message, 'id') && message.id !== null && message.id !== undefined;
  const rawBody = JSON.stringify(message);
  const method = typeof message.method === 'string' ? message.method : '';
  const deadline = Date.now() + CONFIG.timeoutMs;

  // First attempt against a presumed-healthy server.
  let result = await forwardOnce(rawBody);

  // A 401 means the editor turned on token enforcement (or regenerated the
  // token) since we last read the status file. Silently re-read the token and
  // resend once; the editor rejects the request before running any tool, so the
  // resend cannot duplicate a mutation. If still 401 (or no newer token), forward
  // the 401 body through unchanged.
  if (result.ok && result.status === 401) {
    const previousToken = cachedToken;
    const refreshed = refreshToken();
    if (refreshed && refreshed !== previousToken) {
      log('received 401; re-read access token from status file and resending once.');
      result = await forwardOnce(rawBody);
    } else {
      log('received 401 and no newer token available; forwarding the 401 through.');
    }
  }

  if (result.ok) {
    respond(message, hasId, method, result.body, false, reply);
    return;
  }

  // Transport failure. Decide recovery strategy from the failure phase.
  const interruptedMidFlight = result.phase === 'midflight';
  log('forward failed (' + result.phase + '): ' + describeError(result.error) + ' — waiting for editor.');

  const recovered = await waitForServer(deadline);

  if (!recovered) {
    // Editor is genuinely gone.
    if (hasId) {
      reply(editorGoneError(message.id));
    }
    log('editor did not recover within deadline; reported gone.');
    return;
  }

  // The editor is back. Signal readiness once we know recovery happened.
  announceRecovery();

  if (interruptedMidFlight && method === 'tools/call') {
    // The tool may already have run. Do not resend; return a structured success
    // that tells the model to verify and retry if needed.
    if (hasId) {
      reply(reloadInterruptedResult(message.id, extractToolName(message)));
    }
    log('tools/call was interrupted mid-flight; returned verify-and-retry result.');
    return;
  }

  // Safe to resend: either the request never reached the server (connect-level),
  // or it is an idempotent method that can run again harmlessly.
  if (interruptedMidFlight && !IDEMPOTENT_METHODS.has(method)) {
    // A non-idempotent, non-tools/call method interrupted mid-flight (rare —
    // notifications carry no id). Be conservative and do not resend.
    if (hasId) {
      reply(reloadInterruptedResult(message.id, null));
    }
    log('non-idempotent method interrupted mid-flight; returned verify-and-retry result.');
    return;
  }

  const retry = await forwardOnce(rawBody);
  if (retry.ok) {
    respond(message, hasId, method, retry.body, true, reply);
    log('resent request after recovery; responded normally.');
    return;
  }

  if (method === 'tools/call' && retry.phase !== 'connect') {
    if (hasId) reply(reloadInterruptedResult(message.id, extractToolName(message)));
    return;
  }

  // Came back, then failed again immediately. One more wait+send, bounded by the
  // same deadline, before declaring it gone.
  const recoveredAgain = await waitForServer(deadline);
  if (recoveredAgain) {
    const lastTry = await forwardOnce(rawBody);
    if (lastTry.ok) {
      respond(message, hasId, method, lastTry.body, true, reply);
      return;
    }
  }

  if (hasId) {
    reply(editorGoneError(message.id));
  }
  log('request still failing after recovery; reported gone.');
}

// Writes the editor's HTTP response body back to stdout for id-bearing requests.
// Notifications (no id) produce a 202 with an empty body and are dropped.
function respond(message, hasId, method, body, afterRecovery, reply = writeMessage) {
  if (!hasId) {
    // Notification: the Unity server answered 202 with no JSON-RPC body. Nothing
    // to forward to the client.
    return;
  }

  if (!body || body.length === 0) {
    // Defensive: an id-bearing request should always get a JSON body. If the
    // server returned empty, synthesize a minimal error rather than emitting a
    // blank line that would desync the client's parser.
    reply({
      jsonrpc: '2.0',
      id: message.id,
      error: { code: -32603, message: 'Empty response from Unity MCP server.' }
    });
    return;
  }

  // Rewrite the initialize result to advertise listChanged so our post-recovery
  // notification is spec-legal. Only touch initialize; forward everything else
  // byte-for-byte.
  if (method === 'initialize') {
    try {
      const parsed = JSON.parse(body);
      patchInitializeCapabilities(parsed);
      if (CONFIG.codeEnabled && parsed.result) parsed.result.instructions = INSTRUCTIONS + " " + (parsed.result.instructions || "");
      reply(parsed);
      return;
    } catch (err) {
      // Not parseable as expected; fall through to raw passthrough.
    }
  }

  // Forward the server's response verbatim as one NDJSON line.
  const parsed = JSON.parse(body);
  if (method === 'tools/list' && CONFIG.codeEnabled && Array.isArray(parsed.result?.tools)) {
    parsed.result.tools.push(...SESSION_TOOLS);
  }
  reply(parsed);
}

function describeError(err) {
  if (!err) {
    return 'unknown error';
  }
  const codes = Array.from(collectErrorCodes(err));
  const codePart = codes.length > 0 ? ' [' + codes.join(',') + ']' : '';
  return (err.message || String(err)) + codePart;
}

const session = new UnitySession(async (name, args) => {
  let response;
  await handleEditorMessage({ jsonrpc: '2.0', id: 'sdk', method: 'tools/call', params: { name, arguments: args } }, value => { response = value; });
  if (!response || response.error) throw new Error(response?.error?.message || 'Missing Unity response; outcome unknown.');
  return response.result;
}, { receiptPath: path.join(CONFIG.project, 'Temp', 'OpenUnityMcp', 'sessions', require('node:crypto').randomUUID() + '.json') });

async function handleMessage(message) {
  const name = message.method === 'tools/call' ? message.params?.name : null;
  if (CONFIG.codeEnabled && SESSION_TOOLS.some(t => t.name === name)) {
    if (message.id === undefined) return;
    const result = name === 'unity.run_code' ? await session.run(message.params.arguments)
      : name === 'unity.reset_session' ? session.reset() : toolResult(session.status());
    writeMessage({ jsonrpc: '2.0', id: message.id, result });
    return;
  }
  if (session.inFlight && message.method === 'tools/call') {
    writeMessage({ jsonrpc: '2.0', id: message.id, result: toolResult({ error: 'A stopped code cell still has Unity operations draining. Inspect unity.session_status before issuing more tools.' }, true) });
    return;
  }
  return handleEditorMessage(message);
}

// ---------------------------------------------------------------------------
// Serialized message pump
// ---------------------------------------------------------------------------

// MCP over stdio allows pipelined requests, but forwarding them concurrently
// through a reload would let a mutation and its verification race. Process one
// message at a time in arrival order; this matches the single-threaded Unity
// server and keeps recovery reasoning simple.
let chain = Promise.resolve();

function enqueue(message) {
  const generation = session.generation;
  chain = chain.then(() => {
    if (message.method === 'tools/call' && message.params?.name === 'unity.run_code' && generation !== session.generation) {
      if (message.id !== undefined) writeMessage({ jsonrpc: '2.0', id: message.id, result: toolResult({ error: 'Queued code cell cancelled by session reset.' }, true) });
      return;
    }
    return handleMessage(message);
  }).catch((err) => {
    log('unhandled error while handling message: ' + describeError(err));
    const hasId = message && Object.prototype.hasOwnProperty.call(message, 'id') &&
      message.id !== null && message.id !== undefined;
    if (hasId) {
      writeMessage({
        jsonrpc: '2.0',
        id: message.id,
        error: { code: -32603, message: 'Sidecar internal error: ' + (err && err.message ? err.message : String(err)) }
      });
    }
  });
}

// ---------------------------------------------------------------------------
// Startup
// ---------------------------------------------------------------------------

function main() {
  log('starting. endpoint=' + MCP_URL + ' project=' + CONFIG.project + ' timeout=' + CONFIG.timeoutMs + 'ms');
  log('status file=' + STATUS_FILE);

  // Prime the access token from the status file if the editor is already running.
  // It is refreshed after any recovery and on a 401, so a missing file here is fine.
  refreshToken();
  log('access token ' + (cachedToken ? 'loaded from status file' : 'not present yet (will read on demand)'));

  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

  rl.on('line', (line) => {
    const trimmed = line.trim();
    if (trimmed.length === 0) {
      return;
    }

    let message;
    try {
      message = JSON.parse(trimmed);
    } catch (err) {
      // A line we cannot parse cannot be answered (no id available). Log and
      // drop it rather than emitting a malformed response.
      log('dropping unparseable line: ' + describeError(err));
      return;
    }

    if (!message || typeof message !== 'object' || Array.isArray(message)) {
      log('dropping non-object JSON-RPC message.');
      return;
    }

    if (CONFIG.codeEnabled && message.method === 'tools/call' &&
        ['unity.session_status', 'unity.reset_session'].includes(message.params?.name)) {
      handleMessage(message).catch(error => log(error.message));
    } else enqueue(message);
  });

  rl.on('close', () => {
    // stdin closed: the client is gone. Let the in-flight chain settle, then exit.
    chain.finally(() => {
      log('stdin closed; exiting.');
      process.exit(0);
    });
  });

  process.on('SIGTERM', () => process.exit(0));
  process.on('SIGINT', () => process.exit(0));
}

main();
