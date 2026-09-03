import test from 'node:test';
import assert from 'node:assert/strict';

import {
  beginRankingLoad,
  buildProductImageCandidates,
  formatSalesRankBand,
  formatAverageSellingPrice,
  normalizeRankingDays,
  normalizeRankingPageSize,
  normalizeSalesRankBand,
  resolveRankingRetryTarget,
  resolveRankingViewState,
  restoreRankingLoad,
  normalizeSupplierOptions,
  normalizeStoreOptions,
  normalizeTopSalesPage,
  normalizeTopSalesRequest,
  paginateRanking,
  shouldPreserveManualSupplier,
  transitionRankingPagination,
} from '../src/lib/ranking.js';

function expectedBand(rank, totalProductCount) {
  if (rank <= Math.ceil(totalProductCount * 0.1)) return 'top-10';
  if (rank <= Math.ceil(totalProductCount * 0.2)) return 'top-20';
  return 'top-30';
}

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

test('排名每页条数只接受 50、100、200，其他值回退 50', () => {
  assert.equal(normalizeRankingPageSize(undefined), 50);
  assert.equal(normalizeRankingPageSize(50), 50);
  assert.equal(normalizeRankingPageSize('100'), 100);
  assert.equal(normalizeRankingPageSize(200), 200);
  assert.equal(normalizeRankingPageSize(20), 50);
  assert.equal(normalizeRankingPageSize(-1), 50);
});

test('销量档位只接受互斥的 TOP 10/20/30 并生成稳定标签', () => {
  assert.equal(normalizeSalesRankBand('top-10'), 'top-10');
  assert.equal(normalizeSalesRankBand(' TOP-20 '), 'top-20');
  assert.equal(normalizeSalesRankBand('top-30'), 'top-30');
  assert.equal(normalizeSalesRankBand('top-40'), null);
  assert.equal(normalizeSalesRankBand(null), null);
  assert.equal(formatSalesRankBand('top-10'), 'TOP 10%');
  assert.equal(formatSalesRankBand('top-20'), 'TOP 20%');
  assert.equal(formatSalesRankBand('top-30'), 'TOP 30%');
  assert.equal(formatSalesRankBand(null), '');
});

test('分页状态切换供应商、周期或每页条数时回到第一页', () => {
  const current = { page: 4, pageSize: 100 };
  assert.deepEqual(transitionRankingPagination(current, { type: 'context' }), {
    page: 1,
    pageSize: 100,
  });
  assert.deepEqual(transitionRankingPagination(current, { type: 'page-size', pageSize: 200 }), {
    page: 1,
    pageSize: 200,
  });
  assert.deepEqual(transitionRankingPagination(current, { type: 'page', page: 3 }), {
    page: 3,
    pageSize: 100,
  });
});

test('热销榜 loading、empty、error 与内容状态互斥', () => {
  assert.equal(resolveRankingViewState({ hasSupplier: false }), 'no-supplier');
  assert.equal(resolveRankingViewState({ hasSupplier: true, loading: true, error: 'x', totalRankedCount: 0 }), 'loading');
  assert.equal(resolveRankingViewState({ hasSupplier: true, error: 'x', totalRankedCount: 0 }), 'error');
  assert.equal(resolveRankingViewState({ hasSupplier: true, totalRankedCount: 0 }), 'empty');
  assert.equal(resolveRankingViewState({ hasSupplier: true, totalRankedCount: 3 }), 'content');
  assert.equal(resolveRankingViewState({ hasSupplier: true }), 'idle');
});

test('TOP 30 新响应直接使用服务端页和全局名次', () => {
  const items = Array.from({ length: 50 }, (_, index) => ({
    rank: index + 51,
    productCode: `P-${index + 51}`,
    salesRankBand: expectedBand(index + 51, 403),
  }));
  assert.deepEqual(
    normalizeTopSalesPage(
      {
        supplierCode: '240',
        days: 60,
        topPercent: 30,
        totalProductCount: 403,
        totalRankedCount: 121,
        page: 2,
        pageSize: 50,
        totalPages: 3,
        items,
      },
      {
        requestedPage: 2,
        requestedPageSize: 50,
        requestedSupplierCode: '240',
        requestedDays: 60,
      },
    ),
    {
      mode: 'server',
      topPercent: 30,
      items,
      totalRankedCount: 121,
      page: 2,
      pageSize: 50,
      totalPages: 3,
    },
  );
});

test('热销榜请求显式区分 legacy 与完整服务端分页参数', () => {
  assert.equal(normalizeTopSalesRequest({}), null);
  assert.deepEqual(
    normalizeTopSalesRequest({ topPercent: 30, page: 2, pageSize: 100 }),
    { topPercent: 30, page: 2, pageSize: 100 },
  );
  assert.throws(
    () => normalizeTopSalesRequest({ topPercent: 10, page: 1, pageSize: 50 }),
    /分页参数/,
  );
  assert.throws(() => normalizeTopSalesRequest({ topPercent: 30 }), /分页参数/);
  assert.throws(
    () => normalizeTopSalesRequest({ topPercent: 30, page: 0, pageSize: 50 }),
    /分页参数/,
  );
  assert.throws(
    () => normalizeTopSalesRequest({ topPercent: 30, page: 1, pageSize: 75 }),
    /分页参数/,
  );
});

test('旧后端完整 TOP 10 响应动态降级并在前端分页', () => {
  const items = Array.from({ length: 121 }, (_, index) => ({
    rank: index + 1,
    salesRankBand: index % 2 === 0 ? 'top-10' : undefined,
  }));
  const result = normalizeTopSalesPage(
    {
      supplierCode: '240',
      days: 90,
      topPercent: 10,
      totalProductCount: 1201,
      totalRankedCount: 121,
      items,
    },
    {
      requestedPage: 2,
      requestedPageSize: 50,
      requestedSupplierCode: '240',
      requestedDays: 90,
    },
  );

  assert.equal(result.mode, 'legacy');
  assert.equal(result.topPercent, 10);
  assert.equal(result.totalRankedCount, 121);
  assert.equal(result.page, 2);
  assert.equal(result.pageSize, 50);
  assert.equal(result.totalPages, 3);
  assert.deepEqual(result.items, items.slice(50, 100));
  assert.throws(
    () => normalizeTopSalesPage(
      {
        topPercent: 30,
        totalProductCount: 403,
        totalRankedCount: 121,
        page: null,
        pageSize: null,
        totalPages: null,
        items,
      },
      { requestedPage: 1, requestedPageSize: 50 },
    ),
    /分页响应/,
  );
});

test('TOP 30 新协议空结果仅接受第 1 页与 0 总页数', () => {
  assert.deepEqual(
    normalizeTopSalesPage({
      supplierCode: '240',
      days: 60,
      topPercent: 30,
      totalProductCount: 0,
      totalRankedCount: 0,
      page: 1,
      pageSize: 100,
      totalPages: 0,
      items: [],
    }, {
      requestedPage: 99,
      requestedPageSize: 100,
      requestedSupplierCode: '240',
      requestedDays: 60,
    }),
    {
      mode: 'server',
      topPercent: 30,
      totalRankedCount: 0,
      page: 1,
      pageSize: 100,
      totalPages: 0,
      items: [],
    },
  );

  for (const response of [
    { topPercent: 30, totalProductCount: 0, totalRankedCount: 0, page: 2, pageSize: 100, totalPages: 0, items: [] },
    { topPercent: 30, totalProductCount: 0, totalRankedCount: 0, page: 1, pageSize: 100, totalPages: 1, items: [] },
    { topPercent: 30, totalProductCount: 0, totalRankedCount: 0, page: 1, pageSize: 100, totalPages: 0, items: [{ rank: 1, salesRankBand: 'top-10' }] },
  ]) {
    assert.throws(
      () => normalizeTopSalesPage(response, { requestedPage: 1, requestedPageSize: 100 }),
      /分页响应/,
    );
  }
});

test('TOP 30 新协议严格拒绝缺失、矛盾或可疑猜测的字段', () => {
  const validItems = Array.from({ length: 21 }, (_, index) => ({
    rank: index + 101,
    salesRankBand: 'top-30',
  }));
  const valid = {
    supplierCode: '240',
    days: 60,
    topPercent: 30,
    totalProductCount: 403,
    totalRankedCount: 121,
    page: 3,
    pageSize: 50,
    totalPages: 3,
    items: validItems,
  };
  assert.equal(
    normalizeTopSalesPage(valid, {
      requestedPage: 99,
      requestedPageSize: 50,
      requestedSupplierCode: '240',
      requestedDays: 60,
    }).page,
    3,
  );

  for (const response of [
    { ...valid, totalRankedCount: undefined },
    { ...valid, totalProductCount: undefined },
    { ...valid, totalProductCount: 1 },
    { ...valid, supplierCode: '225' },
    { ...valid, days: 90 },
    { ...valid, total: 121, totalRankedCount: undefined },
    { ...valid, totalPages: undefined },
    { ...valid, totalPages: 4 },
    { ...valid, pageSize: 100 },
    { ...valid, page: 2 },
    { ...valid, items: validItems.slice(0, 20) },
    { ...valid, items: validItems.map((item, index) => ({ ...item, rank: index + 1 })) },
    { ...valid, items: validItems.map((item) => ({ ...item, salesRankBand: null })) },
    { ...valid, items: validItems.map((item) => ({ ...item, salesRankBand: 'TOP-30' })) },
  ]) {
    assert.throws(
      () => normalizeTopSalesPage(response, {
        requestedPage: 99,
        requestedPageSize: 50,
        requestedSupplierCode: '240',
        requestedDays: 60,
      }),
      /分页响应/,
    );
  }

  const bandMismatch = {
    supplierCode: '240',
    days: 60,
    topPercent: 30,
    totalProductCount: 10,
    totalRankedCount: 3,
    page: 1,
    pageSize: 50,
    totalPages: 1,
    items: [
      { rank: 1, salesRankBand: 'top-30' },
      { rank: 2, salesRankBand: 'top-10' },
      { rank: 3, salesRankBand: 'top-30' },
    ],
  };
  assert.throws(
    () => normalizeTopSalesPage(bandMismatch, {
      requestedPage: 1,
      requestedPageSize: 50,
      requestedSupplierCode: '240',
      requestedDays: 60,
    }),
    /分页响应/,
  );

  assert.throws(
    () => normalizeTopSalesPage({
      supplierCode: '240',
      days: 60,
      topPercent: 10,
      totalProductCount: 10,
      totalRankedCount: 1,
      items: [{ rank: 1, salesRankBand: 'top-30' }],
    }, {
      requestedPage: 1,
      requestedPageSize: 50,
      requestedSupplierCode: '240',
      requestedDays: 60,
    }),
    /分页响应/,
  );
});

test('普通翻页保留旧页，pageSize 上下文切换则清空旧分页', () => {
  const previousData = {
    topPercent: 30,
    totalProductCount: 403,
    totalRankedCount: 121,
    page: 2,
    pageSize: 50,
    totalPages: 3,
    items: [{ rank: 51, salesRankBand: 'top-20' }],
  };
  const started = beginRankingLoad({
    page: 3,
    pageSize: 50,
    data: previousData,
    legacyItems: null,
  }, { clear: false });

  assert.equal(started.state.data, previousData);
  assert.equal(started.state.loading, true);
  assert.deepEqual(
    { page: started.checkpoint.page, pageSize: started.checkpoint.pageSize },
    { page: 2, pageSize: 50 },
  );

  const restored = restoreRankingLoad(started.checkpoint, '网络错误');
  assert.equal(restored.data, previousData);
  assert.equal(restored.page, 2);
  assert.equal(restored.pageSize, 50);
  assert.equal(restored.loading, false);
  assert.equal(restored.error, '网络错误');

  const contextLoad = beginRankingLoad({
    page: 1,
    pageSize: 100,
    data: previousData,
    legacyItems: null,
  }, { clear: true });
  assert.equal(contextLoad.state.data, null);
  assert.equal(contextLoad.state.page, 1);
  assert.equal(contextLoad.state.pageSize, 100);
});

test('普通翻页失败后仅在同一供应商和周期重试失败目标', () => {
  const failedTarget = {
    supplierCode: '240',
    days: 90,
    page: 3,
    pageSize: 100,
  };
  assert.deepEqual(
    resolveRankingRetryTarget(failedTarget, { supplierCode: '240', days: 90 }),
    { page: 3, pageSize: 100 },
  );
  assert.equal(
    resolveRankingRetryTarget(failedTarget, { supplierCode: '225', days: 90 }),
    null,
  );
  assert.equal(
    resolveRankingRetryTarget(failedTarget, { supplierCode: '240', days: 60 }),
    null,
  );
  assert.equal(
    resolveRankingRetryTarget({ ...failedTarget, pageSize: 75 }, { supplierCode: '240', days: 90 }),
    null,
  );
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
