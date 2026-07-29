import assert from "node:assert/strict";
import test from "node:test";

import { CashierSessionInvalidationBus } from "../security/cashier-session-invalidation";

import { createPublicCashierInvalidation } from "./public-cashier-invalidation";

test("公开失效桥只允许订阅，route 不能主动伪造锁屏或 401/403", () => {
  const bus = new CashierSessionInvalidationBus();
  const source = createPublicCashierInvalidation(bus);
  const reasons: string[] = [];
  const unsubscribe = source.subscribe((reason) => reasons.push(reason));

  assert.deepEqual(Object.keys(source), ["subscribe"]);
  assert.equal("notify" in source, false);
  bus.notify("unauthorized");
  assert.deepEqual(reasons, ["unauthorized"]);
  unsubscribe();
  bus.notify("forbidden");
  assert.deepEqual(reasons, ["unauthorized"]);
});
