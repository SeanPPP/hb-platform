import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_SALES_TOOLBAR_ORDER,
  mergeVisibleSalesToolbarOrder,
  reconcileSalesToolbarOrder,
} from "./sales-toolbar-order";

test("销售工具栏顺序会去除未知和重复 ID，并补齐默认项", () => {
  assert.deepEqual(
    reconcileSalesToolbarOrder([
      "lock",
      "unknown-action",
      "held-orders",
      "lock",
      "returns",
    ]),
    [
      "lock",
      "held-orders",
      "daily-close",
      "returns",
      "remote-history",
      "special-products",
      "installments",
      "settings",
      "attendance-audit",
      "sync-history",
      "catalog-maintenance",
      "hold",
      "language",
    ],
  );
});

test("空的持久化值会恢复完整默认顺序", () => {
  assert.deepEqual(reconcileSalesToolbarOrder(null), [
    ...DEFAULT_SALES_TOOLBAR_ORDER,
  ]);
  assert.deepEqual(reconcileSalesToolbarOrder(undefined), [
    ...DEFAULT_SALES_TOOLBAR_ORDER,
  ]);
});

test("缺失的新操作会补回其默认相邻操作之间", () => {
  assert.deepEqual(
    reconcileSalesToolbarOrder(
      DEFAULT_SALES_TOOLBAR_ORDER.filter((actionId) => actionId !== "language"),
    ),
    [...DEFAULT_SALES_TOOLBAR_ORDER],
  );
});

test("重排可见操作时，隐藏操作保留其原来的槽位", () => {
  assert.deepEqual(
    mergeVisibleSalesToolbarOrder(DEFAULT_SALES_TOOLBAR_ORDER, [
      "returns",
      "held-orders",
      "hold",
    ]),
    [
      "returns",
      "daily-close",
      "held-orders",
      "remote-history",
      "special-products",
      "installments",
      "settings",
      "attendance-audit",
      "sync-history",
      "catalog-maintenance",
      "hold",
      "language",
      "lock",
    ],
  );
});

test("可见顺序中的无效或重复值不会挤占有效操作槽位", () => {
  assert.deepEqual(
    mergeVisibleSalesToolbarOrder(DEFAULT_SALES_TOOLBAR_ORDER, [
      "hold",
      "not-an-action",
      "hold",
      "held-orders",
    ]),
    [
      "hold",
      "daily-close",
      "returns",
      "remote-history",
      "special-products",
      "installments",
      "settings",
      "attendance-audit",
      "sync-history",
      "catalog-maintenance",
      "held-orders",
      "language",
      "lock",
    ],
  );
});
