import test from 'node:test';
import assert from 'node:assert/strict';
import { selectVisibleSupplierEntries } from '../src/lib/supplier-list.js';

const entries = [
  { pattern: 'https://granted-a.example/*', granted: true },
  { pattern: 'https://pending.example/*', granted: false },
  { pattern: 'https://granted-b.example/*', granted: true },
];

test('供应商列表默认隐藏已授权项，但始终显示未授权项', () => {
  const result = selectVisibleSupplierEntries(entries, false);

  assert.deepEqual(result.visibleEntries, [entries[1]]);
  assert.equal(result.grantedCount, 2);
  assert.equal(result.hiddenGrantedCount, 2);
});

test('展开供应商列表后显示全部项', () => {
  const result = selectVisibleSupplierEntries(entries, true);

  assert.deepEqual(result.visibleEntries, entries);
  assert.equal(result.grantedCount, 2);
  assert.equal(result.hiddenGrantedCount, 0);
});
