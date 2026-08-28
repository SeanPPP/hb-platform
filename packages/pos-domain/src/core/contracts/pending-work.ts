export type PendingWorkSnapshot = Readonly<{
  hasActiveCart: boolean;
  hasFulfilmentInFlight: boolean;
  hasSyncOrAuditInFlight: boolean;
  paymentConfigurationSensitiveOrderCount: number;
  pendingDurableWriteCount: number;
  pendingReturnCount: number;
  pendingSaleCount: number;
  unresolvedPaymentCount: number;
}>;

export type PendingWorkBlocker =
  | Readonly<{
      kind: "in-progress";
      code:
        | "active-cart"
        | "fulfilment-in-flight"
        | "sync-or-audit-in-flight";
    }>
  | Readonly<{
      kind: "count";
      code:
        | "payment-configuration-sensitive-orders"
        | "pending-durable-writes"
        | "pending-returns"
        | "pending-sales"
        | "unresolved-payments";
      count: number;
    }>;

/**
 * 只派生 UI 所需的脱敏类别与数量；非法快照抛错，由调用方保持 fail-closed。
 * 顺序是公共展示契约，两端不得自行重排后造成相同设备显示不一致。
 */
export function derivePendingWorkBlockers(
  snapshot: PendingWorkSnapshot,
): readonly PendingWorkBlocker[] {
  assertBoolean(snapshot.hasActiveCart, "hasActiveCart");
  assertBoolean(snapshot.hasFulfilmentInFlight, "hasFulfilmentInFlight");
  assertBoolean(snapshot.hasSyncOrAuditInFlight, "hasSyncOrAuditInFlight");
  assertCount(
    snapshot.paymentConfigurationSensitiveOrderCount,
    "paymentConfigurationSensitiveOrderCount",
  );
  assertCount(snapshot.pendingDurableWriteCount, "pendingDurableWriteCount");
  assertCount(snapshot.pendingReturnCount, "pendingReturnCount");
  assertCount(snapshot.pendingSaleCount, "pendingSaleCount");
  assertCount(snapshot.unresolvedPaymentCount, "unresolvedPaymentCount");

  const blockers: PendingWorkBlocker[] = [];
  if (snapshot.hasActiveCart) {
    blockers.push(Object.freeze({
      kind: "in-progress",
      code: "active-cart",
    }));
  }
  if (snapshot.hasFulfilmentInFlight) {
    blockers.push(Object.freeze({
      kind: "in-progress",
      code: "fulfilment-in-flight",
    }));
  }
  if (snapshot.hasSyncOrAuditInFlight) {
    blockers.push(Object.freeze({
      kind: "in-progress",
      code: "sync-or-audit-in-flight",
    }));
  }
  pushCountBlocker(
    blockers,
    "payment-configuration-sensitive-orders",
    snapshot.paymentConfigurationSensitiveOrderCount,
  );
  pushCountBlocker(
    blockers,
    "pending-durable-writes",
    snapshot.pendingDurableWriteCount,
  );
  pushCountBlocker(blockers, "pending-returns", snapshot.pendingReturnCount);
  pushCountBlocker(blockers, "pending-sales", snapshot.pendingSaleCount);
  pushCountBlocker(
    blockers,
    "unresolved-payments",
    snapshot.unresolvedPaymentCount,
  );
  return Object.freeze(blockers);
}

function assertBoolean(value: unknown, field: string): asserts value is boolean {
  if (typeof value !== "boolean") {
    throw new TypeError(`Pending work snapshot ${field} must be boolean.`);
  }
}

function assertCount(value: unknown, field: string): asserts value is number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) {
    throw new TypeError(
      `Pending work snapshot ${field} must be a non-negative safe integer.`,
    );
  }
}

function pushCountBlocker(
  blockers: PendingWorkBlocker[],
  code: Extract<PendingWorkBlocker, { kind: "count" }>["code"],
  count: number,
): void {
  if (count === 0) return;
  blockers.push(Object.freeze({ kind: "count", code, count }));
}
