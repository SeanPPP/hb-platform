import test from 'node:test';
import assert from 'node:assert/strict';
import { migrateProfileConfig } from '../src/lib/profile-cache.js';

test('旧 DATS 缓存迁移为业务供应商代码 240 且不修改输入', () => {
  const oldConfig = {
    configVersion: '1',
    profiles: [
      {
        supplierCode: 'DATS',
        displayName: 'DATS',
        origins: ['https://www.dats.com.au/*'],
      },
    ],
  };

  const migrated = migrateProfileConfig(oldConfig);

  assert.equal(migrated.profiles[0].supplierCode, '240');
  assert.equal(oldConfig.profiles[0].supplierCode, 'DATS');
});

test('非 DATS origin 的同名供应商和非法缓存保持安全', () => {
  const other = {
    profiles: [{ supplierCode: 'DATS', origins: ['https://example.com/*'] }],
  };
  assert.equal(migrateProfileConfig(other).profiles[0].supplierCode, 'DATS');
  assert.equal(migrateProfileConfig(null), null);
});
