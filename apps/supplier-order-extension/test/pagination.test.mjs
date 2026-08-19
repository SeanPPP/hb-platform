import test from 'node:test';
import assert from 'node:assert/strict';
import {
  normalizeCycles,
  buildTimeline,
  filterTimeline,
  paginate,
} from '../src/lib/pagination.js';

test('normalizeCycles 订单与销售字段映射', () => {
  const { orders, sales } = normalizeCycles({
    orders: [{ orderNo: 'O1', orderDate: '2026-01-02', quantity: 5, averagePurchasePrice: 3.2 }],
    sales: [{ orderNo: 'S1', date: '2026-01-01', quantity: 2, averageSellPrice: 9.9 }],
  });
  assert.equal(orders.length, 1);
  assert.equal(orders[0].type, 'order');
  assert.equal(orders[0].orderNo, 'O1');
  assert.equal(orders[0].quantity, 5);
  assert.equal(orders[0].price, 3.2);
  assert.equal(sales[0].type, 'sales');
  assert.equal(sales[0].price, 9.9);
});

test('normalizeCycles 按后端采购周期 DTO 生成订货和区间销售两类记录', () => {
  const { orders, sales } = normalizeCycles({
    cycles: [
      {
        purchaseDate: '2026-08-01',
        invoiceNumbers: ['INV-2', 'INV-1'],
        purchaseQuantity: 10,
        averagePurchasePrice: 2.6,
        salesStartDate: '2026-08-01',
        salesEndDate: '2026-08-09',
        salesQuantity: 5,
        averageSalePrice: 5.4,
      },
    ],
  });
  assert.deepEqual(orders, [{
    type: 'order',
    orderNo: 'INV-2, INV-1',
    date: '2026-08-01',
    quantity: 10,
    price: 2.6,
  }]);
  assert.deepEqual(sales, [{
    type: 'sales',
    orderNo: '',
    date: '2026-08-09',
    dateRange: '2026-08-01 — 2026-08-09',
    quantity: 5,
    price: 5.4,
  }]);
});

test('buildTimeline 倒序混排 + 订货上限6次 + 12个月窗口', () => {
  const now = Date.parse('2026-08-01T00:00:00Z');
  const orders = [
    { orderNo: 'O1', date: '2026-07-01', quantity: 1, price: 1 },
    { orderNo: 'O2', date: '2026-06-01', quantity: 1, price: 1 },
    { orderNo: 'O3', date: '2026-05-01', quantity: 1, price: 1 },
    { orderNo: 'O4', date: '2026-04-01', quantity: 1, price: 1 },
    { orderNo: 'O5', date: '2026-03-01', quantity: 1, price: 1 },
    { orderNo: 'O6', date: '2026-02-01', quantity: 1, price: 1 },
    { orderNo: 'O7', date: '2026-01-01', quantity: 1, price: 1 },
    { orderNo: 'O8', date: '2025-01-01', quantity: 1, price: 1 },
  ];
  const sales = [
    { orderNo: 'S1', date: '2026-07-15', quantity: 3, price: 5 },
    { orderNo: 'S2', date: '2025-01-01', quantity: 1, price: 5 },
  ];
  const tl = buildTimeline({ orders, sales, now });
  assert.equal(tl.length, 7);
  assert.deepEqual(
    tl.map((e) => e.orderNo),
    ['S1', 'O1', 'O2', 'O3', 'O4', 'O5', 'O6'],
  );
  assert.equal(tl[0].type, 'sales');
  assert.equal(tl[1].type, 'order');
});

test('buildTimeline 使用日历12个月而不是固定360天', () => {
  const now = Date.parse('2026-08-19T00:00:00Z');
  const result = buildTimeline({
    orders: [
      { orderNo: 'IN', date: '2025-08-19' },
      { orderNo: 'OUT', date: '2025-08-18' },
    ],
    now,
  });
  assert.deepEqual(result.map((item) => item.orderNo), ['IN']);
});

test('filterTimeline 全部/订货/销售', () => {
  const tl = [
    { type: 'order', orderNo: 'O1', date: '2026-07-01' },
    { type: 'sales', orderNo: 'S1', date: '2026-06-01' },
    { type: 'order', orderNo: 'O2', date: '2026-05-01' },
  ];
  assert.equal(filterTimeline(tl, 'all').length, 3);
  assert.equal(filterTimeline(tl, 'order').length, 2);
  assert.equal(filterTimeline(tl, 'sales').length, 1);
});

test('paginate 每页20条与边界', () => {
  const items = Array.from({ length: 45 }, (_, i) => ({ i }));
  const p1 = paginate(items, 1);
  assert.equal(p1.pageSize, 20);
  assert.equal(p1.items.length, 20);
  assert.equal(p1.total, 45);
  assert.equal(p1.totalPages, 3);
  assert.equal(paginate(items, 3).items.length, 5);
  assert.equal(paginate(items, 0).page, 1);
  assert.equal(paginate(items, 99).page, 3);
  assert.equal(paginate([], 1).totalPages, 1);
});
