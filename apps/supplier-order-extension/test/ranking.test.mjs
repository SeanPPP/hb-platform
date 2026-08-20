import test from 'node:test';
import assert from 'node:assert/strict';

import {
  buildProductImageCandidates,
  formatAverageSellingPrice,
  normalizeRankingDays,
  normalizeSupplierOptions,
  normalizeStoreOptions,
  paginateRanking,
  shouldPreserveManualSupplier,
} from '../src/lib/ranking.js';

test('normalizeStoreOptions 仅保留后端返回的有效门店并去重', () => {
  const stores = normalizeStoreOptions({
    stores: [
      { storeCode: '1014', storeName: 'Kawana' },
      { storeCode: '1014', storeName: '重复门店' },
      { storeCode: '', storeName: '无代码' },
      null,
    ],
  });

  assert.deepEqual(stores, [{ code: '1014', name: 'Kawana' }]);
});

test('normalizeRankingDays 默认 60 天且最大只切换到 90 天', () => {
  assert.equal(normalizeRankingDays(undefined), 60);
  assert.equal(normalizeRankingDays(60), 60);
  assert.equal(normalizeRankingDays(90), 90);
  assert.equal(normalizeRankingDays(120), 60);
});

test('buildProductImageCandidates 优先商品图并提供货号默认图', () => {
  assert.deepEqual(
    buildProductImageCandidates(
      {
        imageUrl: '/images/item.jpg',
        itemNumber: 'FAR-0026',
        productCode: 'P-1',
      },
      'http://localhost:5002',
    ),
    [
      'http://localhost:5002/images/item.jpg',
      'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/FAR-0026.jpg',
      'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/P-1.jpg',
    ],
  );
});

test('normalizeSupplierOptions 按供应商代码去重并保留显示名称', () => {
  assert.deepEqual(
    normalizeSupplierOptions([
      { supplierCode: '201', displayName: 'Yatsal' },
      { supplierCode: '201', displayName: '重复域名' },
      { supplierCode: '225', displayName: 'MNB' },
      { supplierCode: '', displayName: '无代码' },
    ]),
    [
      { code: '201', name: 'Yatsal' },
      { code: '225', name: 'MNB' },
    ],
  );
});

test('paginateRanking 默认每页 50 条并校正页码边界', () => {
  const items = Array.from({ length: 121 }, (_, index) => ({ rank: index + 1 }));

  assert.deepEqual(paginateRanking(items, 1), {
    items: items.slice(0, 50),
    page: 1,
    totalPages: 3,
    totalItems: 121,
    pageSize: 50,
  });
  assert.deepEqual(paginateRanking(items, 99).items, items.slice(100));
  assert.equal(paginateRanking([], 1).totalPages, 1);
});

test('formatAverageSellingPrice 使用澳元格式且空值显示占位', () => {
  assert.equal(formatAverageSellingPrice(2.5, 'en'), '$2.50');
  assert.equal(formatAverageSellingPrice(2.5, 'zh'), '$2.50');
  assert.equal(formatAverageSellingPrice(null, 'en'), '—');
  assert.equal(formatAverageSellingPrice('invalid', 'en'), '—');
});

test('手动供应商在同一网页刷新时保持，切换到其他受支持供应商时恢复自动跟随', () => {
  assert.equal(
    shouldPreserveManualSupplier({
      manualSupplierCode: '225',
      detectedSupplierCode: '240',
      previousDetectedSupplierCode: '240',
    }),
    true,
  );
  assert.equal(
    shouldPreserveManualSupplier({
      manualSupplierCode: '225',
      detectedSupplierCode: '243',
      previousDetectedSupplierCode: '240',
    }),
    false,
  );
  assert.equal(
    shouldPreserveManualSupplier({
      manualSupplierCode: '225',
      detectedSupplierCode: null,
      previousDetectedSupplierCode: '240',
    }),
    true,
  );
});
