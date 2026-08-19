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
