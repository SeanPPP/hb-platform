import test from 'node:test';
import assert from 'node:assert/strict';
import { createAuthExecutor } from '../src/lib/refresh-flow.js';

test('非鉴权失败不触发刷新', async () => {
  let refreshCalls = 0;
  const ex = createAuthExecutor({
    isAuthFailure: (r) => r.authFail,
    refresh: async () => {
      refreshCalls++;
    },
  });
  const r = await ex.withRefresh(async () => ({ authFail: false, data: 'ok' }));
  assert.equal(r.data, 'ok');
  assert.equal(refreshCalls, 0);
});

test('鉴权失败单次刷新后重试一次', async () => {
  let refreshCalls = 0;
  let calls = 0;
  const ex = createAuthExecutor({
    isAuthFailure: (r) => r.authFail,
    refresh: async () => {
      refreshCalls++;
    },
  });
  const req = async () => {
    calls++;
    return calls === 1 ? { authFail: true } : { authFail: false, data: 'retried' };
  };
  const r = await ex.withRefresh(req);
  assert.equal(r.data, 'retried');
  assert.equal(refreshCalls, 1);
});

test('刷新后会重新执行请求闭包并读取旋转后的凭据', async () => {
  let credential = 'old';
  const observed = [];
  const ex = createAuthExecutor({
    isAuthFailure: (r) => r.authFail,
    refresh: async () => {
      credential = 'rotated';
    },
  });

  const result = await ex.withRefresh(async () => {
    observed.push(credential);
    return credential === 'old'
      ? { authFail: true }
      : { authFail: false, credential };
  });

  assert.deepEqual(observed, ['old', 'rotated']);
  assert.equal(result.credential, 'rotated');
});

test('并发请求 single-flight：仅一次刷新', async () => {
  let refreshCalls = 0;
  let calls = 0;
  const ex = createAuthExecutor({
    isAuthFailure: (r) => r.authFail,
    refresh: async () => {
      refreshCalls++;
      await new Promise((r) => setTimeout(r, 5));
    },
  });
  const req = async () => {
    calls++;
    return calls <= 2 ? { authFail: true } : { authFail: false, data: 'ok' };
  };
  const [a, b] = await Promise.all([ex.withRefresh(req), ex.withRefresh(req)]);
  assert.equal(a.data, 'ok');
  assert.equal(b.data, 'ok');
  assert.equal(refreshCalls, 1);
});

test('刷新失败则请求失败', async () => {
  const ex = createAuthExecutor({
    isAuthFailure: (r) => r.authFail,
    refresh: async () => {
      throw new Error('refresh failed');
    },
  });
  await assert.rejects(ex.withRefresh(async () => ({ authFail: true })), /refresh failed/);
});
