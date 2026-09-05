import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { UnitySession, toolResult } = require('../unity-session');

async function usingSession(call, work) {
  const session = new UnitySession(call);
  try { await work(session); } finally { session.reset(); await session.bridgeChain; }
}

test('persistent state, explicit output, structured SDK results and timings', async () => {
  await usingSession(async () => toolResult({ objects: [1] }), async session => {
    assert.equal((await session.run({ code: 'state.targets = await unity.scene.query();' })).isError, false);
    const result = await session.run({ code: 'emit(state.targets);' });
    assert.deepEqual(result.structuredContent.output, [{ objects: [1] }]);
    assert.equal(session.status().receipts[0].state, 'completed');
    assert.equal(typeof session.status().receipts[0].elapsedMs, 'number');
    assert.equal(result.structuredContent.sdkCalls, 0);
  });
});

test('bulk edit is exactly one bridge call and retains epoch', async () => {
  const calls = [];
  await usingSession(async (name, args) => { calls.push({ name, args }); return toolResult({ complete: true }); }, async session => {
    const result = await session.run({ code: 'emit(await unity.edit({targets:{editorEpoch:"epoch",objects:[{components:[{objectId:"7"},{objectId:"8"}]}]},set:{m_Intensity:2.5}}));' });
    assert.equal(result.isError, false);
    assert.deepEqual(calls, [{ name: 'unity.edit_objects', args: { editorEpoch: 'epoch', targets: ['7', '8'], set: { m_Intensity: 2.5 }, label: undefined } }]);
  });
});

test('bulk edit refuses unacknowledged query truncation', async () => {
  await usingSession(() => { throw new Error('Must not call Unity'); }, async session => {
    assert.equal((await session.run({ code: 'await unity.edit({targets:{editorEpoch:"epoch",objects:[],hasMore:true},set:{m_Intensity:1}});' })).isError, true);
    assert.equal(session.status().receipts.length, 0);
  });
});

test('SDK errors retain partial results and uncertain receipts', async () => {
  await usingSession(async () => ({ ...toolResult({ attempted: 2 }, true), _meta: { 'com.strangeape.open-unity-mcp/verifyBeforeRetry': true } }), async session => {
    const result = await session.run({ code: 'await unity.call("unity.batch",{});' });
    assert.equal(result.isError, true);
    assert.deepEqual(result.structuredContent.error.result, { attempted: 2 });
    assert.equal(session.status().receipts[0].state, 'unknown');
  });
});

test('loop timeout clears state and permits a fresh worker', async () => {
  await usingSession(async () => toolResult({}), async session => {
    const oldId = session.status().sessionId;
    const result = await session.run({ code: 'state.x=1; while(true) {}', timeoutMs: 200 });
    assert.equal(result.isError, true);
    assert.notEqual(session.status().sessionId, oldId);
    assert.deepEqual((await session.run({ code: 'emit(Object.keys(state));' })).structuredContent.output, [[]]);
  });
});

test('reset blocks new cells until dispatched mutations drain', async () => {
  let finish;
  let started;
  const start = new Promise(resolve => { started = resolve; });
  await usingSession(() => { started(); return new Promise(resolve => { finish = resolve; }); }, async session => {
    const pending = session.run({ code: 'await unity.call("unity.set_transform",{});' });
    await start;
    session.reset();
    assert.equal((await pending).isError, true);
    assert.equal(session.status().state, 'draining');
    assert.equal((await session.run({ code: 'emit(1);' })).isError, true);
    finish(toolResult({ changed: true }));
    await session.bridgeChain;
    assert.equal((await session.run({ code: 'emit(1);' })).isError, false);
  });
});

test('forgotten await is drained and failed calls fail the cell', async () => {
  await usingSession(async () => { await new Promise(r => setTimeout(r, 20)); return toolResult({ failure: true }, true); }, async session => {
    const result = await session.run({ code: 'unity.call("unity.add_component",{});' });
    assert.equal(result.isError, true);
    assert.equal(session.status().receipts.length, 1);
  });
});

test('bounded output rejects excessive text and forwards image blocks', async () => {
  await usingSession(async () => ({ content: [{ type: 'image', mimeType: 'image/png', data: 'AAAA' }], isError: false }), async session => {
    assert.equal((await session.run({ code: 'emit("x".repeat(140000));' })).isError, true);
    const result = await session.run({ code: 'emit(await unity.view.capture());' });
    assert.equal(result.content[1].type, 'image');
    assert.deepEqual(result.structuredContent.output, [{ imageCount: 1 }]);
  });
});

test('compilation wait returns stable idle in one code cell', async () => {
  let count = 0;
  await usingSession(async () => toolResult({ isCompiling: ++count < 2, isUpdating: false }), async session => {
    const result = await session.run({ code: 'emit(await unity.compilation.wait({timeoutMs:3000}));' });
    assert.equal(result.isError, false);
    assert.equal(result.structuredContent.output[0].isCompiling, false);
    assert.equal(result.structuredContent.sdkCalls, 1);
    assert.ok(count >= 4);
  });
});

test('uncloneable SDK arguments fail promptly and cyclic error details stay bounded', async () => {
  await usingSession(() => { throw new Error('Must not reach Unity'); }, async session => {
    const start = Date.now();
    assert.equal((await session.run({ code: 'await unity.call("unity.get_components",{invalid:()=>{}});', timeoutMs: 2000 })).isError, true);
    assert.ok(Date.now() - start < 1500);
    const result = await session.run({ code: 'const e=new Error("cycle"); e.result={}; e.result.self=e.result; throw e;' });
    assert.equal(result.isError, true);
    assert.equal(result.structuredContent.error.detailsOmitted, true);
    assert.equal((await session.run({ code: 'emit(1);' })).isError, false);
  });
});
