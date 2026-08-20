import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeLocale, t } from '../src/lib/i18n.js';

test('normalizeLocale', () => {
  assert.equal(normalizeLocale('en'), 'en');
  assert.equal(normalizeLocale('zh'), 'zh');
  assert.equal(normalizeLocale('fr'), 'zh');
  assert.equal(normalizeLocale(null), 'zh');
});

test('t 语言区分与回退', () => {
  assert.equal(typeof t('zh', 'login'), 'string');
  assert.equal(typeof t('en', 'login'), 'string');
  assert.notEqual(t('zh', 'login'), t('en', 'login'));
  assert.equal(t('zh', 'missing.key.xyz'), 'missing.key.xyz');
});

test('后端接口设置提供完整中英文文案', () => {
  for (const key of [
    'apiTitle',
    'apiRemote',
    'apiLocal',
    'apiApply',
    'apiHint',
    'apiSaved',
    'apiSwitched',
    'apiInvalid',
    'apiPermissionDenied',
  ]) {
    assert.equal(typeof t('zh', key), 'string', `zh.${key}`);
    assert.equal(typeof t('en', key), 'string', `en.${key}`);
    assert.notEqual(t('zh', key), key, `zh.${key}`);
    assert.notEqual(t('en', key), key, `en.${key}`);
  }
});

test('供应商折叠控件提供完整中英文文案', () => {
  for (const key of ['supplierExpand', 'supplierCollapse', 'supplierCollapsedHint']) {
    assert.equal(typeof t('zh', key), 'string', `zh.${key}`);
    assert.equal(typeof t('en', key), 'string', `en.${key}`);
    assert.notEqual(t('zh', key), key, `zh.${key}`);
    assert.notEqual(t('en', key), key, `en.${key}`);
  }
});
