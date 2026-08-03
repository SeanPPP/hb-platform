import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import { CurrentCashierSession } from "./current-cashier-session";
import type { InstallmentVoucherIntentVaultPort } from "./production-installment-payment-adapter";
import {
  createProductionInstallmentRuntime,
  type InstallmentActionStorePort,
  type InstallmentApprovedRefund,
  type InstallmentPaymentAction,
  type InstallmentMutationPaymentPort,
  type InstallmentReceiptReprintRuntimePort,
  type PersistedInstallmentAction,
  type ProductionInstallmentRuntimeDependencies,
} from "./production-installment-runtime";

import { HbposApiError } from "@/core/api/hbpos-api";
import type { InstallmentSnapshot } from "@/core/contracts";
import type { CashierLoginResult } from "@/core/security/cashier-authentication";
import {
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_REPRINT_PERMISSION,
  INSTALLMENTS_VIEW_PERMISSION,
} from "@/features/installments/installment-authorization";
import type {
  InstallmentCreateCommand,
  InstallmentCancelCommand,
  InstallmentDetails,
  InstallmentPaymentCommand,
  InstallmentsRemotePort,
} from "@/features/installments/installment-models";
import type {
  InstallmentPresenter,
  InstallmentWorkflowPort,
} from "@/features/installments/installment-presenter";
import { PAYMENT_PERMISSION } from "@/features/payments/runtime/payment-checkout-runtime";

const STORE_CODE = "STORE-1";
const DEVICE_CODE = "IPAD-1";
const ALL_PERMISSIONS = Object.freeze([
  INSTALLMENTS_VIEW_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
  INSTALLMENTS_REPRINT_PERMISSION,
  PAYMENT_PERMISSION.view,
  PAYMENT_PERMISSION.takeCash,
  PAYMENT_PERMISSION.takeCard,
  PAYMENT_PERMISSION.takeVoucher,
  PAYMENT_PERMISSION.confirm,
]);

test("公开服务提供管理、统一支付与恢复工厂，在线列表不读写旧门店快照", async () => {
  const harness = createHarness();

  assert.deepEqual(Object.keys(harness.runtime), [
    "createPresenter",
    "prepareCreateCheckout",
    "createCheckoutPresenter",
    "hasRecoveryRequired",
  ]);
  const presenter = harness.runtime.createPresenter();
  await presenter.load();

  assert.equal(harness.api.listCalls, 1);
  assert.equal(harness.cache.upsertCalls.length, 0);
  assert.equal(harness.cache.listCalls.length, 0);
  assert.equal(presenter.getState().orders[0]?.installmentGuid, "installment-1");
});

test("分期重打使用同一可信 lease、精确 History.Reprint 权限与 fulfilment 结果", async () => {
  const calls: string[] = [];
  const receiptReprint: InstallmentReceiptReprintRuntimePort = {
    canReprint: () => true,
    async execute(installmentGuid, authorization, assertActive) {
      assertActive();
      calls.push(installmentGuid);
      assert.equal(
        authorization.permissionCode,
        INSTALLMENTS_REPRINT_PERMISSION,
      );
      assert.equal(authorization.requestingCashierId, "CASHIER-1");
      return { state: "Printed", errorCode: null };
    },
  };
  const harness = createHarness({ receiptReprint });
  const presenter = harness.runtime.createPresenter();
  await presenter.select("installment-1");

  assert.equal(presenter.capabilities.reprint, true);
  await presenter.reprintSelected();

  assert.deepEqual(calls, ["installment-1"]);
  assert.deepEqual(presenter.getState().reprint, {
    kind: "succeeded",
    installmentGuid: "installment-1",
  });
});

test("分期重打缺权限或 cashier lease 失效时 fail closed", async () => {
  const receiptReprint: InstallmentReceiptReprintRuntimePort = {
    canReprint: () => true,
    async execute() {
      throw new Error("不应调用 fulfilment");
    },
  };
  const missingPermission = createHarness({
    permissions: ALL_PERMISSIONS.filter(
      (permission) => permission !== INSTALLMENTS_REPRINT_PERMISSION,
    ),
    receiptReprint,
  });
  const denied = missingPermission.runtime.createPresenter();
  await denied.select("installment-1");
  assert.equal(denied.capabilities.reprint, false);

  const expired = createHarness({ receiptReprint });
  const presenter = expired.runtime.createPresenter();
  await presenter.select("installment-1");
  expired.currentCashier.clear();
  assert.equal(presenter.capabilities.reprint, false);
  await presenter.reprintSelected();
  assert.deepEqual(presenter.getState().reprint, { kind: "unavailable" });
});

test("统一支付确认前掉线且无耐久 action 时保留可重试状态", async () => {
  const harness = createHarness();
  const entry = harness.runtime.prepareCreateCheckout();
  const presenter = harness.runtime.createCheckoutPresenter(entry);

  assert.equal(await presenter.initialize(), true);
  presenter.openInstallmentCustomerEditor();
  presenter.setInstallmentCustomerDraftName("Customer");
  presenter.setInstallmentCustomerDraftPhone("0400000000");
  presenter.saveInstallmentCustomer();
  assert.equal(await presenter.submitSelected(), true);

  harness.online.value = false;
  assert.equal(await presenter.confirm(), false);
  assert.equal(presenter.getState().phase, "ready");
  assert.equal(presenter.getState().runtimeErrorCode, "ONLINE_REQUIRED");
  assert.equal(presenter.getState().allowedActions.recover, false);
  assert.equal(harness.actionStore.createdCandidates.length, 0);
  assert.equal(await harness.runtime.hasRecoveryRequired(), false);
});

test("transport failure 与离线都返回 online-required，且不读取旧 SQLCipher 快照", async () => {
  const harness = createHarness({
    cached: [snapshot({ installmentGuid: "cached-store-1" })],
  });
  harness.api.listError = new HbposApiError("network", { kind: "transport" });
  const presenter = harness.runtime.createPresenter();
  await presenter.load();
  assert.equal(harness.cache.upsertCalls.length, 0);
  assert.equal(presenter.getState().statusCode, "online-required");
  assert.deepEqual(presenter.getState().orders, []);

  harness.online.value = false;
  presenter.setOnline(false);
  await presenter.load();
  assert.deepEqual(harness.cache.listCalls, []);
  assert.deepEqual(presenter.getState().orders, []);
  assert.equal(presenter.getState().statusCode, "online-required");
});

test("设备范围只使用可信 terminal code，日期按注入时钟和门店时区转为 UTC", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();

  await presenter.setDeviceScope("device");
  await presenter.setDateFilter({
    preset: "last7",
    fromDate: null,
    toDate: null,
  });

  assert.deepEqual(
    harness.api.listInputs.at(-1),
    {
      createdFromIso: "2026-07-21T14:00:00.000Z",
      createdToIso: "2026-07-28T13:59:59.999Z",
      deviceCode: DEVICE_CODE,
      keyword: null,
      skip: 0,
      status: null,
      take: 51,
    },
  );
  assert.equal(harness.cache.upsertCalls.length, 0);
  assert.equal(harness.cache.listCalls.length, 0);
});

test("远端 5xx、权限和未知错误映射为不同安全状态码", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();

  harness.api.listError = new HbposApiError(
    "https://secret.example/internal unavailable",
    { kind: "http", status: 503 },
  );
  await presenter.load();
  assert.equal(presenter.getState().statusCode, "service-unavailable");

  harness.api.listError = new HbposApiError("secret permission payload", {
    kind: "http",
    status: 403,
  });
  await presenter.load();
  assert.equal(presenter.getState().statusCode, "authorization-declined");

  harness.api.listError = new Error("https://secret.example/raw-url");
  await presenter.load();
  assert.equal(presenter.getState().statusCode, "history-failed");
  assert.equal(
    JSON.stringify(presenter.getState()).includes("secret.example"),
    false,
  );
});

test("详情设备范围拒绝不会伪装成收银员无权限或清空已加载列表", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();
  await presenter.load();
  harness.api.detailsError = new HbposApiError("device scope denied", {
    kind: "http",
    status: 403,
    code: "DEVICE_SCOPE_FORBIDDEN",
  });

  await presenter.select("installment-1");

  assert.equal(presenter.getState().kind, "ready");
  assert.equal(presenter.getState().statusCode, "details-failed");
  assert.equal(presenter.getState().selectedGuid, "installment-1");
  assert.equal(presenter.getState().orders.length, 1);
});

test("单次 mutation 更新快照时不会把第 101 条以后的本机历史截断", async () => {
  const cached = Array.from({ length: 150 }, (_, index) =>
    snapshot({ installmentGuid: `cached-${String(index).padStart(3, "0")}` }),
  );
  const harness = createHarness({ cached });
  const presenter = harness.runtime.createPresenter();
  fillCreate(presenter);

  await presenter.create();

  assert.equal(
    harness.cache
      .all()
      .some((item) => item.installmentGuid === "cached-149"),
    true,
  );
  assert.equal(harness.cache.all().length, 151);
});

test("create 在后端失败后以同一 action 恢复，绝不重复扣款且不清车", async () => {
  const harness = createHarness({ createFailsOnce: true });
  const presenter = harness.runtime.createPresenter();
  fillCreate(presenter);

  await presenter.create();
  assert.equal(harness.payments.charges, 1);
  assert.equal(harness.cart.clearCalls, 0);
  assert.equal(harness.api.createCalls.length, 1);

  await presenter.recoverBlocking();
  assert.equal(harness.payments.charges, 1);
  assert.equal(harness.api.createCalls.length, 2);
  assert.equal(
    harness.api.createCalls[0]?.installmentGuid,
    harness.api.createCalls[1]?.installmentGuid,
  );
  assert.equal(
    harness.api.createCalls[0]?.downPayment.paymentGuid,
    harness.api.createCalls[1]?.downPayment.paymentGuid,
  );
  assert.deepEqual(
    harness.api.createCalls[0]?.lines.map((line) => line.installmentLineGuid),
    harness.api.createCalls[1]?.lines.map((line) => line.installmentLineGuid),
  );
  assert.equal(harness.cart.clearCalls, 1);
});

test("Unknown 支付结果只恢复同一 provider attempt，不创建新 action", async () => {
  const harness = createHarness({ paymentOutcome: "unknown" });
  const presenter = harness.runtime.createPresenter();
  fillCreate(presenter);

  await presenter.create();
  assert.equal(presenter.getState().statusCode, "payment-recovery-required");
  await presenter.recoverBlocking();
  assert.equal(harness.payments.beginCalls, 2);
  assert.equal(
    harness.payments.actions[0]?.actionId,
    harness.payments.actions[1]?.actionId,
  );
  assert.equal(harness.api.createCalls.length, 0);
});

test("Approved 后 backend 失败并重建 runtime，必须恢复同一 action 且只扣款一次", async () => {
  const harness = createHarness({ createFailsOnce: true });
  const first = harness.runtime.createPresenter();
  fillCreate(first);
  await first.create();
  first.destroy();

  const recovered = harness.rebuildRuntime().createPresenter();
  await recovered.load();

  assert.equal(harness.payments.charges, 1);
  assert.equal(harness.api.createCalls.length, 2);
  assert.equal(
    harness.payments.actions[0]?.actionId,
    harness.payments.actions[1]?.actionId,
  );
  assert.equal(
    harness.api.createCalls[0]?.installmentGuid,
    harness.api.createCalls[1]?.installmentGuid,
  );
});

test("Unknown 后重建 runtime 只能恢复旧 attempt，不能创建新的 provider action", async () => {
  const harness = createHarness({
    paymentOutcomes: ["unknown", "approved"],
  });
  const first = harness.runtime.createPresenter();
  fillCreate(first);
  await first.create();
  first.destroy();

  const recovered = harness.rebuildRuntime().createPresenter();
  await recovered.load();

  assert.equal(harness.payments.beginCalls, 2);
  assert.equal(harness.payments.charges, 1);
  assert.equal(
    harness.payments.actions[0]?.actionId,
    harness.payments.actions[1]?.actionId,
  );
  assert.equal(harness.api.createCalls.length, 1);
});

test("券 create/repayment 仅提交券码并在 action insert 前按稳定 actionId 写 vault", async () => {
  const createHarnessInstance = createHarness();
  const createPresenter = createHarnessInstance.runtime.createPresenter();
  fillVoucherCreate(createPresenter);

  await createPresenter.create();

  const createCandidate =
    createHarnessInstance.actionStore.createdCandidates[0];
  const createStage = createHarnessInstance.voucherIntents.calls[0];
  assert.ok(createCandidate);
  assert.ok(createStage);
  assert.equal(createStage.actionId, createCandidate.action.actionId);
  assert.equal(createStage.paymentGuid, createCandidate.action.paymentGuid);
  assert.equal(createStage.installmentGuid, createCandidate.action.installmentGuid);
  assert.equal(createStage.storeCode, STORE_CODE);
  assert.equal(createStage.deviceCode, DEVICE_CODE);
  assert.equal(createStage.cashierId, "CASHIER-1");
  assert.equal(createStage.amountCents, 2_000);
  assert.equal(createStage.voucherReference, "VOUCHER-SECRET-CREATE");
  assert.equal(createStage.voucherReservationToken, null);
  assert.ok(
    createHarnessInstance.events.indexOf("voucher-stage") <
      createHarnessInstance.events.indexOf("action-create"),
  );
  assert.ok(
    createHarnessInstance.events.indexOf("action-create") <
      createHarnessInstance.events.indexOf(
        "action-transition:ProviderPending",
      ),
  );
  assert.match(
    createCandidate.intentFingerprint,
    /^sha256:[0-9a-f]{64}$/,
  );
  const createPersistedJson = JSON.stringify(createCandidate);
  assert.equal(createPersistedJson.includes("VOUCHER-SECRET-CREATE"), false);
  const createVoucherMaterial = createHarnessInstance.hashMaterials[0];
  const createFingerprintMaterial =
    createHarnessInstance.hashMaterials[1];
  assert.ok(createVoucherMaterial);
  assert.ok(createFingerprintMaterial);
  const createVoucherDigest = `sha256:${createHash("sha256")
    .update(createVoucherMaterial, "utf8")
    .digest("hex")}`;
  assert.equal(
    createFingerprintMaterial.includes(createVoucherDigest),
    true,
  );
  assert.equal(
    createFingerprintMaterial.includes("VOUCHER-SECRET-CREATE"),
    false,
  );
  assert.equal(createVoucherMaterial.includes('"reservationToken":null'), true);

  const repaymentHarness = createHarness();
  const repaymentPresenter = repaymentHarness.runtime.createPresenter();
  await repaymentPresenter.select("installment-1");
  fillVoucherRepayment(repaymentPresenter);

  await repaymentPresenter.addRepayment();

  const repaymentCandidate =
    repaymentHarness.actionStore.createdCandidates[0];
  const repaymentStage = repaymentHarness.voucherIntents.calls[0];
  assert.ok(repaymentCandidate);
  assert.ok(repaymentStage);
  assert.equal(repaymentStage.actionId, repaymentCandidate.action.actionId);
  assert.equal(repaymentStage.paymentGuid, repaymentCandidate.action.paymentGuid);
  assert.equal(repaymentStage.installmentGuid, "installment-1");
  assert.equal(repaymentStage.amountCents, 1_000);
  assert.equal(repaymentStage.voucherReference, "VOUCHER-SECRET-REPAY");
  assert.equal(repaymentStage.voucherReservationToken, null);
  assert.match(
    repaymentCandidate.intentFingerprint,
    /^sha256:[0-9a-f]{64}$/,
  );
  assert.equal(
    JSON.stringify(repaymentCandidate).includes("VOUCHER-SECRET-REPAY"),
    false,
  );
});

test("券 intent 支持 WPF 等价的 code-only 输入，reservationToken 通常为 null", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();

  await workflowOf(presenter).create({
    draftRevision: 1,
    customerName: "Customer",
    customerPhone: "0400000000",
    note: null,
    downPaymentCents: 2_000,
    method: "voucher",
    voucherReference: "VOUCHER-CODE-ONLY",
    voucherReservationToken: null,
  });

  assert.equal(harness.voucherIntents.calls.length, 1);
  assert.equal(
    harness.voucherIntents.calls[0]?.voucherReservationToken,
    null,
  );
  assert.equal(
    harness.hashMaterials[0]?.includes('"reservationToken":null'),
    true,
  );
  assert.equal(
    JSON.stringify(harness.actionStore.createdCandidates[0]).includes(
      "VOUCHER-CODE-ONLY",
    ),
    false,
  );
});

test("券 vault stage 失败时不插入 action、不进入 ProviderPending 且不把错误 secret 暴露为状态", async () => {
  const harness = createHarness();
  harness.voucherIntents.failure = new Error(
    "vault failed for VOUCHER-SECRET-FAIL",
  );
  const presenter = harness.runtime.createPresenter();
  fillVoucherCreate(presenter, "VOUCHER-SECRET-FAIL");

  await presenter.create();

  assert.equal(harness.voucherIntents.calls.length, 1);
  assert.equal(harness.actionStore.createdCandidates.length, 0);
  assert.equal(harness.actionStore.transitionCalls.length, 0);
  assert.equal(harness.payments.beginCalls, 0);
  assert.equal(presenter.getState().statusCode, "action-failed");
});

test("券 Unknown/崩溃恢复沿用同一 actionId 且不再次 stage", async () => {
  const harness = createHarness({
    paymentOutcomes: ["unknown", "approved"],
  });
  const first = harness.runtime.createPresenter();
  fillVoucherCreate(first);
  await first.create();
  first.destroy();

  const recovered = harness.rebuildRuntime().createPresenter();
  await recovered.load();

  assert.equal(harness.voucherIntents.calls.length, 1);
  assert.equal(harness.payments.actions.length, 2);
  assert.equal(
    harness.payments.actions[0]?.actionId,
    harness.payments.actions[1]?.actionId,
  );
  assert.equal(
    harness.voucherIntents.calls[0]?.actionId,
    harness.payments.actions[0]?.actionId,
  );
});

test("terminal scope 竞争时 losing 券 intent 保持原 actionId，不重绑 winning action", async () => {
  const harness = createHarness();
  harness.actionStore.raceOnNextCreate = true;
  const presenter = harness.runtime.createPresenter();
  fillVoucherCreate(presenter);

  await presenter.create();

  const losingActionId = harness.voucherIntents.calls[0]?.actionId;
  const winningActionId = harness.actionStore.raceWinner?.action.actionId;
  assert.ok(losingActionId);
  assert.ok(winningActionId);
  assert.notEqual(losingActionId, winningActionId);
  assert.equal(harness.voucherIntents.calls.length, 1);
  assert.equal(harness.payments.actions[0]?.actionId, winningActionId);
  assert.equal(
    harness.voucherIntents.calls.some(
      (call) => call.actionId === winningActionId,
    ),
    false,
  );
});

test("现金和卡 action 不写 voucher vault，fingerprint 仍为不可逆摘要", async () => {
  const cash = createHarness();
  const cashPresenter = cash.runtime.createPresenter();
  fillCreate(cashPresenter);
  await cashPresenter.create();

  assert.equal(cash.voucherIntents.calls.length, 0);
  assert.match(
    cash.actionStore.createdCandidates[0]?.intentFingerprint ?? "",
    /^sha256:[0-9a-f]{64}$/,
  );

  const card = createHarness();
  const cardPresenter = card.runtime.createPresenter();
  fillCreate(cardPresenter);
  cardPresenter.setCreatePaymentMethod("card");
  await cardPresenter.create();

  assert.equal(card.voucherIntents.calls.length, 0);
  assert.match(
    card.actionStore.createdCandidates[0]?.intentFingerprint ?? "",
    /^sha256:[0-9a-f]{64}$/,
  );
});

test("Approved/backend pending 后即使重建 runtime 并修改 note，也只重放冻结旧 command", async () => {
  const harness = createHarness({ createFailsOnce: true });
  const first = harness.runtime.createPresenter();
  fillCreate(first);
  first.setCreateNote("Original note");
  await first.create();
  first.destroy();

  const recovered = harness.rebuildRuntime().createPresenter();
  fillCreate(recovered);
  recovered.setCreateNote("Changed note");
  await recovered.create();

  assert.equal(harness.payments.charges, 1);
  assert.equal(harness.api.createCalls.length, 2);
  assert.equal(harness.api.createCalls[0]?.note, "Original note");
  assert.equal(harness.api.createCalls[1]?.note, "Original note");
});

test("mutation 回包必须关联冻结 action、terminal、approved payment/refund 与目标状态", async () => {
  const mismatchedCreate = createHarness({
    createResult: details({
      installmentGuid: "90000000-0000-4000-8000-000000000001",
    }),
  });
  const createPresenter = mismatchedCreate.runtime.createPresenter();
  fillCreate(createPresenter);
  await createPresenter.create();
  assert.equal(
    createPresenter.getState().statusCode,
    "payment-recovery-required",
  );
  assert.equal(mismatchedCreate.cart.clearCalls, 0);

  const missingRepayment = createHarness({
    appendResultMode: "missing-payment",
  });
  const repaymentPresenter = missingRepayment.runtime.createPresenter();
  await repaymentPresenter.select("installment-1");
  repaymentPresenter.setRepaymentAmount("10.00");
  await repaymentPresenter.addRepayment();
  assert.equal(
    repaymentPresenter.getState().statusCode,
    "payment-recovery-required",
  );

  const invalidCancel = createHarness({
    cancelResultMode: "active-without-refund",
  });
  const cancelPresenter = invalidCancel.runtime.createPresenter();
  await cancelPresenter.select("installment-1");
  await cancelPresenter.cancelWithRefund();
  assert.equal(
    cancelPresenter.getState().statusCode,
    "payment-recovery-required",
  );
});

test("cancel/refund 只把 durable action 交给支付 port，不从脱敏详情伪造原卡引用", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();
  await presenter.select("installment-1");
  await presenter.cancelWithRefund();

  const action = harness.payments.actions[0];
  assert.deepEqual(
    Object.keys(action ?? {}).sort(),
    [
      "actionId",
      "amountCents",
      "idempotencyKey",
      "installmentGuid",
      "kind",
      "method",
      "paymentGuid",
    ],
  );
  assert.equal(action?.kind, "cancel-refund");
  assert.equal(harness.api.cancelCalls.length, 1);
  assert.equal(
    harness.api.cancelCalls[0]?.idempotencyKey,
    action?.idempotencyKey,
  );
});

test("首次还款在 provider 前重新读取详情并精确拒绝 GUID、门店或设备 scope 不匹配", async () => {
  const mismatches: readonly Partial<InstallmentDetails>[] = [
    { installmentGuid: "installment-other" },
    { storeCode: "STORE-OTHER" },
    { deviceCode: "DEVICE-OTHER" },
  ];

  for (const mismatch of mismatches) {
    const harness = createHarness();
    const presenter = harness.runtime.createPresenter();
    await presenter.select("installment-1");
    harness.api.detailsResponse = details(mismatch);
    presenter.setRepaymentAmount("10.00");

    await presenter.addRepayment();

    assert.equal(harness.api.detailsCalls.length, 2);
    assert.equal(harness.payments.beginCalls, 0);
    assert.equal(harness.actionStore.createdCandidates.length, 0);
    assert.equal(await harness.runtime.hasRecoveryRequired(), false);
    assert.equal(presenter.getState().statusCode, "conflict");
    assert.equal(
      harness.actionStore.transitionCalls.some(
        (call) => call.nextState === "ProviderPending",
      ),
      false,
    );
  }
});

test("首次还款或退款的详情复核读取失败时 fail closed，provider 零调用", async () => {
  for (const operation of ["repayment", "refund"] as const) {
    for (const failure of ["transport", "missing"] as const) {
      const harness = createHarness();
      const presenter = harness.runtime.createPresenter();
      await presenter.select("installment-1");
      if (failure === "transport") {
        harness.api.detailsError = new HbposApiError("scope lookup failed", {
          kind: "transport",
        });
      } else {
        harness.api.detailsResponse = null;
      }

      if (operation === "repayment") {
        presenter.setRepaymentAmount("10.00");
        await presenter.addRepayment();
      } else {
        await presenter.cancelWithRefund();
      }

      assert.equal(harness.api.detailsCalls.length, 2);
      assert.equal(harness.payments.beginCalls, 0);
      assert.equal(harness.api.cancelCalls.length, 0);
      assert.equal(harness.actionStore.createdCandidates.length, 0);
      assert.equal(await harness.runtime.hasRecoveryRequired(), false);
      assert.equal(
        presenter.getState().statusCode,
        failure === "transport" ? "online-required" : "conflict",
      );
    }
  }
});

test("券还款 scope 预检失败时不生成候选券材料或 durable action", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();
  await presenter.select("installment-1");
  harness.api.detailsResponse = details({ deviceCode: "DEVICE-OTHER" });
  fillVoucherRepayment(presenter);

  await presenter.addRepayment();

  assert.equal(harness.voucherIntents.calls.length, 0);
  assert.equal(harness.actionStore.createdCandidates.length, 0);
  assert.equal(harness.payments.beginCalls, 0);
  assert.equal(await harness.runtime.hasRecoveryRequired(), false);
  assert.equal(presenter.getState().statusCode, "conflict");
});

test("首次退款在 provider 前拒绝跨设备分期，但同设备正常流不受影响", async () => {
  const denied = createHarness();
  const deniedPresenter = denied.runtime.createPresenter();
  await deniedPresenter.select("installment-1");
  denied.api.detailsResponse = details({ deviceCode: "DEVICE-OTHER" });

  await deniedPresenter.cancelWithRefund();

  assert.equal(denied.payments.beginCalls, 0);
  assert.equal(denied.api.cancelCalls.length, 0);
  assert.equal(denied.actionStore.createdCandidates.length, 0);
  assert.equal(await denied.runtime.hasRecoveryRequired(), false);
  assert.equal(deniedPresenter.getState().statusCode, "conflict");

  const allowed = createHarness();
  const allowedPresenter = allowed.runtime.createPresenter();
  await allowedPresenter.select("installment-1");
  await allowedPresenter.cancelWithRefund();

  assert.equal(allowed.api.detailsCalls.length, 2);
  assert.equal(allowed.payments.beginCalls, 1);
  assert.equal(allowed.api.cancelCalls.length, 1);
});

test("退款进入 BackendPending 后恢复同一 provider 结果，不再用详情读取阻断后端重放", async () => {
  const harness = createHarness({
    cancelResultMode: "active-without-refund",
  });
  const presenter = harness.runtime.createPresenter();
  await presenter.select("installment-1");

  await presenter.cancelWithRefund();
  assert.equal(presenter.getState().statusCode, "payment-recovery-required");
  assert.equal(harness.payments.beginCalls, 1);
  assert.equal(harness.api.detailsCalls.length, 2);

  harness.api.detailsError = new Error("details endpoint unavailable");
  await presenter.recoverBlocking();

  assert.equal(harness.payments.beginCalls, 2);
  assert.equal(harness.api.detailsCalls.length, 2);
  assert.equal(harness.api.cancelCalls.length, 2);
});

test("create 在独占 lease 内复核 revision；过期草稿不会触发支付", async () => {
  const harness = createHarness();
  const presenter = harness.runtime.createPresenter();
  fillCreate(presenter);
  harness.cart.bumpRevisionWithoutNotification();

  await presenter.create();
  assert.equal(harness.payments.beginCalls, 0);
  assert.equal(harness.api.createCalls.length, 0);
  assert.equal(presenter.getState().statusCode, "conflict");
});

test("写操作在执行时复核 online、权限与 cashier lease", async () => {
  const offline = createHarness();
  const offlinePresenter = offline.runtime.createPresenter();
  fillCreate(offlinePresenter);
  offline.online.value = false;
  await offlinePresenter.create();
  assert.equal(offline.payments.beginCalls, 0);
  assert.equal(offlinePresenter.getState().statusCode, "online-required");

  const expired = createHarness();
  const expiredPresenter = expired.runtime.createPresenter();
  fillCreate(expiredPresenter);
  expired.currentCashier.clear();
  await expiredPresenter.create();
  assert.equal(expired.payments.beginCalls, 0);
  assert.equal(expired.api.createCalls.length, 0);

  const denied = createHarness({ permissions: [INSTALLMENTS_VIEW_PERMISSION] });
  const deniedPresenter = denied.runtime.createPresenter();
  deniedPresenter.showCreate();
  assert.equal(deniedPresenter.getState().statusCode, "permission-required");
});

class ScriptedPayments implements InstallmentMutationPaymentPort {
  public beginCalls = 0;
  public charges = 0;
  public readonly actions: InstallmentPaymentAction[] = [];
  private readonly chargedActionIds = new Set<string>();

  private readonly outcomes: ("approved" | "unknown")[];

  public constructor(
    outcomes: readonly ("approved" | "unknown")[],
    private readonly actionStore: MemoryInstallmentActionStore,
    private readonly events: string[],
  ) {
    this.outcomes = [...outcomes];
  }

  public beginOrRecover(persistedActionId: string) {
    return this.resolve(persistedActionId);
  }

  public recoverBlocking(persistedActionId: string) {
    return this.resolve(persistedActionId);
  }

  private async resolve(persistedActionId: string) {
    this.beginCalls += 1;
    this.events.push("payments-begin");
    const persisted = this.actionStore.get(persistedActionId);
    if (!persisted) throw new Error("persisted action is required");
    const action = persisted.action;
    this.actions.push(action);
    const outcome =
      this.outcomes.length > 1
        ? this.outcomes.shift()
        : this.outcomes[0] ?? "approved";
    if (outcome === "unknown") return { kind: "unknown" as const };
    if (!this.chargedActionIds.has(action.actionId)) {
      this.chargedActionIds.add(action.actionId);
      this.charges += 1;
    }
    if (action.kind === "cancel-refund") {
      const refund: InstallmentApprovedRefund = {
        refund: {
          paymentGuid: "10000000-0000-4000-8000-000000000001",
          method: "cash",
          amountCents: 2_000,
          reference: null,
          cardTransactions: [],
          idempotencyKey: action.idempotencyKey,
        },
        originalTenderEvidenceId: "tender-evidence-1",
        refundAttemptId: "refund-attempt-1",
        sourceAttemptId: "source-attempt-1",
        sourcePaymentGuid: "20000000-0000-4000-8000-000000000001",
      };
      return {
        kind: "approved" as const,
        refunds: [refund],
      };
    }
    const payment: InstallmentPaymentCommand = {
      paymentGuid: action.paymentGuid ?? "invalid-payment-guid",
      method: action.method ?? "cash",
      amountCents: action.amountCents ?? 0,
      reference: null,
      reservationToken: null,
      cardTransactions: [],
      idempotencyKey: action.idempotencyKey,
    };
    return { kind: "approved" as const, payment };
  }
}

class MemorySnapshotCache {
  public readonly listCalls: { storeCode: string; limit: number; offset: number }[] = [];
  public readonly upsertCalls: { storeCode: string; snapshots: readonly InstallmentSnapshot[] }[] = [];

  public constructor(private snapshots: readonly InstallmentSnapshot[]) {}

  public async listForStore(storeCode: string, limit: number, offset: number) {
    this.listCalls.push({ storeCode, limit, offset });
    return this.snapshots.slice(offset, offset + limit);
  }

  public async upsertForStore(storeCode: string, snapshots: readonly InstallmentSnapshot[]) {
    this.upsertCalls.push({ storeCode, snapshots });
    const incoming = new Map(
      snapshots.map((snapshot) => [snapshot.installmentGuid, snapshot]),
    );
    this.snapshots = [
      ...snapshots,
      ...this.snapshots.filter(
        (snapshot) => !incoming.has(snapshot.installmentGuid),
      ),
    ];
  }

  public all(): readonly InstallmentSnapshot[] {
    return this.snapshots;
  }
}

class MemoryInstallmentActionStore implements InstallmentActionStorePort {
  private current: PersistedInstallmentAction | null = null;
  public readonly createdCandidates: PersistedInstallmentAction[] = [];
  public readonly transitionCalls: Parameters<
    InstallmentActionStorePort["transition"]
  >[0][] = [];
  public raceOnNextCreate = false;
  public raceWinner: PersistedInstallmentAction | null = null;

  public constructor(private readonly events: string[]) {}

  public async loadBlocking(): Promise<PersistedInstallmentAction | null> {
    return this.current;
  }

  public async createIfNone(candidate: PersistedInstallmentAction) {
    this.events.push("action-create");
    if (this.current) {
      return { created: false, action: this.current };
    }
    if (this.raceOnNextCreate) {
      this.raceOnNextCreate = false;
      const winnerActionId = "f0000000-0000-4000-8000-000000000001";
      const winnerPaymentGuid =
        candidate.action.paymentGuid === null
          ? null
          : "f0000000-0000-4000-8000-000000000002";
      this.raceWinner = Object.freeze({
        ...candidate,
        action: Object.freeze({
          ...candidate.action,
          actionId: winnerActionId,
          idempotencyKey: winnerActionId,
          paymentGuid: winnerPaymentGuid,
        }),
        intentFingerprint: `sha256:${"f".repeat(64)}`,
      });
      this.current = this.raceWinner;
      return { created: false, action: this.raceWinner };
    }
    this.createdCandidates.push(candidate);
    this.current = candidate;
    return { created: true, action: candidate };
  }

  public async transition(input: Parameters<InstallmentActionStorePort["transition"]>[0]) {
    this.transitionCalls.push(input);
    this.events.push(`action-transition:${input.nextState}`);
    const current = this.requireCurrent(input.actionId);
    if (current.state !== input.expectedState) {
      throw new Error(
        `state conflict: expected ${input.expectedState}, got ${current.state}`,
      );
    }
    this.current = Object.freeze({
      ...current,
      state: input.nextState,
    });
    return this.current;
  }

  public async decline(input: Parameters<InstallmentActionStorePort["decline"]>[0]) {
    const current = this.requireCurrent(input.actionId);
    if (current.state !== input.expectedState) {
      throw new Error("decline state conflict");
    }
    this.current = null;
  }

  public async complete(input: Parameters<InstallmentActionStorePort["complete"]>[0]) {
    const current = this.requireCurrent(input.actionId);
    if (current.state !== input.expectedState) {
      throw new Error("complete state conflict");
    }
    this.current = null;
  }

  public get(actionId: string): PersistedInstallmentAction | null {
    return this.current?.action.actionId === actionId ? this.current : null;
  }

  private requireCurrent(actionId: string): PersistedInstallmentAction {
    if (!this.current || this.current.action.actionId !== actionId) {
      throw new Error("persisted action not found");
    }
    return this.current;
  }
}

type VoucherIntentStageInput = Parameters<
  InstallmentVoucherIntentVaultPort["stage"]
>[0];

class RecordingVoucherIntentVault
  implements InstallmentVoucherIntentVaultPort
{
  public readonly calls: VoucherIntentStageInput[] = [];
  public failure: Error | null = null;

  public constructor(private readonly events: string[]) {}

  public async stage(input: VoucherIntentStageInput): Promise<void> {
    this.events.push("voucher-stage");
    this.calls.push(Object.freeze({ ...input }));
    if (this.failure) throw this.failure;
  }
}

class FakeCart {
  public clearCalls = 0;
  private readonly listeners = new Set<() => void>();
  private revision = 1;

  public read = () => this.snapshot();

  public subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public async runExclusive<T>(operation: (lease: { read(): unknown; clearAfterCommittedOrder(orderGuid: string): unknown }) => T | Promise<T>): Promise<T> {
    return operation({
      read: () => this.snapshot(),
      clearAfterCommittedOrder: () => {
        this.clearCalls += 1;
        this.revision += 1;
        for (const listener of this.listeners) listener();
      },
    });
  }

  public bumpRevisionWithoutNotification(): void {
    this.revision += 1;
  }

  private snapshot() {
    const cart = {
      revision: this.revision,
      mode: "sale",
      lines: [
        {
          lineId: "line-1",
          productCode: "P1",
          itemNumber: "ITEM-1",
          lookupCode: "P1",
          displayName: "Product 1",
          quantity: "1",
          unitPrice: { currency: "AUD", cents: 5_000 },
          discount: { currency: "AUD", cents: 0 },
          actualAmount: { currency: "AUD", cents: 5_000 },
          priceSource: "catalog",
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
        },
      ],
      subtotal: { currency: "AUD", cents: 5_000 },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: 5_000 },
    };
    return { cart, pricingState: { revision: this.revision } } as unknown;
  }
}

class ScriptedApi {
  public listCalls = 0;
  public readonly listInputs: unknown[] = [];
  public listError: Error | null = null;
  public detailsError: Error | null = null;
  public detailsResponse: InstallmentDetails | null = details();
  public readonly detailsCalls: string[] = [];
  public readonly createCalls: InstallmentCreateCommand[] = [];
  public readonly cancelCalls: InstallmentCancelCommand[] = [];

  public constructor(
    private createFailsOnce: boolean,
    private readonly createResult: InstallmentDetails | null,
    private readonly appendResultMode: "approved-payment" | "missing-payment",
    private readonly cancelResultMode:
      | "cancelled-with-refund"
      | "active-without-refund",
  ) {}

  public async list(input: unknown) {
    this.listCalls += 1;
    this.listInputs.push(input);
    if (this.listError) throw this.listError;
    return [summary()];
  }

  public async getDetails(installmentGuid: string) {
    this.detailsCalls.push(installmentGuid);
    if (this.detailsError) throw this.detailsError;
    return this.detailsResponse;
  }

  public async create(command: InstallmentCreateCommand) {
    this.createCalls.push(command);
    if (this.createFailsOnce) {
      this.createFailsOnce = false;
      throw new Error("backend write failed after approved payment");
    }
    return (
      this.createResult ??
      details({
        installmentGuid: command.installmentGuid,
        deviceCode: command.deviceCode,
        cashierId: command.cashierId,
        cashierName: command.cashierName,
        customerName: command.customerName,
        customerPhone: command.customerPhone,
        totalCents: command.totalCents,
        downPaymentCents: command.downPaymentCents,
        paidCents: command.downPayment.amountCents,
        balanceCents:
          command.totalCents - command.downPayment.amountCents,
        status:
          command.totalCents === command.downPayment.amountCents
            ? "PaidOff"
            : "Active",
        lines: command.lines,
        payments: [
          recordedPayment(command.downPayment),
        ],
      })
    );
  }

  public async appendPayment(command: {
    installmentGuid: string;
    payment: InstallmentPaymentCommand;
  }) {
    return details({
      installmentGuid: command.installmentGuid,
      payments:
        this.appendResultMode === "approved-payment"
          ? [recordedPayment(command.payment)]
          : [],
    });
  }
  public async cancelWithRefund(command: InstallmentCancelCommand) {
    this.cancelCalls.push(command);
    if (this.cancelResultMode === "active-without-refund") {
      return details({ installmentGuid: command.installmentGuid });
    }
    return details({
      installmentGuid: command.installmentGuid,
      status: "Cancelled",
      balanceCents: 0,
      cancellationInfo: {
        kind: "RefundCancel",
        cancelledAtIso: command.cancelledAtIso,
        cancelledBy: command.cashierName,
        reason: command.reason,
      },
      payments: command.refunds.map((refund) => ({
          paymentGuid: refund.paymentGuid,
          method: refund.method,
          amountCents: -refund.amountCents,
          status: "Recorded",
          recordedAtIso: "2026-07-28T08:00:00.000Z",
          cashierId: command.cashierId,
          deviceCode: command.deviceCode,
          cardType: null,
          maskedCardNumber: null,
        })),
    });
  }
  public async void(command: { installmentGuid: string }) {
    return details({
      installmentGuid: command.installmentGuid,
      status: "Cancelled",
      cancellationInfo: {
        kind: "VoidCancel",
        cancelledAtIso: "2026-07-28T08:00:00.000Z",
        cancelledBy: "Cashier One",
        reason: "void",
      },
    });
  }
  public async confirmPickup(command: { installmentGuid: string }) {
    return details({
      installmentGuid: command.installmentGuid,
      status: "PickedUp",
    });
  }
}

function createHarness(options: Readonly<{
  appendResultMode?: "approved-payment" | "missing-payment";
  cached?: readonly InstallmentSnapshot[];
  cancelResultMode?: "cancelled-with-refund" | "active-without-refund";
  createFailsOnce?: boolean;
  createResult?: InstallmentDetails;
  paymentOutcome?: "approved" | "unknown";
  paymentOutcomes?: readonly ("approved" | "unknown")[];
  permissions?: readonly string[];
  receiptReprint?: InstallmentReceiptReprintRuntimePort;
}> = {}) {
  const currentCashier = new CurrentCashierSession();
  activateCashier(currentCashier, options.permissions ?? ALL_PERMISSIONS);
  const cart = new FakeCart();
  const online = { value: true };
  const api = new ScriptedApi(
    options.createFailsOnce ?? false,
    options.createResult ?? null,
    options.appendResultMode ?? "approved-payment",
    options.cancelResultMode ?? "cancelled-with-refund",
  );
  const cache = new MemorySnapshotCache(options.cached ?? []);
  const events: string[] = [];
  const hashMaterials: string[] = [];
  const actionStore = new MemoryInstallmentActionStore(events);
  const voucherIntents = new RecordingVoucherIntentVault(events);
  const payments = new ScriptedPayments(
    options.paymentOutcomes ?? [options.paymentOutcome ?? "approved"],
    actionStore,
    events,
  );
  let id = 0;
  const dependencies: ProductionInstallmentRuntimeDependencies = {
    currentCashier,
    terminal: { storeCode: STORE_CODE, deviceCode: DEVICE_CODE },
    activeCart: cart as unknown as ProductionInstallmentRuntimeDependencies["activeCart"],
    connectivity: { isOnline: async () => online.value },
    api: api as unknown as InstallmentsRemotePort,
    snapshotCache: cache,
    actionStore,
    payments,
    ...(options.receiptReprint
      ? { receiptReprint: options.receiptReprint }
      : {}),
    voucherIntents,
    sha256Hex: async (material: string) => {
      hashMaterials.push(material);
      return createHash("sha256").update(material, "utf8").digest("hex");
    },
    createId: () => `00000000-0000-4000-8000-${String(++id).padStart(12, "0")}`,
    businessTimeZone: "Australia/Brisbane",
    now: () => new Date("2026-07-28T08:00:00.000Z"),
    nowIso: () => "2026-07-28T08:00:00.000Z",
  };
  return {
    currentCashier,
    cart,
    online,
    api,
    cache,
    payments,
    actionStore,
    events,
    hashMaterials,
    voucherIntents,
    runtime: createProductionInstallmentRuntime(dependencies),
    rebuildRuntime: () => createProductionInstallmentRuntime(dependencies),
  };
}

function activateCashier(session: CurrentCashierSession, permissions: readonly string[]): void {
  const epoch = session.beginAuthentication();
  session.activate(epoch, {
    source: "online",
    session: {
      cashierId: "CASHIER-1",
      userGuid: null,
      cashierName: "Cashier One",
      storeCode: STORE_CODE,
      deviceCode: DEVICE_CODE,
      permissionCodes: [...permissions],
    },
  } satisfies CashierLoginResult, { storeCode: STORE_CODE, deviceCode: DEVICE_CODE });
}

function fillCreate(presenter: InstallmentPresenter): void {
  presenter.showCreate();
  presenter.setCustomerName("Customer");
  presenter.setCustomerPhone("0400000000");
  presenter.setCreateDownPayment("20.00");
}

function fillVoucherCreate(
  presenter: InstallmentPresenter,
  voucherReference = "VOUCHER-SECRET-CREATE",
): void {
  fillCreate(presenter);
  presenter.setCreatePaymentMethod("voucher");
  presenter.setCreateVoucherReference(voucherReference);
}

function fillVoucherRepayment(presenter: InstallmentPresenter): void {
  presenter.setRepaymentAmount("10.00");
  presenter.setRepaymentMethod("voucher");
  presenter.setRepaymentVoucherReference("VOUCHER-SECRET-REPAY");
}

function workflowOf(presenter: InstallmentPresenter): InstallmentWorkflowPort {
  return (
    presenter as unknown as Readonly<{
      workflow: InstallmentWorkflowPort;
    }>
  ).workflow;
}

function summary(overrides: Partial<InstallmentSnapshot> = {}) {
  return {
    installmentGuid: "installment-1",
    installmentNumber: "IP-001",
    storeCode: STORE_CODE,
    deviceCode: DEVICE_CODE,
    cashierName: "Cashier One",
    customerName: "Customer",
    customerPhone: "0400000000",
    createdAtIso: "2026-07-28T08:00:00.000Z",
    totalCents: 5_000,
    downPaymentCents: 2_000,
    paidCents: 2_000,
    balanceCents: 3_000,
    status: "Active",
    updatedAtIso: "2026-07-28T08:00:00.000Z",
    ...overrides,
  } as const;
}

function snapshot(overrides: Partial<InstallmentSnapshot> = {}): InstallmentSnapshot {
  return {
    ...summary(overrides),
    note: null,
    encryptedSensitiveRevision: 1,
  };
}

function details(overrides: Partial<InstallmentDetails> = {}): InstallmentDetails {
  return {
    ...summary(overrides),
    cashierId: "CASHIER-1",
    minimumDownPaymentCents: 2_000,
    lines: [],
    payments: [],
    pickupInfo: null,
    cancellationInfo: null,
    note: null,
    ...overrides,
  };
}

function recordedPayment(command: InstallmentPaymentCommand) {
  return {
    paymentGuid: command.paymentGuid,
    method: command.method,
    amountCents: command.amountCents,
    status: "Recorded" as const,
    recordedAtIso: "2026-07-28T08:00:00.000Z",
    cashierId: "CASHIER-1",
    deviceCode: DEVICE_CODE,
    cardType: null,
    maskedCardNumber: null,
  };
}
