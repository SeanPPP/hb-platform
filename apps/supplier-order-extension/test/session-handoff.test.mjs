import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import {
  authorizeWithCookieSessionRefresh,
  coordinateWebsiteOpen,
  createAccessRequestExecutor,
  createPkceMaterial,
  createSingleFlight,
  parseAuthorizeResponse,
  parseTokenResponse,
  validateWebsiteSessionContext,
} from '../src/lib/session-handoff.js';

test('网站 access cookie 过期时只刷新一次并重试授权，并发请求仍保持 single-flight', async () => {
  let authorizeCalls = 0;
  let refreshCalls = 0;
  let releaseRefresh;
  const refreshGate = new Promise((resolve) => { releaseRefresh = resolve; });
  const authorize = createSingleFlight(() => authorizeWithCookieSessionRefresh({
    authorizeRequest: async () => {
      authorizeCalls += 1;
      return authorizeCalls === 1
        ? { httpStatus: 401, httpOk: false }
        : { httpStatus: 200, httpOk: true, body: { success: true } };
    },
    refreshSession: async () => {
      refreshCalls += 1;
      await refreshGate;
      return true;
    },
  }));

  const first = authorize();
  const second = authorize();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(authorizeCalls, 1);
  assert.equal(refreshCalls, 1);
  releaseRefresh();

  assert.deepEqual(await Promise.all([first, second]), [
    { httpStatus: 200, httpOk: true, body: { success: true } },
    { httpStatus: 200, httpOk: true, body: { success: true } },
  ]);
  assert.equal(authorizeCalls, 2);
  assert.equal(refreshCalls, 1);
});

test('网站 refresh cookie 失效时不重复 authorize，也不会把刷新响应当授权结果', async () => {
  let authorizeCalls = 0;
  let refreshCalls = 0;
  const unauthorized = { httpStatus: 401, httpOk: false, body: { success: false } };

  const result = await authorizeWithCookieSessionRefresh({
    authorizeRequest: async () => {
      authorizeCalls += 1;
      return unauthorized;
    },
    refreshSession: async () => {
      refreshCalls += 1;
      return false;
    },
  });

  assert.equal(result, unauthorized);
  assert.equal(authorizeCalls, 1);
  assert.equal(refreshCalls, 1);
});

test('Chrome/Edge 在网络授权前立即请求侧栏，Safari 保持先授权再打开', async () => {
  for (const buildTarget of ['chrome', 'edge']) {
    const calls = [];
    const result = await coordinateWebsiteOpen({
      buildTarget,
      authorize: async () => {
        calls.push('authorize');
        return { ok: true };
      },
      openPanel: async () => {
        calls.push('open');
        return { ok: true };
      },
    });

    assert.deepEqual(calls, ['open', 'authorize']);
    assert.equal(result.openResult.ok, true);
  }

  const safariCalls = [];
  await coordinateWebsiteOpen({
    buildTarget: 'safari',
    authorize: async () => {
      safariCalls.push('authorize');
      return { ok: true };
    },
    openPanel: async () => {
      safariCalls.push('open');
      return { ok: true };
    },
  });
  assert.deepEqual(safariCalls, ['authorize', 'open']);
});

test('PKCE verifier 使用 32 字节随机数并生成 S256 challenge', async () => {
  const bytes = Uint8Array.from({ length: 64 }, (_, index) => index);
  let offset = 0;
  const cryptoImpl = {
    subtle: webcrypto.subtle,
    getRandomValues(target) {
      target.set(bytes.slice(offset, offset + target.length));
      offset += target.length;
      return target;
    },
  };

  const result = await createPkceMaterial({ cryptoImpl });

  assert.match(result.codeVerifier, /^[A-Za-z0-9_-]{43}$/);
  assert.match(result.state, /^[A-Za-z0-9_-]{43}$/);
  const expectedChallenge = createHash('sha256')
    .update(result.codeVerifier)
    .digest('base64url');
  assert.equal(result.codeChallenge, expectedChallenge);
});

test('仅允许受信顶层 HB /shop 且 API 与网页同源', () => {
  assert.deepEqual(
    validateWebsiteSessionContext({
      pageUrl: 'https://hotbargain.vip/shop?category=abc',
      webOrigin: 'https://hotbargain.vip',
      apiOrigin: 'https://hotbargain.vip',
      isTopLevel: true,
    }),
    { ok: true, origin: 'https://hotbargain.vip' },
  );

  for (const input of [
    { pageUrl: 'https://evil.example/shop', webOrigin: 'https://hotbargain.vip', apiOrigin: 'https://hotbargain.vip', isTopLevel: true },
    { pageUrl: 'https://hotbargain.vip/orders', webOrigin: 'https://hotbargain.vip', apiOrigin: 'https://hotbargain.vip', isTopLevel: true },
    { pageUrl: 'https://hotbargain.vip/shop', webOrigin: 'https://hotbargain.vip', apiOrigin: 'http://localhost:5002', isTopLevel: true },
    { pageUrl: 'https://hotbargain.vip/shop', webOrigin: 'https://hotbargain.vip', apiOrigin: 'https://hotbargain.vip', isTopLevel: false },
  ]) {
    assert.equal(validateWebsiteSessionContext(input).ok, false);
  }
});

test('authorize ApiResponse 必须成功、字段完整且 state 完全匹配', () => {
  assert.deepEqual(
    parseAuthorizeResponse({
      httpOk: true,
      body: {
        success: true,
        data: {
          code: 'one-time-code',
          state: 'expected-state',
          expiresAtUtc: '2026-08-30T10:00:00Z',
        },
      },
      expectedState: 'expected-state',
    }),
    {
      ok: true,
      code: 'one-time-code',
      state: 'expected-state',
      expiresAtUtc: '2026-08-30T10:00:00Z',
    },
  );

  assert.equal(parseAuthorizeResponse({
    httpOk: true,
    body: { success: true, data: { code: 'x', state: 'other' } },
    expectedState: 'expected-state',
  }).ok, false);
  assert.equal(parseAuthorizeResponse({
    httpOk: false,
    body: { success: false, message: 'login required' },
    expectedState: 'expected-state',
  }).ok, false);
});

test('token ApiResponse 只接受 access token 与有效到期时间', () => {
  const parsed = parseTokenResponse({
    httpOk: true,
    body: {
      success: true,
      data: {
        accessToken: 'short-lived-token',
        accessTokenExpiry: '2026-08-30T10:05:00Z',
        userGuid: 'user-123',
        username: 'admin',
        fullName: 'HB Admin',
      },
    },
    nowMs: Date.parse('2026-08-30T10:00:00Z'),
  });
  assert.deepEqual(parsed, {
    ok: true,
    accessToken: 'short-lived-token',
    accessTokenExpiry: '2026-08-30T10:05:00Z',
    user: { userGuid: 'user-123', username: 'admin', fullName: 'HB Admin' },
  });

  assert.equal(parseTokenResponse({
    httpOk: true,
    body: { success: true, data: { refreshToken: 'forbidden' } },
  }).ok, false);

  assert.equal(parseTokenResponse({
    httpOk: true,
    body: {
      success: true,
      data: {
        accessToken: 'token-without-minimal-user',
        accessTokenExpiry: '2026-08-30T10:05:00Z',
        username: 'admin',
      },
    },
    nowMs: Date.parse('2026-08-30T10:00:00Z'),
  }).ok, false);

  assert.equal(parseTokenResponse({
    httpOk: true,
    body: {
      success: true,
      data: {
        accessToken: 'already-expired',
        accessTokenExpiry: '2026-08-30T10:00:00Z',
      },
    },
    nowMs: Date.parse('2026-08-30T10:00:01Z'),
  }).ok, false);
});

test('single-flight 将并发授权合并为同一次请求，完成后允许下一次请求', async () => {
  let calls = 0;
  let release;
  const gate = new Promise((resolve) => { release = resolve; });
  const run = createSingleFlight(async () => {
    calls += 1;
    await gate;
    return calls;
  });

  const first = run();
  const second = run();
  assert.equal(calls, 1);
  release();
  assert.deepEqual(await Promise.all([first, second]), [1, 1]);

  assert.equal(await run(), 2);
});

test('受保护请求遇到 401 会清理扩展 access session，且不会自动重试', async () => {
  let calls = 0;
  let clears = 0;
  const execute = createAccessRequestExecutor({
    isAuthFailure: (response) => response.httpStatus === 401,
    clearAccessSession: async () => { clears += 1; },
  });

  const response = await execute(async () => {
    calls += 1;
    return { httpStatus: 401, success: false };
  });

  assert.equal(response.httpStatus, 401);
  assert.equal(calls, 1);
  assert.equal(clears, 1);
});
