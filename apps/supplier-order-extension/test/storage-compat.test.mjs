import test from 'node:test';
import assert from 'node:assert/strict';
import { getPendingLocateChange, matchesStorageArea } from '../src/lib/storage-compat.js';

const pendingLocate = { storeCode: '001', supplierCode: '240', itemNumber: 'ABC' };

test('pendingLocate 接受 session 事件与 Safari 旧版本缺失的 areaName', () => {
  const changes = { pendingLocate: { newValue: pendingLocate } };

  assert.deepEqual(getPendingLocateChange(changes, 'session'), pendingLocate);
  assert.deepEqual(getPendingLocateChange(changes, undefined), pendingLocate);
});

test('pendingLocate 拒绝其他存储区和删除事件', () => {
  assert.equal(getPendingLocateChange({ pendingLocate: { newValue: pendingLocate } }, 'local'), null);
  assert.equal(getPendingLocateChange({ pendingLocate: { newValue: undefined } }, 'session'), null);
  assert.equal(getPendingLocateChange({}, undefined), null);
});

test('Safari 缺失 areaName 时兼容目标存储区，明确的其他区域仍拒绝', () => {
  assert.equal(matchesStorageArea('local', 'local'), true);
  assert.equal(matchesStorageArea(undefined, 'local'), true);
  assert.equal(matchesStorageArea('session', 'local'), false);
});
