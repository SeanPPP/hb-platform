import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeLocale, resolveInitialLocale, t } from '../src/lib/i18n.js';

test('normalizeLocale', () => {
  assert.equal(normalizeLocale('en'), 'en');
  assert.equal(normalizeLocale('zh'), 'zh');
  assert.equal(normalizeLocale('fr'), 'zh');
  assert.equal(normalizeLocale(null), 'zh');
});

test('首次打开按系统语言选择中文或英文', () => {
  for (const systemLocale of ['zh', 'zh-CN', 'zh-TW', 'zh-Hans', 'zh-Hant']) {
    assert.equal(resolveInitialLocale(null, [systemLocale]), 'zh', systemLocale);
  }

  assert.equal(resolveInitialLocale(undefined, ['en-AU']), 'en');
  assert.equal(resolveInitialLocale(undefined, ['fr-FR']), 'en');
  assert.equal(resolveInitialLocale(undefined, []), 'en');
});

test('已保存语言优先，非法保存值回退系统语言', () => {
  assert.equal(resolveInitialLocale('zh', ['en-AU']), 'zh');
  assert.equal(resolveInitialLocale('en', ['zh-CN']), 'en');
  assert.equal(resolveInitialLocale('fr', ['zh-TW']), 'zh');
  assert.equal(resolveInitialLocale('fr', ['de-DE']), 'en');
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
