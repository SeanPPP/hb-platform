import assert from "node:assert/strict";
import test from "node:test";

import { withPersistedLinklyAcknowledgementRecovery } from "./linkly-acknowledgement-recovery-runtime";
import type { PaymentCheckoutPublicSnapshot, PaymentCheckoutRuntimePort } from "./payment-checkout-runtime";

import type { PaymentAttempt } from "@/core/contracts";

const amount = { currency: "AUD", cents: 1200 } as const;
function harness(state: "Approved" | "Declined" | "Cancelled" = "Approved", sessionId: string | null = "original-session") {
  let marked = false;
  let fail = false;
  let durable = true;
  let discover = true;
  let readFails = false;
  let closeCancelled = false;
  let financialBlocked = false;
  let ackCalls = 0;
  let pendingReads = 0;
  let financialCalls = 0;
  const attempt: PaymentAttempt = {
    attemptId: "original-attempt", orderGuid: "original-order", idempotencyKey: "original-key",
    provider: "linkly-cloud", providerEnvironment: "Sandbox", operation: "purchase", amount, state,
    references: { checkoutId: null, paymentId: null, sessionId, txnRef: null, rfn: null, voucherReservationToken: null },
    createdAtIso: "2026-09-09T00:00:00.000Z", updatedAtIso: "2026-09-09T00:00:01.000Z", lastErrorCode: null,
  };
  const snapshot: PaymentCheckoutPublicSnapshot = {
    orderGuid: attempt.orderGuid, total: amount, remaining: { ...amount, cents: state === "Approved" ? 0 : 1200 }, tenders: [],
    attemptId: attempt.attemptId, attemptCreatedAtIso: attempt.createdAtIso, provider: "linkly-cloud",
    status: state === "Approved" ? "completed" : state === "Declined" ? "declined" : "cancelled", errorCode: null,
    allowedActions: { start: false, changeProvider: false, recover: false, cancel: false, addCash: false, removeTender: false },
  };
  const financial = async () => {
    financialCalls += 1;
    if (closeCancelled) { discover = true; return { ...snapshot, attemptId: null, attemptCreatedAtIso: null }; }
    return snapshot;
  };
  const base: PaymentCheckoutRuntimePort = {
    listProviderAvailability: () => [], read: async () => snapshot, findRecoveryRequired: async () => null,
    resumeCurrent: financial, start: financial, startCash: financial, recover: financial, cancel: financial,
    abandonPrepared: financial, addCash: financial, removeTender: financial,
  };
  const create = () => withPersistedLinklyAcknowledgementRecovery({
    runtime: base, assertView: () => undefined, assertAcknowledge: () => undefined,
    assertFinancialAvailable: () => { if (financialBlocked) throw new Error("RETURN_RECOVERY_REQUIRED"); },
    findPendingAttempt: async () => { pendingReads += 1; return marked || !discover ? null : attempt; },
    getAttempt: async () => { if (readFails) throw new Error("local read failed after commit"); return { ...attempt, providerAcknowledgedAtIso: marked ? "2026-09-09T00:00:02.000Z" : null }; },
    canAcknowledge: async () => durable,
    readFinalSnapshot: async () => snapshot,
    acknowledgements: { async acknowledge() {
      ackCalls += 1;
      if (!fail) marked = true;
      return { attempt, acknowledged: !fail, pending: fail, errorCode: fail ? "LINKLY_ACKNOWLEDGEMENT_PENDING" : null };
    } },
  });
  return { create, attempt, snapshot, setFail: (v: boolean) => { fail = v; }, setDurable: (v: boolean) => { durable = v; },
    setMarked: (v: boolean) => { marked = v; }, setDiscover: (v: boolean) => { discover = v; }, setReadFails: (v: boolean) => { readFails = v; },
    setFinancialBlocked: (v: boolean) => { financialBlocked = v; },
    setCloseCancelled: () => { closeCancelled = true; }, pendingReads: () => pendingReads, counts: () => ({ ackCalls, financialCalls }) };
}

test("已完成旧订单冷启动发现待确认；恢复只 ACK，不执行付款、落单或购物车动作", async () => {
  const h = harness();
  const recovery = await h.create().findRecoveryRequired();
  assert.equal(recovery?.status, "recovery-required");
  assert.equal(recovery?.errorCode, "LINKLY_ACKNOWLEDGEMENT_PENDING");
  assert.equal(recovery?.attemptId, h.attempt.attemptId);
  assert.equal(recovery?.allowedActions.start, false);
  assert.equal((await h.create().recover({ orderGuid: h.attempt.orderGuid, attemptId: h.attempt.attemptId })).status, "completed");
  assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 0 });
  assert.equal(await h.create().findRecoveryRequired(), null);
});

test("ACK失败跨runtime重建继续待确认，下一单被挡住且只重试原ACK", async () => {
  const h = harness();
  h.setFail(true);
  assert.equal((await h.create().resumeCurrent())?.status, "recovery-required");
  const blocked = await h.create().start({ checkoutIntentId: "new-cart", expectedCartRevision: 7, actionId: "new-action", provider: "square", amount });
  assert.equal(blocked.orderGuid, h.attempt.orderGuid);
  assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 0 });
  h.setFail(false);
  assert.equal((await h.create().resumeCurrent())?.status, "completed");
  assert.deepEqual(h.counts(), { ackCalls: 2, financialCalls: 0 });
});

test("失败与取消交易的ACK恢复保留原金融结果并允许退出", async () => {
  for (const state of ["Declined", "Cancelled"] as const) {
    const h = harness(state);
    const resolved = await h.create().resumeCurrent();
    assert.equal(resolved?.status, state === "Declined" ? "declined" : "cancelled");
    assert.equal(resolved?.attemptId, null);
    assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 0 });
  }
});

test("Approved 尚无业务落账证明时仍走原支付恢复，不提前确认", async () => {
  const h = harness();
  h.setDurable(false);
  assert.equal(await h.create().findRecoveryRequired(), null);
  await h.create().recover({ orderGuid: h.attempt.orderGuid, attemptId: h.attempt.attemptId });
  assert.deepEqual(h.counts(), { ackCalls: 0, financialCalls: 1 });
});


test("正常收款在本地完成之后自动确认，确认故障保持金额与最终订单事实", async () => {
  const h = harness();
  h.setDiscover(false);
  h.setFail(true);
  const result = await h.create().start({ checkoutIntentId: "cart", expectedCartRevision: 1, actionId: "action", provider: "linkly-cloud", amount });
  assert.equal(result.errorCode, "LINKLY_ACKNOWLEDGEMENT_PENDING");
  assert.equal(result.remaining.cents, 0);
  assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 1 });
});

test("订单提交后的确认账本读取故障仍保留已支付金额并显示待确认", async () => {
  const h = harness();
  h.setDiscover(false);
  h.setReadFails(true);
  const result = await h.create().start({ checkoutIntentId: "cart", expectedCartRevision: 1, actionId: "action", provider: "linkly-cloud", amount });
  assert.equal(result.errorCode, "LINKLY_ACKNOWLEDGEMENT_PENDING");
  assert.equal(result.remaining.cents, 0);
  assert.deepEqual(h.counts(), { ackCalls: 0, financialCalls: 1 });
});


test("人工ACK已写marker后刷新页面仍只读原终态，不进入交易恢复", async () => {
  const h = harness();
  h.setMarked(true);
  const result = await h.create().recover({ orderGuid: h.attempt.orderGuid, attemptId: h.attempt.attemptId });
  assert.equal(result.status, "completed");
  assert.deepEqual(h.counts(), { ackCalls: 0, financialCalls: 0 });
  assert.equal(h.pendingReads(), 0, "原交易 marker 快路径不能调用可能读取后端的全局发现");
});


test("安全取消关闭草稿清除公开指针后，仍自动确认耐久原交易", async () => {
  const h = harness("Cancelled");
  h.setDiscover(false);
  h.setCloseCancelled();
  const result = await h.create().cancel({ orderGuid: h.attempt.orderGuid, attemptId: h.attempt.attemptId });
  assert.equal(result.status, "cancelled");
  assert.equal(result.attemptId, null);
  assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 1 });
  assert.equal(await h.create().findRecoveryRequired(), null);
});


test("没有后端会话的拒绝与本地取消不被误列为终端待确认", async () => {
  for (const state of ["Declined", "Cancelled"] as const) {
    const h = harness(state, null);
    assert.equal(await h.create().findRecoveryRequired(), null);
    const result = await h.create().start({ checkoutIntentId: "cart", expectedCartRevision: 1, actionId: "action", provider: "linkly-cloud", amount });
    assert.equal(result.status, state === "Declined" ? "declined" : "cancelled");
    assert.deepEqual(h.counts(), { ackCalls: 0, financialCalls: 1 });
  }
});


test("活动退货阻止新金融操作，仍允许纯ACK及安全放弃未提交草稿", async () => {
  const h = harness();
  h.setFinancialBlocked(true);
  h.setDiscover(false);
  h.setDurable(false);
  const runtime = h.create();
  await assert.rejects(runtime.start({ checkoutIntentId: "new-cart", expectedCartRevision: 1, actionId: "new", provider: "linkly-cloud", amount }), /RETURN_RECOVERY_REQUIRED/);
  await assert.rejects(runtime.startCash!({ checkoutIntentId: "new-cart", expectedCartRevision: 1, actionId: "cash", amount }), /RETURN_RECOVERY_REQUIRED/);
  await assert.rejects(runtime.recover({ orderGuid: h.attempt.orderGuid, attemptId: h.attempt.attemptId }), /RETURN_RECOVERY_REQUIRED/);
  assert.deepEqual(h.counts(), { ackCalls: 0, financialCalls: 0 });
  h.setDiscover(true);
  h.setDurable(true);
  assert.equal((await runtime.resumeCurrent())?.status, "completed");
  assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 0 });
  await runtime.abandonPrepared({ orderGuid: h.attempt.orderGuid, actionId: "abandon" });
  assert.deepEqual(h.counts(), { ackCalls: 1, financialCalls: 1 });
});
