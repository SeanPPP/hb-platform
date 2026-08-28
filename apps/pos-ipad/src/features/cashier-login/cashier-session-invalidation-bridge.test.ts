import assert from "node:assert/strict";
import test from "node:test";

import { createCashierInvalidationHandler } from "@hb/pos-domain/features/cashier-login/cashier-session-invalidation-recovery";

test("401 只清除收银员，403 原地锁定 runtime 而不触发重建", () => {
  let clears = 0;
  let locks = 0;
  let revocations = 0;
  const handler = createCashierInvalidationHandler({
    revokeTemporaryAuthorizations() {
      revocations += 1;
    },
    clearActiveCashier() {
      clears += 1;
    },
    lockRuntime() {
      locks += 1;
    },
  });

  handler("unauthorized");
  assert.equal(clears, 1);
  assert.equal(locks, 0);

  handler("manual-lock");
  assert.equal(clears, 2);
  assert.equal(locks, 0);

  handler("forbidden");
  handler("forbidden");
  assert.equal(clears, 4);
  assert.equal(locks, 2);
  assert.equal(revocations, 4);
});

test("临时授权或锁定投影失败不阻止活动收银员清理", () => {
  let clears = 0;
  const handler = createCashierInvalidationHandler({
    revokeTemporaryAuthorizations() {
      throw new Error("scope cleanup unavailable");
    },
    clearActiveCashier() {
      clears += 1;
    },
    lockRuntime() {
      throw new Error("runtime state unavailable");
    },
  });

  assert.throws(() => handler("forbidden"), /runtime state unavailable/);
  assert.equal(clears, 1);
});

test("设备 scope 变更只清除 Zustand 活动收银员，不把 runtime 投影为 forbidden", () => {
  let clears = 0;
  let locks = 0;
  const handler = createCashierInvalidationHandler({
    clearActiveCashier() {
      clears += 1;
    },
    lockRuntime() {
      locks += 1;
    },
  });

  handler("device-scope-change");

  assert.equal(clears, 1);
  assert.equal(locks, 0);
});
