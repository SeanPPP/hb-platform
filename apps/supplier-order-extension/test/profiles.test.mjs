import test from 'node:test';
import assert from 'node:assert/strict';
import {
  validateProfiles,
  matchProfile,
  originMatchesAny,
  matchesListPage,
} from '../src/lib/profiles.js';
import { DEFAULT_PROFILES } from '../src/lib/profiles-default.js';

const validProfile = {
  supplierCode: 'DATS',
  displayName: 'DATS',
  enabled: true,
  origins: ['https://www.dats.com.au/*'],
  listPagePatterns: [],
  cardSelector: '.product[data-product-code]',
  itemNumber: {
    source: 'attribute',
    selector: null,
    attribute: 'data-product-code',
    transforms: ['trim', 'uppercase'],
  },
  mountSelector: '.widget-productlist-code',
  mountPosition: 'afterend',
};

test('validateProfiles 接受合法 profile', () => {
  const r = validateProfiles({ configVersion: 1, profiles: [validProfile] });
  assert.equal(r.valid, true);
  assert.equal(r.profiles.length, 1);
  assert.equal(r.errors.length, 0);
});

test('服务端可返回空 profile 列表以后台停用全部供应商', () => {
  const r = validateProfiles({ configVersion: '2', profiles: [] });
  assert.equal(r.valid, true);
  assert.deepEqual(r.profiles, []);
});

test('validateProfiles 拒绝非法 transform（不允许 eval）', () => {
  const p = {
    ...validProfile,
    itemNumber: { ...validProfile.itemNumber, transforms: [{ type: 'eval' }] },
  };
  const r = validateProfiles({ profiles: [p] });
  assert.equal(r.valid, false);
  assert.ok(r.errors.some((e) => /transforms/.test(e)));
});

test('validateProfiles 拒绝非法 source / mountPosition / 缺失字段', () => {
  assert.equal(validateProfiles({ profiles: [{ ...validProfile, itemNumber: { source: 'css' } }] }).valid, false);
  assert.equal(validateProfiles({ profiles: [{ ...validProfile, mountPosition: 'inside' }] }).valid, false);
  assert.equal(validateProfiles({ profiles: [{ ...validProfile, supplierCode: '' }] }).valid, false);
  assert.equal(validateProfiles(null).valid, false);
  assert.equal(validateProfiles({}).valid, false);
  assert.equal(
    validateProfiles({ profiles: [{ ...validProfile, origins: ['javascript:alert(1)'] }] }).valid,
    false,
  );
});

test('仅允许 TXK 的精确 HTTP 站点，拒绝其他明文 HTTP 配置', () => {
  const txk = {
    ...validProfile,
    supplierCode: 'SP2502280001',
    displayName: 'TXK',
    origins: ['http://txkorders.inzantsales.com/*'],
    listPagePatterns: ['http://txkorders.inzantsales.com/shop*'],
    cardSelector: '.single-product.grid-view',
    itemNumber: {
      source: 'text',
      selector: '.sku',
      attribute: null,
      transforms: ['after-sku', 'trim', 'uppercase'],
    },
    mountSelector: '.price-box',
  };

  assert.equal(validateProfiles({ profiles: [txk] }).valid, true);
  assert.equal(
    validateProfiles({
      profiles: [{ ...txk, origins: ['http://example.com/*'] }],
    }).valid,
    false,
  );
  for (const unsafeOrigin of [
    'http://txkorders.inzantsales.com.evil.example/*',
    'http://txkorders.inzantsales.com:8080/*',
    'http://user@txkorders.inzantsales.com/*',
  ]) {
    assert.equal(
      validateProfiles({ profiles: [{ ...txk, origins: [unsafeOrigin] }] }).valid,
      false,
      `必须拒绝 ${unsafeOrigin}`,
    );
  }
  assert.equal(originMatchesAny(txk.origins, 'http://txkorders.inzantsales.com'), true);
  assert.equal(originMatchesAny(txk.origins, 'http://evil.example.com'), false);
});

test('内置 DATS profile 通过校验', () => {
  const r = validateProfiles(DEFAULT_PROFILES);
  assert.equal(r.valid, true);
  assert.equal(DEFAULT_PROFILES.configVersion, '2');
  assert.equal(r.profiles[0].supplierCode, '240');
  assert.equal(r.profiles[0].displayName, 'DATS');
  assert.equal(r.profiles[0].mountPosition, 'afterend');
});

test('originMatchesAny 与 matchesListPage', () => {
  assert.equal(originMatchesAny(['https://www.dats.com.au/*'], 'https://www.dats.com.au'), true);
  assert.equal(originMatchesAny(['https://www.dats.com.au/*'], 'https://evil.com'), false);
  assert.equal(
    matchesListPage(['https://www.dats.com.au/*'], 'https://www.dats.com.au/filing-notebooks-and-paper'),
    true,
  );
  assert.equal(matchesListPage(['/search/*'], 'https://example.com/search/123'), true);
  assert.equal(matchesListPage(['/search/*'], 'https://example.com/cart'), false);
  assert.equal(matchesListPage([], 'https://example.com/anything'), false);
});

test('matchProfile 按 origin 匹配且跳过 disabled', () => {
  const profiles = [
    validProfile,
    { ...validProfile, supplierCode: 'X', enabled: false, origins: ['https://x.com/*'] },
  ];
  assert.equal(matchProfile(profiles, { origin: 'https://www.dats.com.au', pathname: '/' }).supplierCode, 'DATS');
  assert.equal(matchProfile(profiles, { origin: 'https://x.com', pathname: '/' }), null);
  assert.equal(matchProfile(profiles, { origin: 'https://other.com', pathname: '/' }), null);
});
