// Invoked by the EditMode live integration test with a running Unity server.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

const [port, project, rootId] = process.argv.slice(2);
const sidecar = fileURLToPath(new URL('../open-unity-mcp-sidecar.js', import.meta.url));
const child = spawn(process.execPath, [sidecar, '--port', port, '--project', project], { stdio: ['pipe', 'pipe', 'pipe'] });
child.stderr.pipe(process.stderr);
const rl = createInterface({ input: child.stdout });
const pending = new Map();
let id = 0;
rl.on('line', line => { const msg = JSON.parse(line); pending.get(msg.id)?.(msg); pending.delete(msg.id); });
function call(name, args) {
  return new Promise((resolve, reject) => {
    const requestId = ++id;
    const timer = setTimeout(() => reject(new Error('Live session timed out')), 15000);
    pending.set(requestId, msg => { clearTimeout(timer); if (msg.error || msg.result.isError) reject(new Error(JSON.stringify(msg))); else resolve(msg.result); });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: requestId, method: 'tools/call', params: { name, arguments: args } }) + '\n');
  });
}
try {
  const query = await call('unity.run_code', { code: `state.lights = await unity.scene.query({rootObjectId:${JSON.stringify(rootId)},componentType:"UnityEngine.Light",limit:100}); emit(state.lights.count);` });
  assert.equal(query.structuredContent.output[0], 5);
  const edit = await call('unity.run_code', { code: 'emit(await unity.edit({targets:state.lights,set:{m_Intensity:4.25}}));' });
  assert.equal(edit.structuredContent.editorRequests, 1);
  const result = edit.structuredContent.output[0];
  assert.equal(result.results.length, 5);
  for (const target of result.results) assert.equal(target.values.m_Intensity, 4.25);
  const status = await call('unity.session_status', {});
  const timing = status.structuredContent.receipts.at(-1).editorTiming;
  assert.ok(timing.dispatchMs >= timing.executeMs);
  console.log(JSON.stringify({ targets: 5, editorRequests: edit.structuredContent.editorRequests, editMs: edit.structuredContent.elapsedMs, timing, verified: true }));
} finally { rl.close(); child.kill(); }
