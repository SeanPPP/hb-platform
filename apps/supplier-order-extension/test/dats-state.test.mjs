import test from 'node:test';
import assert from 'node:assert/strict';
import {
  createGenerationGuard,
  createNodeStateRegistry,
  needsProcessing,
  shouldInjectList,
  computeButtonState,
  buildSummaryCacheKey,
  normalizeSummaryItem,
  normalizeSummaryMap,
} from '../src/lib/dats-state.js';

test('generation guard 递增与代次校验', () => {
  const g = createGenerationGuard();
  assert.equal(g.current(), 0);
  assert.equal(g.isCurrent(0), true);
  g.advance();
  assert.equal(g.current(), 1);
  assert.equal(g.isCurrent(0), false);
  assert.equal(g.isCurrent(1), true);
});

test('WeakMap 节点状态注册与复用判断', () => {
  const reg = createNodeStateRegistry();
  const a = {};
  const b = {};
  reg.set(a, { generation: 0 });
  assert.equal(needsProcessing(a, reg, 0), false);
  assert.equal(needsProcessing(a, reg, 1), true);
  assert.equal(needsProcessing(b, reg, 0), true);
  assert.equal(reg.has(a), true);
  reg.delete(a);
  assert.equal(reg.has(a), false);
});

test('shouldInjectList 单一详情容器不注入', () => {
  assert.equal(shouldInjectList({ href: 'https://www.dats.com.au/p/1', listPagePatterns: [], cardCount: 1 }), false);
  assert.equal(shouldInjectList({ href: 'https://www.dats.com.au/list', listPagePatterns: [], cardCount: 2 }), true);
  assert.equal(shouldInjectList({
    href: 'https://www.dats.com.au/filing-notebooks-and-paper',
    listPagePatterns: ['https://www.dats.com.au/*'],
    cardCount: 2,
  }), true);
  assert.equal(shouldInjectList({
    href: 'https://www.dats.com.au/product/1',
    listPagePatterns: ['https://www.dats.com.au/*'],
    cardCount: 4,
    isDetailPage: true,
  }), false);
});

test('computeButtonState 明确短状态', () => {
  assert.deepEqual(computeButtonState(null), { kind: 'none', reason: 'noMatch' });
  assert.deepEqual(computeButtonState({ error: 'x' }), { kind: 'error', reason: 'error' });
  assert.deepEqual(computeButtonState({ hasMatch: false }), { kind: 'none', reason: 'noMatch' });
  assert.deepEqual(
    computeButtonState({
      hasMatch: true,
      hasPurchase: false,
      salesRankBand: 'top-20',
      salesRankingDays: 90,
    }),
    { kind: 'none', reason: 'noPurchase', salesRankBand: 'top-20', salesRankingDays: 90 },
  );
  assert.deepEqual(
    computeButtonState({
      hasMatch: true,
      hasPurchase: true,
      lastOrderDate: '2026-08-01',
      lastOrderQuantity: 10,
      salesToDate: 42,
      salesRankBand: 'top-10',
      salesRankingDays: 60,
    }),
    {
      kind: 'ok',
      lastOrderDate: '2026-08-01',
      lastOrderQuantity: 10,
      salesToDate: 42,
      salesRankBand: 'top-10',
      salesRankingDays: 60,
    },
  );
  assert.deepEqual(
    computeButtonState({ hasMatch: false, hasPurchase: false, salesRankBand: 'top-10' }),
    { kind: 'none', reason: 'noMatch' },
  );
});

test('normalizeSummaryItem 归一化摘要项', () => {
  assert.deepEqual(normalizeSummaryItem(null), { hasMatch: false });
  assert.deepEqual(normalizeSummaryItem({ error: 'x' }), { error: 'x' });
  assert.deepEqual(normalizeSummaryItem({ hasMatch: false }), { hasMatch: false, hasPurchase: false, lastOrderDate: null, lastOrderQuantity: null, salesToDate: null });
  assert.equal(normalizeSummaryItem({ lastOrderDate: '2026-08-01' }).hasPurchase, true);
  assert.deepEqual(
    normalizeSummaryItem(
      {
        matchStatus: 'matched',
        latestPurchaseDate: '2026-08-01',
        latestPurchaseQuantity: 12,
        salesSinceLatestPurchase: 7,
        salesRankBand: 'top-30',
      },
      { salesRankingAvailable: true },
    ),
    {
      hasMatch: true,
      hasPurchase: true,
      lastOrderDate: '2026-08-01',
      lastOrderQuantity: 12,
      salesToDate: 7,
      salesRankBand: 'top-30',
    },
  );
  assert.equal(normalizeSummaryItem({ matchStatus: 'no-purchase' }).hasPurchase, false);
  assert.equal(normalizeSummaryItem({ matchStatus: 'unmatched' }).hasMatch, false);
});

test('摘要缓存键包含门店、周期和商品，避免 60/90 天结果串用', () => {
  assert.equal(buildSummaryCacheKey('1014', 'ABC', 60), '1014:60:ABC');
  assert.equal(buildSummaryCacheKey('1014', 'ABC', 90), '1014:90:ABC');
  assert.notEqual(
    buildSummaryCacheKey('1014', 'ABC', 60),
    buildSummaryCacheKey('1014', 'ABC', 90),
  );
});

test('normalizeSummaryMap 支持对象/数组/items 形态', () => {
  assert.deepEqual(normalizeSummaryMap(null), {});
  assert.equal(normalizeSummaryMap({ A: { hasPurchase: true }, B: { hasMatch: false } }).A.hasPurchase, true);
  assert.equal(normalizeSummaryMap([{ itemNumber: 'A', hasPurchase: true }]).A.hasPurchase, true);
  assert.equal(normalizeSummaryMap({ items: [{ itemNumber: 'A', hasPurchase: true }] }).A.hasPurchase, true);
});

test('批量摘要仅在顶层明确声明排名可用时接受销量档位', () => {
  const item = {
    itemNumber: 'A',
    matchStatus: 'no-purchase',
    salesRankBand: 'top-20',
  };

  assert.equal(
    normalizeSummaryMap({ salesRankingAvailable: true, items: [item] }).A.salesRankBand,
    'top-20',
  );
  assert.equal(
    normalizeSummaryMap({ salesRankingAvailable: false, items: [item] }).A.salesRankBand,
    undefined,
  );
  assert.equal(normalizeSummaryMap({ items: [item] }).A.salesRankBand, undefined);
  assert.equal(normalizeSummaryMap([item]).A.salesRankBand, undefined);
});
