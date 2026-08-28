import assert from "node:assert/strict";
import test from "node:test";

import {
  derivePendingWorkBlockers,
  type PendingWorkSnapshot,
} from "./pending-work";

const emptySnapshot = (): PendingWorkSnapshot => ({
  hasActiveCart: false,
  hasFulfilmentInFlight: false,
  hasSyncOrAuditInFlight: false,
  paymentConfigurationSensitiveOrderCount: 0,
  pendingDurableWriteCount: 0,
  pendingReturnCount: 0,
  pendingSaleCount: 0,
  unresolvedPaymentCount: 0,
});

test("待处理业务阻断按稳定顺序返回八类脱敏状态", () => {
  assert.deepEqual(
    derivePendingWorkBlockers({
      hasActiveCart: true,
      hasFulfilmentInFlight: true,
      hasSyncOrAuditInFlight: true,
      paymentConfigurationSensitiveOrderCount: 2,
      pendingDurableWriteCount: 3,
      pendingReturnCount: 4,
      pendingSaleCount: 5,
      unresolvedPaymentCount: 6,
    }),
    [
      { kind: "in-progress", code: "active-cart" },
      { kind: "in-progress", code: "fulfilment-in-flight" },
      { kind: "in-progress", code: "sync-or-audit-in-flight" },
      {
        kind: "count",
        code: "payment-configuration-sensitive-orders",
        count: 2,
      },
      { kind: "count", code: "pending-durable-writes", count: 3 },
      { kind: "count", code: "pending-returns", count: 4 },
      { kind: "count", code: "pending-sales", count: 5 },
      { kind: "count", code: "unresolved-payments", count: 6 },
    ],
  );
});

test("没有本地待处理业务时不返回阻断项", () => {
  assert.deepEqual(derivePendingWorkBlockers(emptySnapshot()), []);
});

test("非法布尔或计数拒绝派生，调用方可按安全检查失败保持阻断", () => {
  for (const field of [
    "hasActiveCart",
    "hasFulfilmentInFlight",
    "hasSyncOrAuditInFlight",
  ] as const) {
    assert.throws(
      () => derivePendingWorkBlockers({
        ...emptySnapshot(),
        [field]: 1,
      } as unknown as PendingWorkSnapshot),
      TypeError,
      field,
    );
  }

  for (const field of [
    "paymentConfigurationSensitiveOrderCount",
    "pendingDurableWriteCount",
    "pendingReturnCount",
    "pendingSaleCount",
    "unresolvedPaymentCount",
  ] as const) {
    for (const invalid of [-1, 0.5, Number.MAX_SAFE_INTEGER + 1, NaN]) {
      assert.throws(
        () => derivePendingWorkBlockers({
          ...emptySnapshot(),
          [field]: invalid,
        }),
        TypeError,
        `${field}: ${String(invalid)}`,
      );
    }
  }
});
