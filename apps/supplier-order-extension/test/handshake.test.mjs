import test from 'node:test';
import assert from 'node:assert/strict';
import {
  EXTENSION_MESSAGE_SOURCE,
  OPEN_MESSAGE_TYPE,
  OPEN_RESULT_MESSAGE_TYPE,
  PING_MESSAGE_TYPE,
  PLATFORM_MESSAGE_SOURCE,
  STATUS_MESSAGE_TYPE,
  isValidNonce,
  generateNonce,
  detectBrowser,
  validateBridgeMessage,
  buildBridgeResponse,
} from '../src/lib/handshake.js';

test('nonce 格式校验', () => {
  assert.equal(isValidNonce('a'.repeat(32)), true);
  assert.equal(isValidNonce('A1-_bC'.repeat(5)), true);
  assert.equal(isValidNonce('short'), false);
  assert.equal(isValidNonce('bad nonce!'), false);
  assert.equal(isValidNonce(null), false);
  assert.equal(isValidNonce(123), false);
});

test('generateNonce 生成合法且不重复 nonce', () => {
  const n = generateNonce();
  assert.equal(isValidNonce(n), true);
  assert.notEqual(n, generateNonce());
});

test('detectBrowser UA 识别', () => {
  assert.equal(detectBrowser('Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120.0 Edg/120.0'), 'edge');
  assert.equal(detectBrowser('Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120.0 Safari/537.36'), 'chrome');
  assert.equal(detectBrowser('Mozilla/5.0 (Macintosh) Safari/537.36'), 'unknown');
});

test('bridge 消息校验', () => {
  const win = {};
  const base = {
    source: win,
    sourceWindow: win,
    origin: 'https://hotbargain.vip',
    expectedOrigin: 'https://hotbargain.vip',
    message: {
      source: PLATFORM_MESSAGE_SOURCE,
      type: PING_MESSAGE_TYPE,
      nonce: 'a'.repeat(32),
    },
  };
  assert.equal(validateBridgeMessage(base).ok, true);
  assert.equal(validateBridgeMessage({ ...base, source: {} }).ok, false);
  assert.equal(validateBridgeMessage({ ...base, origin: 'https://evil.com' }).ok, false);
  assert.equal(validateBridgeMessage({ ...base, message: { ...base.message, nonce: 'bad' } }).ok, false);
  assert.equal(validateBridgeMessage({ ...base, message: { ...base.message, source: 'evil' } }).ok, false);
  assert.equal(validateBridgeMessage({ ...base, message: { ...base.message, type: 'STEAL' } }).ok, false);
  assert.equal(validateBridgeMessage({ ...base, message: null }).ok, false);
});

test('bridge 响应仅含允许字段，绝不包含账户/令牌/销售数据', () => {
  const r = buildBridgeResponse({
    kind: 'STATUS',
    nonce: 'a'.repeat(32),
    version: '1.0.0',
    browser: 'chrome',
    installed: true,
  });
  assert.deepEqual(Object.keys(r).sort(), ['browser', 'installed', 'nonce', 'source', 'type', 'version']);
  assert.equal(r.source, EXTENSION_MESSAGE_SOURCE);
  assert.equal(r.type, STATUS_MESSAGE_TYPE);

  const o = buildBridgeResponse({
    kind: 'OPEN_RESULT',
    nonce: 'a'.repeat(32),
    version: '1.0.0',
    browser: 'edge',
    ok: false,
    error: 'blocked',
  });
  assert.equal(o.type, OPEN_RESULT_MESSAGE_TYPE);
  assert.equal(o.ok, false);
  assert.equal(o.error, 'blocked');
  assert.ok(!('accessToken' in o));
  assert.ok(!('refreshToken' in o));
  assert.ok(!('data' in o));
});

test('bridge 常量与 HB /shop 页面契约完全一致', () => {
  assert.equal(PLATFORM_MESSAGE_SOURCE, 'hb-platform');
  assert.equal(EXTENSION_MESSAGE_SOURCE, 'hb-supplier-ordering-extension');
  assert.equal(PING_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_PING');
  assert.equal(OPEN_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_OPEN');
  assert.equal(STATUS_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_STATUS');
  assert.equal(OPEN_RESULT_MESSAGE_TYPE, 'HB_SUPPLIER_ASSISTANT_OPEN_RESULT');
});
