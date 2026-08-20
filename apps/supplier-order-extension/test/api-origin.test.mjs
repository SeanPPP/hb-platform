import test from 'node:test';
import assert from 'node:assert/strict';
import {
  LOCAL_API_ORIGIN,
  normalizeApiOrigin,
  resolveApiOrigin,
  toApiHostPattern,
} from '../src/lib/api-origin.js';

const REMOTE_ORIGIN = 'https://hotbargain.vip';

test('normalizeApiOrigin 将空值和 / 解析为构建时远端地址', () => {
  assert.equal(normalizeApiOrigin('', REMOTE_ORIGIN), REMOTE_ORIGIN);
  assert.equal(normalizeApiOrigin('/', REMOTE_ORIGIN), REMOTE_ORIGIN);
  assert.equal(normalizeApiOrigin('  /  ', REMOTE_ORIGIN), REMOTE_ORIGIN);
});

test('normalizeApiOrigin 接受 HTTPS 和本机 HTTP origin', () => {
  assert.equal(normalizeApiOrigin('https://api.example.com/', REMOTE_ORIGIN), 'https://api.example.com');
  assert.equal(normalizeApiOrigin('http://localhost:5002', REMOTE_ORIGIN), LOCAL_API_ORIGIN);
  assert.equal(normalizeApiOrigin('http://127.0.0.1:5002/', REMOTE_ORIGIN), 'http://127.0.0.1:5002');
});

test('normalizeApiOrigin 拒绝非本机 HTTP、路径、查询和凭据', () => {
  for (const value of [
    'http://api.example.com',
    'https://api.example.com/v1',
    'https://api.example.com?x=1',
    'https://api.example.com/#x',
    'https://user:password@api.example.com',
    'not-a-url',
  ]) {
    assert.equal(normalizeApiOrigin(value, REMOTE_ORIGIN), null, value);
  }
});

test('resolveApiOrigin 对非法存储值安全回退远端地址', () => {
  assert.equal(resolveApiOrigin('https://staging.example.com', REMOTE_ORIGIN), 'https://staging.example.com');
  assert.equal(resolveApiOrigin('javascript:alert(1)', REMOTE_ORIGIN), REMOTE_ORIGIN);
  assert.equal(resolveApiOrigin(undefined, REMOTE_ORIGIN), REMOTE_ORIGIN);
});

test('toApiHostPattern 生成浏览器 origin 权限模式', () => {
  assert.equal(toApiHostPattern('https://api.example.com'), 'https://api.example.com/*');
  assert.equal(toApiHostPattern(LOCAL_API_ORIGIN), 'http://localhost:5002/*');
});
