'use strict';

// This function is loaded into the session context. Keep it self-contained.
function createUnitySdk(call) {
  async function invokePayload(name, args = {}) {
    const result = await call(name, args);
    if (result.isError || result._meta?.['com.strangeape.open-unity-mcp/verifyBeforeRetry']) {
      const error = new Error(result._meta?.['com.strangeape.open-unity-mcp/verifyBeforeRetry']
        ? 'Outcome unknown. Verify current Unity state before retrying.'
        : result.content?.find(c => c.type === 'text')?.text || 'Unity tool failed');
      error.result = result.structuredContent || result;
      throw error;
    }
    if (result.structuredContent) return result.structuredContent;
    const text = result.content?.find(c => c.type === 'text')?.text;
    try { return JSON.parse(text); } catch { return result; }
  }
  function payload(name, args = {}) {
    const promise = invokePayload(name, args);
    promise.catch(() => {}); // The bridge drain records failures of forgotten awaits.
    return promise;
  }
  return Object.freeze({
    call: payload,
    discover: (name) => payload('unity.discover_tools', { name }),
    scene: Object.freeze({ query: (args = {}) => payload('unity.query_scene', args) }),
    properties: Object.freeze({ read: (objectId, propertyPaths) => payload('unity.get_serialized_properties', { objectId, propertyPaths }) }),
    batch: (operations) => payload('unity.batch', { operations }),
    edit: ({ targets, set, label }) => {
      if (!targets?.editorEpoch || !Array.isArray(targets.objects)) throw new Error('targets must be a query_scene result, including editorEpoch.');
      if (targets.hasMore) throw new Error('Query has more pages. Narrow the query or explicitly pass one page with hasMore=false.');
      const ids = targets.objects.flatMap(o => o.components?.length ? o.components.map(c => c.objectId) : [o.objectId]);
      return payload('unity.edit_objects', { editorEpoch: targets.editorEpoch, targets: ids, set, label });
    },
    view: Object.freeze({ capture: (args = {}) => call('unity.capture_scene_view', args) }),
    compilation: Object.freeze({
      // Server-side polling avoids model round trips. Stable idle is not proof that
      // a particular requested compilation succeeded; returned diagnostics still matter.
      wait: (args = {}) => call('$waitCompilation', args)
    })
  });
}

module.exports = { createUnitySdk };
