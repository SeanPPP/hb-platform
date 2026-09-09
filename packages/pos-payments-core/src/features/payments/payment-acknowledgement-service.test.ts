import assert from "node:assert/strict";
import test from "node:test";

import type { PaymentAttempt } from "@hb/pos-domain/core/contracts/payment";

import {
  PaymentAcknowledgementService,
} from "./payment-acknowledgement-service";

function attempt(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-1",
    idempotencyKey: "idempotency-1",
    orderGuid: "order-1",
    provider: "linkly-cloud",
    operation: "purchase",
    amount: { currency: "AUD", cents: 500 },
    state: "Approved",
    references: { checkoutId: null, paymentId: null, sessionId: "session-1", txnRef: null, rfn: null, voucherReservationToken: null },
    createdAtIso: "2026-09-09T00:00:00.000Z",
    updatedAtIso: "2026-09-09T00:00:01.000Z",
    lastErrorCode: null,
    providerEnvironment: "production",
    providerAcknowledgedAtIso: null,
    ...overrides,
  };
}

test("ACK 成功后才写 marker；marker 失败保留确定金融结果以供纯 ACK 重试", async () => {
  const ledger = new MemoryLedger(attempt());
  const acknowledger = new FakeAcknowledger();
  const service = new PaymentAcknowledgementService({
    ledger,
    acknowledger,
    nowIso: () => "2026-09-09T00:00:02.000Z",
  });

  ledger.markResult = false;
  const markerFailed = await service.acknowledge("attempt-1");
  assert.equal(markerFailed.pending, true);
  assert.equal(markerFailed.errorCode, "LINKLY_ACKNOWLEDGEMENT_PENDING");
  assert.equal(acknowledger.calls, 1);
  assert.equal(ledger.current.state, "Approved");
  assert.equal(ledger.current.providerAcknowledgedAtIso, null);

  ledger.markResult = true;
  const completed = await service.acknowledge("attempt-1");
  assert.equal(completed.pending, false);
  assert.equal(completed.acknowledged, true);
  assert.equal(acknowledger.calls, 2);
  assert.equal(ledger.current.providerAcknowledgedAtIso, "2026-09-09T00:00:02.000Z");
});

test("历史无环境的 final Linkly 记录 fail-closed 且不调用 ACK", async () => {
  const ledger = new MemoryLedger(attempt({ providerEnvironment: null }));
  const acknowledger = new FakeAcknowledger();
  const service = new PaymentAcknowledgementService({ ledger, acknowledger, nowIso: () => "2026-09-09T00:00:02.000Z" });

  const result = await service.acknowledge("attempt-1");
  assert.equal(result.pending, true);
  assert.equal(acknowledger.calls, 0);
  assert.equal(ledger.current.providerAcknowledgedAtIso, null);
});

test("历史 final 只接受 provider 强匹配回传的环境，并在 CAS 冻结后 ACK", async () => {
  const ledger = new MemoryLedger(attempt({ providerEnvironment: null }));
  const acknowledger = new FakeAcknowledger();
  const service = new PaymentAcknowledgementService({
    ledger,
    acknowledger,
    legacyReconciler: { async reconcileLegacy() { return "production"; } },
    nowIso: () => "2026-09-09T00:00:02.000Z",
  });
  const result = await service.acknowledge("attempt-1");
  assert.equal(result.acknowledged, true);
  assert.equal(ledger.current.providerEnvironment, "production");
  assert.equal(acknowledger.calls, 1);
});

test("Approved 缺少本地业务证明时不允许把 server guard 释放", async () => {
  const ledger = new MemoryLedger(attempt());
  ledger.eligible = false;
  const acknowledger = new FakeAcknowledger();
  const service = new PaymentAcknowledgementService({ ledger, acknowledger, nowIso: () => "2026-09-09T00:00:02.000Z" });

  const result = await service.acknowledge("attempt-1");
  assert.equal(result.pending, true);
  assert.equal(acknowledger.calls, 0);
  assert.equal(ledger.current.state, "Approved");
});

test("已确认或非终态不能重放 provider ACK", async () => {
  for (const candidate of [
    attempt({ providerAcknowledgedAtIso: "2026-09-09T00:00:02.000Z" }),
    attempt({ state: "Unknown" }),
  ]) {
    const ledger = new MemoryLedger(candidate);
    const acknowledger = new FakeAcknowledger();
    const service = new PaymentAcknowledgementService({ ledger, acknowledger, nowIso: () => "2026-09-09T00:00:03.000Z" });
    const result = await service.acknowledge("attempt-1");
    assert.equal(acknowledger.calls, 0);
    assert.equal(result.pending, candidate.state === "Unknown");
  }
});

class MemoryLedger {
  public markResult = true;
  public eligible = true;
  public current: PaymentAttempt;
  public constructor(current: PaymentAttempt) { this.current = current; }
  public async get(): Promise<PaymentAttempt> { return this.current; }
  public async canProviderAcknowledged(): Promise<boolean> { return this.eligible; }
  public async verifyProviderEnvironment(expected: PaymentAttempt, environment: string): Promise<boolean> {
    if (this.current.attemptId !== expected.attemptId || this.current.providerEnvironment) return false;
    this.current = { ...this.current, providerEnvironment: environment };
    return true;
  }
  public async markProviderAcknowledged(expected: PaymentAttempt, nowIso: string): Promise<boolean> {
    if (!this.markResult || this.current.attemptId !== expected.attemptId) return false;
    this.current = { ...this.current, providerAcknowledgedAtIso: nowIso };
    return true;
  }
}

class FakeAcknowledger {
  public calls = 0;
  public async acknowledge(): Promise<void> { this.calls += 1; }
}


test("未创建后端会话的确定拒绝与本地取消无需 ACK，也不伪造确认标记", async () => {
  for (const state of ["Declined", "Cancelled"] as const) {
    const original = attempt({ state, providerEnvironment: null });
    const ledger = new MemoryLedger({ ...original, references: { ...original.references, sessionId: null } });
    const acknowledger = new FakeAcknowledger();
    const service = new PaymentAcknowledgementService({ ledger, acknowledger, nowIso: () => "2026-09-09T00:00:03.000Z" });
    const resolved = await service.acknowledge(original.attemptId);
    assert.equal(resolved.acknowledged, true);
    assert.equal(resolved.pending, false);
    assert.equal(acknowledger.calls, 0);
    assert.equal(ledger.current.providerAcknowledgedAtIso, null);
  }
});
