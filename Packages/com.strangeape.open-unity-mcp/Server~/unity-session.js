'use strict';

const { Worker } = require('node:worker_threads');
const { randomUUID } = require('node:crypto');
const path = require('node:path');
const fs = require('node:fs');

const INSTRUCTIONS = 'Use unity.run_code for persistent JavaScript workflows. Globals: unity, state (persists across cells), emit(value) (explicit JSON output). Start: state.targets = await unity.scene.query({componentType:"UnityEngine.Light",limit:100}); emit(state.targets). Then: emit(await unity.edit({targets:state.targets,set:{m_Intensity:2.5}})). APIs: unity.call(name,args), unity.discover(name), unity.batch(operations), unity.properties.read(objectId,propertyPaths), unity.view.capture(args), unity.compilation.wait({timeoutMs:60000}). To show a capture call emit(await unity.view.capture()); image blocks are forwarded. Await SDK calls. Inspect unity.session_status after errors; never replay uncertain mutations. Reset clears variables, not Unity changes. Code runs in a worker, not a security sandbox; authorize it as local code execution. Direct HTTP clients do not support sessions.';
const SESSION_TOOLS = [
  { name: 'unity.run_code', description: 'Execute trusted local JavaScript with persistent state, Unity SDK, and explicit emit output. May mutate Unity. Worker timeout resets variables but cannot undo/cancel an already dispatched Unity operation. ' + INSTRUCTIONS,
    inputSchema: { type: 'object', properties: { code: { type: 'string', maxLength: 32768 }, timeoutMs: { type: 'integer', minimum: 100, maximum: 120000 } }, required: ['code'], additionalProperties: false }, annotations: { readOnlyHint: false } },
  { name: 'unity.session_status', description: 'Inspect session/cell status, bounded operation receipts, timing, and receipt-file location without waiting for Unity. A running receipt left by a dead sidecar has an unknown outcome.', inputSchema: { type: 'object', properties: {}, additionalProperties: false }, annotations: { readOnlyHint: true } },
  { name: 'unity.reset_session', description: 'Stop JavaScript and clear session variables. Already dispatched Unity operations may still finish. Does not undo changes.', inputSchema: { type: 'object', properties: {}, additionalProperties: false }, annotations: { readOnlyHint: false } }
];

function toolResult(payload, error = false) {
  return { content: [{ type: 'text', text: JSON.stringify(payload) }], structuredContent: payload, isError: error };
}

class UnitySession {
  constructor(call, options = {}) {
    this.call = call;
    this.generation = randomUUID();
    this.worker = null;
    this.active = null;
    this.receipts = [];
    this.inFlight = 0;
    this.bridgeChain = Promise.resolve();
    this.last = null;
    this.receiptPath = options.receiptPath || null;
    this.storageError = null;
  }

  status() {
    return { sessionId: this.generation, activeCell: this.active?.id || null, inFlight: this.inFlight,
      state: this.active ? 'running' : this.inFlight ? 'draining' : 'idle', lastCell: this.last, receipts: this.receipts.slice(-64),
      receiptPath: this.receiptPath, storageError: this.storageError };
  }

  persist() {
    if (!this.receiptPath) return;
    try {
      fs.mkdirSync(path.dirname(this.receiptPath), { recursive: true });
      fs.writeFileSync(this.receiptPath + '.tmp', JSON.stringify({ pid: process.pid, ...this.status() }));
      fs.renameSync(this.receiptPath + '.tmp', this.receiptPath);
      this.storageError = null;
    } catch (error) { this.storageError = error.message; }
  }

  ensureWorker() {
    if (this.worker) return;
    const worker = this.worker = new Worker(path.join(__dirname, 'unity-session-worker.js'), {
      resourceLimits: { maxOldGenerationSizeMb: 64 }, stdout: true, stderr: true
    });
    // Never let arbitrary console output corrupt the NDJSON transport.
    worker.stdout.resume();
    worker.stderr.resume();
    worker.on('message', message => {
      if (message.kind === 'call') this.dispatch(worker, message);
      if (message.kind === 'done' && this.active?.id === message.id) this.finish(message);
    });
    worker.on('error', error => { if (this.worker === worker) this.reset('Worker failed: ' + error.message); });
    worker.on('exit', () => { if (this.worker === worker) this.reset('Worker exited; session variables were cleared.'); });
  }

  dispatch(worker, message) {
    const cell = this.active;
    if (!cell || cell.id !== message.cellId || ++cell.calls > 64) {
      worker.postMessage({ kind: 'reply', id: message.id, error: 'Inactive cell or SDK call budget exceeded (64).' });
      return;
    }
    this.inFlight++;
    this.bridgeChain = this.bridgeChain.then(async () => {
      if (this.active !== cell) { worker.postMessage({ kind: 'reply', id: message.id, error: 'Cell stopped before dispatch.' }); return; }
      const receipt = { id: randomUUID(), sessionId: this.generation, cellId: cell.id, tool: message.name, state: 'running', startedAt: Date.now() };
      this.receipts.push(receipt);
      if (this.receipts.length > 64) this.receipts.shift();
      this.persist();
      try {
        const result = message.name === '$waitCompilation'
          ? await this.waitCompilation(message.args, cell)
          : await this.invoke(message.name, message.args);
        receipt.state = result._meta?.['com.strangeape.open-unity-mcp/verifyBeforeRetry'] ? 'unknown' : result.isError ? 'failed' : 'completed';
        if (result._meta?.['com.strangeape.open-unity-mcp/timing']) receipt.editorTiming = result._meta['com.strangeape.open-unity-mcp/timing'];
        worker.postMessage({ kind: 'reply', id: message.id, result });
      } catch (error) {
        receipt.state = 'unknown';
        worker.postMessage({ kind: 'reply', id: message.id, error: error.message });
      } finally { receipt.elapsedMs = Date.now() - receipt.startedAt; this.persist(); }
    }).catch(() => {}).finally(() => { this.inFlight--; this.persist(); });
  }

  async invoke(name, args) {
    if (typeof name !== 'string' || !name.startsWith('unity.') || SESSION_TOOLS.some(t => t.name === name)) throw new Error('Invalid SDK tool; recursive sessions are not supported.');
    if (this.active) this.active.editorRequests++;
    return this.call(name, args || {});
  }

  async waitCompilation(args = {}, cell) {
    const timeout = Math.max(100, Math.min(110000, Number(args.timeoutMs) || 60000));
    const deadline = Date.now() + timeout;
    let idleSince = null;
    while (this.active === cell && Date.now() < deadline) {
      const result = await this.invoke('unity.get_compilation_status', { includeConsole: true, logLimit: 25 });
      if (result.isError) throw new Error('Could not read compilation status.');
      const status = result.structuredContent;
      if (status && !status.isCompiling && !status.isUpdating) {
        idleSince ??= Date.now();
        if (Date.now() - idleSince >= 750) return status;
      } else idleSince = null;
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error('Compilation wait stopped or timed out. Query diagnostics; do not repeat a refresh blindly.');
  }

  run(args = {}) {
    args = args || {};
    if (typeof args.code !== 'string' || Buffer.byteLength(args.code) > 32768) return Promise.resolve(toolResult({ error: 'code must be a string of at most 32 KiB.' }, true));
    if (this.active || this.inFlight) return Promise.resolve(toolResult({ error: 'Session busy or draining. Inspect session_status before continuing.' }, true));
    this.ensureWorker();
    return new Promise(resolve => {
      const cell = this.active = { id: randomUUID(), startedAt: Date.now(), calls: 0, editorRequests: 0, resolve };
      cell.timer = setTimeout(() => this.reset('Code timed out; variables cleared. Dispatched Unity operations may still apply.'), Math.max(100, Math.min(120000, Number(args.timeoutMs) || 30000)));
      this.worker.postMessage({ kind: 'run', id: cell.id, code: args.code });
    });
  }

  finish(message) {
    const cell = this.active;
    clearTimeout(cell.timer);
    this.last = { id: cell.id, elapsedMs: Date.now() - cell.startedAt, sdkCalls: cell.calls, editorRequests: cell.editorRequests, error: message.error || null };
    this.active = null;
    this.persist();
    const output = [];
    const images = [];
    for (const value of message.output || []) {
      if (value?.content?.some(c => c.type === 'image')) {
        images.push(...value.content.filter(c => c.type === 'image'));
        const summary = { imageCount: value.content.filter(c => c.type === 'image').length };
        const metadata = value.content.filter(c => c.type === 'text').map(c => c.text);
        if (metadata.length) summary.metadata = metadata;
        output.push(summary);
      } else output.push(value);
    }
    const result = toolResult({ sessionId: this.generation, ...this.last, output }, Boolean(message.error));
    result.content.push(...images);
    cell.resolve(result);
  }

  reset(reason = 'Session reset; Unity changes are retained.') {
    const worker = this.worker;
    this.worker = null;
    if (this.active) this.finish({ error: { message: reason }, output: [] });
    if (worker) worker.terminate();
    this.generation = randomUUID();
    return toolResult({ ...this.status(), message: reason });
  }
}

module.exports = { UnitySession, SESSION_TOOLS, INSTRUCTIONS, toolResult };
