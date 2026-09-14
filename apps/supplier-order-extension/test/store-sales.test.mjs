import test from 'node:test';
import assert from 'node:assert/strict';
import {
  filterStoreSales,
  normalizeStoreSalesResponse,
} from '../src/lib/store-sales.js';

const response = {
  supplierCode: '240',
  productCode: 'P-1',
  days: 60,
  startDate: '2026-07-16',
  endDate: '2026-09-13',
  snapshotVersion: 'snapshot-v1',
  enabledStoreCount: 3,
  totalSalesQuantity: 13,
  stores: [
    { storeCode: 'S-1', storeName: 'Bankstown', salesQuantity: 1 },
    { storeCode: 'S-3', storeName: 'Springfield', salesQuantity: 0 },
    { storeCode: 'S-2', storeName: 'Campbelltown', salesQuantity: 12 },
  ],
};

test('商品分店销量响应校验身份、周期、门店数与合计，并按销量降序', () => {
  const result = normalizeStoreSalesResponse(response, {
    supplierCode: '240',
    productCode: 'P-1',
    days: 60,
    startDate: '2026-07-16',
    endDate: '2026-09-13',
    snapshotVersion: 'snapshot-v1',
    totalSalesQuantity: 13,
  });

  assert.equal(result.totalSalesQuantity, 13);
  assert.deepEqual(
    result.stores.map((store) => [store.storeCode, store.salesQuantity]),
    [['S-2', 12], ['S-1', 1], ['S-3', 0]],
  );
});

test('商品分店销量响应拒绝串单、周期漂移、重复门店与不一致合计', () => {
  for (const invalid of [
    { ...response, supplierCode: '225' },
    { ...response, productCode: 'P-2' },
    { ...response, days: 90 },
    { ...response, startDate: '2026-07-17' },
    { ...response, endDate: '2026-09-12' },
    { ...response, snapshotVersion: 'snapshot-v2' },
    { ...response, totalSalesQuantity: 99 },
    { ...response, enabledStoreCount: 2 },
    { ...response, stores: [...response.stores, response.stores[0]] },
  ]) {
    assert.throws(
      () => normalizeStoreSalesResponse(invalid, {
        supplierCode: '240',
        productCode: 'P-1',
        days: 60,
        startDate: '2026-07-16',
        endDate: '2026-09-13',
        snapshotVersion: 'snapshot-v1',
        totalSalesQuantity: 13,
      }),
      /分店销量响应/,
    );
  }
});

test('分店搜索同时匹配名称和编码且忽略大小写与首尾空格', () => {
  assert.deepEqual(
    filterStoreSales(response.stores, ' bank ').map((store) => store.storeCode),
    ['S-1'],
  );
  assert.deepEqual(
    filterStoreSales(response.stores, 's-2').map((store) => store.storeCode),
    ['S-2'],
  );
  assert.equal(filterStoreSales(response.stores, 'missing').length, 0);
});
