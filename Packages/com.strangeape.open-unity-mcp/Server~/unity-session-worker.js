'use strict';

const vm = require('node:vm');
const { parentPort } = require('node:worker_threads');
const { createUnitySdk } = require('./unity-session-sdk');
const pending = new Map();
let nextId = 0;
let active = null;

// VM provides a clean API namespace, NOT a security sandbox. This worker runs
// trusted local agent code. Its separate thread allows the host to stop loops.
const context = vm.createContext({
  state: {},
  bridge(name, args) {
    if (!active) return Promise.reject(new Error('No active cell.'));
    const id = ++nextId;
    const promise = new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
    // Observe rejections even when user code forgets await. Drain before completion.
    active.calls.push(promise.then(() => null, error => error));
    try { parentPort.postMessage({ kind: 'call', cellId: active.id, id, name, args }); }
    catch (error) { pending.get(id).reject(error); pending.delete(id); }
    return promise;
  },
  emit(value) {
    if (!active) throw new Error('No active cell.');
    const text = JSON.stringify(value);
    if (text === undefined) throw new Error('emit requires a JSON-safe value.');
    if (value?.content?.some(c => c.type === 'image')) {
      active.imageBytes += Buffer.byteLength(text);
      if (active.imageBytes > 8388608) throw new Error('Image output exceeds 8 MiB. Capture at a smaller resolution.');
    } else {
      active.bytes += Buffer.byteLength(text);
      if (active.bytes > 131072) throw new Error('Output exceeds 128 KiB. Emit a smaller summary.');
    }
    active.output.push(JSON.parse(text));
  }
});
vm.runInContext(`globalThis.unity = (${createUnitySdk.toString()})(bridge);`, context);

parentPort.on('message', async message => {
  if (message.kind === 'reply') {
    const item = pending.get(message.id);
    if (!item) return;
    pending.delete(message.id);
    if (message.error) item.reject(new Error(message.error));
    else if (message.result?.isError || message.result?._meta?.['com.strangeape.open-unity-mcp/verifyBeforeRetry']) {
      const error = new Error(message.result._meta?.['com.strangeape.open-unity-mcp/verifyBeforeRetry']
        ? 'Outcome unknown. Verify current Unity state before retrying.'
        : message.result.content?.find(c => c.type === 'text')?.text || 'Unity tool failed');
      error.result = message.result.structuredContent || message.result;
      item.reject(error);
    } else item.resolve(message.result);
    return;
  }
  if (message.kind !== 'run') return;
  active = { id: message.id, output: [], calls: [], bytes: 0, imageBytes: 0 };
  let error = null;
  try {
    await new vm.Script(`(async () => {\n${message.code}\n})()`, { filename: 'unity-session.js' }).runInContext(context);
  } catch (err) {
    error = { message: String(err?.message || err).slice(0, 4096) };
    if (err?.result !== undefined) {
      try {
        const details = JSON.stringify(err.result);
        if (details && Buffer.byteLength(details) <= 65536) error.result = JSON.parse(details);
        else error.detailsOmitted = true;
      } catch { error.detailsOmitted = true; }
    }
  }
  const failures = [];
  let drained = 0;
  while (drained < active.calls.length) {
    const calls = active.calls.slice(drained);
    drained = active.calls.length;
    failures.push(...await Promise.all(calls));
    await Promise.resolve();
  }
  if (!error && failures.some(Boolean)) error = { message: 'One or more SDK calls failed. Inspect output and session receipts before retrying.' };
  parentPort.postMessage({ kind: 'done', id: active.id, output: active.output, error });
  active = null;
});
