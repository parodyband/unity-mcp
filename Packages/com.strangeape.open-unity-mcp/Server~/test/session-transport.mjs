import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('../open-unity-mcp-sidecar.js', import.meta.url));
async function fixture(work, options = []) {
  const project = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-session-transport-'));
  const requests = [];
  const server = http.createServer((req, res) => {
    if (req.url === '/health') { res.end('{}'); return; }
    let body = '';
    req.on('data', data => { body += data; });
    req.on('end', () => {
      const message = JSON.parse(body);
      requests.push(message);
      let result;
      if (message.method === 'initialize') result = { capabilities: {} };
      else if (message.method === 'tools/list') result = { tools: [] };
      else result = { structuredContent: { changed: true }, content: [], isError: false };
      const respond = () => res.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }));
      if (message.params?.name === 'unity.reset_mutation') res.destroy();
      else if (message.params?.name === 'unity.slow_mutation') setTimeout(respond, 2300);
      else respond();
    });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const child = spawn(process.execPath, [script, '--port', String(server.address().port), '--project', project, ...options], { stdio: ['pipe', 'pipe', 'pipe'] });
  child.stderr.resume();
  const pending = new Map();
  const rl = createInterface({ input: child.stdout });
  let id = 0;
  rl.on('line', line => { const msg = JSON.parse(line); pending.get(msg.id)?.(msg); pending.delete(msg.id); });
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const requestId = ++id;
    const timer = setTimeout(() => reject(new Error('Timed out waiting for sidecar')), 10000);
    pending.set(requestId, value => { clearTimeout(timer); resolve(value); });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: requestId, method, params }) + '\n');
  });
  try { await work({ send, requests, project, server, port: server.address().port }); }
  finally { child.kill(); rl.close(); server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); fs.rmSync(project, { recursive: true, force: true }); }
}

test('stdio advertises sessions, routes code and persists receipts without replaying slow mutations', async () => {
  await fixture(async ({ send, requests }) => {
    const init = await send('initialize');
    assert.match(init.result.instructions, /state/);
    const list = await send('tools/list');
    assert.equal(list.result.tools.filter(t => t.name === 'unity.run_code').length, 1);
    const started = Date.now();
    const result = await send('tools/call', { name: 'unity.run_code', arguments: { code: 'state.answer = await unity.call("unity.slow_mutation",{}); emit(state.answer);' } });
    assert.equal(result.result.isError, false);
    assert.ok(Date.now() - started >= 2200);
    assert.equal(requests.filter(r => r.params?.name === 'unity.slow_mutation').length, 1);
    const next = await send('tools/call', { name: 'unity.run_code', arguments: { code: 'emit(state.answer.changed);' } });
    assert.deepEqual(next.result.structuredContent.output, [true]);
    const status = await send('tools/call', { name: 'unity.session_status' });
    const saved = JSON.parse(fs.readFileSync(status.result.structuredContent.receiptPath));
    assert.equal(saved.receipts[0].state, 'completed');
  });
});

test('reset/status can interrupt a running cell through stdio', async () => {
  await fixture(async ({ send }) => {
    const cell = send('tools/call', { name: 'unity.run_code', arguments: { code: 'while(true) {}', timeoutMs: 10000 } });
    await new Promise(resolve => setTimeout(resolve, 150));
    const status = await send('tools/call', { name: 'unity.session_status' });
    assert.equal(status.result.structuredContent.state, 'running');
    await send('tools/call', { name: 'unity.reset_session' });
    assert.equal((await cell).result.isError, true);
    const next = await send('tools/call', { name: 'unity.run_code', arguments: { code: 'emit(42);' } });
    assert.deepEqual(next.result.structuredContent.output, [42]);
  });
});

test('--no-code does not advertise session tools', async () => {
  await fixture(async ({ send }) => {
    assert.deepEqual((await send('tools/list')).result.tools, []);
    assert.equal((await send('initialize')).result.instructions, undefined);
  }, ['--no-code']);
});

test('reset cancels code cells queued against the previous session generation', async () => {
  await fixture(async ({ send, requests }) => {
    const active = send('tools/call', { name: 'unity.run_code', arguments: { code: 'while(true) {}' } });
    const queued = send('tools/call', { name: 'unity.run_code', arguments: { code: 'await unity.call("unity.should_not_run",{});' } });
    await new Promise(resolve => setTimeout(resolve, 150));
    await send('tools/call', { name: 'unity.reset_session' });
    assert.equal((await active).result.isError, true);
    assert.equal((await queued).result.isError, true);
    assert.equal(requests.length, 0);
  });
});

test('mid-flight SDK interruption preserves variables and reports unknown without replay', async () => {
  await fixture(async ({ send, requests }) => {
    const result = await send('tools/call', { name: 'unity.run_code', arguments: { code: 'state.keep=7; await unity.call("unity.reset_mutation",{});' } });
    assert.equal(result.result.isError, true);
    assert.equal(requests.filter(r => r.params?.name === 'unity.reset_mutation').length, 1);
    const status = await send('tools/call', { name: 'unity.session_status' });
    assert.equal(status.result.structuredContent.receipts[0].state, 'unknown');
    const next = await send('tools/call', { name: 'unity.run_code', arguments: { code: 'emit(state.keep);' } });
    assert.deepEqual(next.result.structuredContent.output, [7]);
  });
});

test('mid-flight failure after connect recovery is not sent a third time', async () => {
  await fixture(async ({ send, requests, server, port }) => {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
    const pending = send('tools/call', { name: 'unity.run_code', arguments: { code: 'await unity.call("unity.reset_mutation",{});' } });
    await new Promise(resolve => setTimeout(resolve, 150));
    await new Promise(resolve => server.listen(port, '127.0.0.1', resolve));
    assert.equal((await pending).result.isError, true);
    assert.equal(requests.filter(r => r.params?.name === 'unity.reset_mutation').length, 1);
  });
});
