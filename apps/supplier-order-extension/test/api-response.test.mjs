import test from 'node:test';
import assert from 'node:assert/strict';
import { isApiSuccess, isAuthFailure, extractData } from '../src/lib/api-response.js';

test('成功判定', () => {
  assert.equal(isApiSuccess({ success: true, data: {} }), true);
  assert.equal(isApiSuccess({ success: false }), false);
  assert.equal(isApiSuccess(null), false);
});

test('鉴权失败：HTTP 401 或业务 errorCode', () => {
  assert.equal(isAuthFailure(null, 401), true);
  assert.equal(isAuthFailure({ success: false, errorCode: 'TOKEN_EXPIRED' }, 200), true);
  assert.equal(isAuthFailure({ success: false, errorCode: 'UNAUTHORIZED' }, 200), true);
  assert.equal(isAuthFailure({ success: false, errorCode: 'VALIDATION' }, 200), false);
  assert.equal(isAuthFailure({ success: true }, 200), false);
  assert.equal(isAuthFailure(null, 200), false);
  assert.equal(isAuthFailure(null, 403), false);
});

test('extractData', () => {
  assert.deepEqual(extractData({ success: true, data: { a: 1 } }), { a: 1 });
  assert.equal(extractData(null), undefined);
});
