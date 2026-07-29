import assert from "node:assert/strict";
import test from "node:test";

import {
  InstallmentPaymentAdapterError,
  ProductionInstallmentPaymentAdapter,
  type InstallmentApprovedPaymentMaterial,
  type InstallmentCashSettlement,
  type InstallmentProviderAttemptPlan,
  type InstallmentProviderAttemptRecord,
  type InstallmentProviderAttemptStorePort,
  type InstallmentRefundProvenanceRemotePort,
  type InstallmentRefundProvenanceSnapshot,
  type InstallmentVoucherMaterialPort,
} from "./production-installment-payment-adapter";
import type { PersistedInstallmentAction } from "./production-installment-runtime";

import type {
  CardSyncEvidenceV1,
  OnlinePaymentPort,
  PaymentAttempt,
  PaymentProvider,
  PaymentProviderResult,
} from "@/core/contracts";
import type { PaymentProviderRegistryPort } from "@/features/payments/payment-attempt-service";

const ACTION_ID = "10000000-0000-4000-8000-000000000001";
const INSTALLMENT_GUID = "20000000-0000-4000-8000-000000000001";
const PAYMENT_GUID = "30000000-0000-4000-8000-000000000001";
const STORE_CODE = "STORE-1";
const DEVICE_CODE = "IPAD-1";
const NOW = "2026-07-29T01:02:03.000Z";

test("现金首付/续付先耐久批准并建立原付款证据，恢复返回同一 paymentGuid", async () => {
  const store = new MemoryAttemptStore(paymentAction("cash"));
  const ids = new StableIds();
  const adapter = createAdapter({ store, ids });

  const first = await adapter.beginOrRecover(ACTION_ID);
  assert.deepEqual(first, {
    kind: "approved",
    payment: {
      paymentGuid: PAYMENT_GUID,
      method: "cash",
      amountCents: 2_500,
      reference: null,
      reservationToken: null,
      cardTransactions: [],
      idempotencyKey: ACTION_ID,
    },
  });
  assert.equal(store.plans.get(ACTION_ID)?.cashSettlements[0]?.state, "Approved");
  assert.equal(store.originalTenderWrites, 1);

  const recovered = await adapter.recoverBlocking(ACTION_ID);
  assert.deepEqual(recovered, first);
  assert.equal(store.originalTenderWrites, 1);
  assert.equal(ids.calls, 2);
});

test("银行卡 provider 必须显式且唯一配置；缺失或多选均在持久化/provider 前失败关闭", async () => {
  for (const configured of [
    [],
    ["square", "linkly-cloud"],
  ] as const) {
    const store = new MemoryAttemptStore(paymentAction("card"));
    const square = new ScriptedProvider("square");
    const linkly = new ScriptedProvider("linkly-cloud");
    const adapter = createAdapter({
      store,
      configuredCardProviders: configured,
      providers: new ProviderRegistry(square, linkly),
    });

    await assert.rejects(
      () => adapter.beginOrRecover(ACTION_ID),
      (error) =>
        error instanceof InstallmentPaymentAdapterError &&
        error.code === "INSTALLMENT_CARD_PROVIDER_SELECTION_INVALID",
    );
    assert.equal(store.plans.size, 0);
    assert.equal(square.calls.length + linkly.calls.length, 0);
  }
});

test("新版 action 已冻结 Linkly 时，重启后配置切为 Square 仍绑定并执行原 adapter", async () => {
  const store = new MemoryAttemptStore(
    paymentAction("card", "linkly-cloud"),
  );
  const square = new ScriptedProvider("square");
  const linkly = new ScriptedProvider("linkly-cloud");
  linkly.submitResults.push(
    approvedCardResult(
      "linkly-cloud",
      { sessionId: "SESSION-PROTECTED", txnRef: "TXN-LINKLY" },
      cardEvidence("linkly-cloud", "purchase", 2_500),
    ),
  );
  const adapter = createAdapter({
    store,
    configuredCardProviders: ["square"],
    providers: new ProviderRegistry(square, linkly),
  });

  assert.equal((await adapter.beginOrRecover(ACTION_ID)).kind, "approved");
  assert.equal(square.calls.length, 0);
  assert.equal(linkly.calls.length, 1);
  assert.equal(
    store.plans.get(ACTION_ID)?.attempts[0]?.attempt.provider,
    "linkly-cloud",
  );
});

test("新版 action 冻结的 card provider adapter 缺失时失败关闭且不绑定 plan", async () => {
  const store = new MemoryAttemptStore(
    paymentAction("card", "linkly-cloud"),
  );
  const square = new ScriptedProvider("square");
  const adapter = createAdapter({
    store,
    configuredCardProviders: ["square"],
    providers: new ProviderRegistry(square),
  });

  await assert.rejects(
    () => adapter.beginOrRecover(ACTION_ID),
    (error) =>
      error instanceof InstallmentPaymentAdapterError &&
      error.code === "INSTALLMENT_CARD_PROVIDER_SELECTION_INVALID",
  );
  assert.equal(store.plans.size, 0);
  assert.equal(square.calls.length, 0);
});

test("Square 扣款在 Submitted CAS 后执行，批准证据加密写入后映射 WPF payment command", async () => {
  const store = new MemoryAttemptStore(paymentAction("card"));
  const square = new ScriptedProvider("square");
  square.submitResults.push(
    approvedCardResult(
      "square",
      {
        checkoutId: "checkout-protected",
        paymentId: "payment-protected",
      },
      cardEvidence("square", "purchase", 2_500),
    ),
  );
  const adapter = createAdapter({
    store,
    providers: new ProviderRegistry(square),
    configuredCardProviders: ["square"],
  });

  const result = await adapter.beginOrRecover(ACTION_ID);

  assert.equal(store.events[0], "bind-plan");
  assert.equal(store.events[1], "attempt:Created->Submitted");
  assert.equal(square.calls[0]?.kind, "submit");
  assert.equal(square.calls[0]?.attempt.state, "Submitted");
  assert.deepEqual(result, {
    kind: "approved",
    payment: {
      paymentGuid: PAYMENT_GUID,
      method: "card",
      amountCents: 2_500,
      reference: "TXN-square-purchase",
      reservationToken: null,
      cardTransactions: [
        {
          processor: "Square",
          txnRef: "TXN-square-purchase",
          authCode: "AUTH-PROTECTED",
          cardType: "VISA",
          cardBin: 411111,
          maskedCardNumber: "****1111",
          merchantId: "MID-PROTECTED",
          responseCode: "00",
          responseText: "APPROVED",
          stan: "STAN-PROTECTED",
          bankDateTime: NOW,
          amount: 25,
          receiptText: "CARD RECEIPT",
          refundReference: null,
        },
      ],
      idempotencyKey: ACTION_ID,
    },
  });
  assert.equal(store.approvedMaterialWrites, 1);

  await adapter.recoverBlocking(ACTION_ID);
  assert.equal(square.calls.length, 1);
});

test("Linkly Pending/Unknown 只恢复同一 attempt，Approved 前不创建第二套身份", async () => {
  const store = new MemoryAttemptStore(paymentAction("card"));
  const linkly = new ScriptedProvider("linkly-cloud");
  linkly.submitResults.push(
    providerResult("Pending", {
      sessionId: "SESSION-PROTECTED",
      txnRef: "TXN-linkly-cloud-purchase",
    }),
  );
  linkly.recoverResults.push(
    approvedCardResult(
      "linkly-cloud",
      {
        sessionId: "SESSION-PROTECTED",
        txnRef: "TXN-linkly-cloud-purchase",
        rfn: "RFN-PROTECTED",
      },
      cardEvidence("linkly-cloud", "purchase", 2_500),
    ),
  );
  const ids = new StableIds();
  const adapter = createAdapter({
    store,
    ids,
    providers: new ProviderRegistry(linkly),
    configuredCardProviders: ["linkly-cloud"],
  });

  assert.deepEqual(await adapter.beginOrRecover(ACTION_ID), { kind: "unknown" });
  const attemptId =
    store.plans.get(ACTION_ID)?.attempts[0]?.attempt.attemptId;
  assert.ok(attemptId);
  assert.equal(
    store.plans.get(ACTION_ID)?.attempts[0]?.attempt.state,
    "Pending",
  );

  const recovered = await adapter.recoverBlocking(ACTION_ID);
  assert.equal(recovered.kind, "approved");
  assert.equal(linkly.calls[1]?.kind, "recover");
  assert.equal(linkly.calls[1]?.attempt.attemptId, attemptId);
  assert.equal(ids.calls, 3);
});

test("券码和 reservation token 只由受保护材料 Port 解析，公开 attempt 仅持有 opaque handle", async () => {
  const store = new MemoryAttemptStore(paymentAction("voucher"));
  const voucher = new ScriptedProvider("voucher");
  voucher.submitResults.push(
    providerResult("Approved", {
      voucherReservationToken: "vpr_opaque_handle",
    }),
  );
  const voucherMaterials = new FakeVoucherMaterials();
  voucherMaterials.approved = {
    reference: "VOUCHER-SECRET",
    reservationToken: "TOKEN-SECRET",
  };
  const adapter = createAdapter({
    store,
    providers: new ProviderRegistry(voucher),
    voucherMaterials,
  });

  const result = await adapter.beginOrRecover(ACTION_ID);

  assert.equal(voucherMaterials.prepareCalls.length, 1);
  assert.equal(voucherMaterials.resolveCalls.length, 1);
  assert.equal(
    JSON.stringify(store.plans.get(ACTION_ID)).includes("VOUCHER-SECRET"),
    false,
  );
  assert.equal(
    JSON.stringify(store.plans.get(ACTION_ID)).includes("TOKEN-SECRET"),
    false,
  );
  assert.equal(store.protectedMaterialJson.includes("VOUCHER-SECRET"), true);
  assert.deepEqual(result, {
    kind: "approved",
    payment: {
      paymentGuid: PAYMENT_GUID,
      method: "voucher",
      amountCents: 2_500,
      reference: "VOUCHER-SECRET",
      reservationToken: "TOKEN-SECRET",
      cardTransactions: [],
      idempotencyKey: ACTION_ID,
    },
  });
});

test("取消按 paymentGuid 精确绑定现金/Square/券原付款，先完成 provider 退款再批准现金退款", async () => {
  const store = new MemoryAttemptStore(cancelAction());
  const provenance = new FakeProvenance(
    provenanceSnapshot([
      originalTender("cash", 500, "01"),
      originalTender("card", 1_200, "02", "square"),
      originalTender("voucher", 800, "03", "voucher"),
    ]),
  );
  const square = new ScriptedProvider("square");
  square.refundResults.push(
    approvedCardResult(
      "square",
      {
        paymentId: "ORIGINAL-SQUARE-PAYMENT",
      },
      cardEvidence("square", "refund", 1_200),
    ),
  );
  const voucher = new ScriptedProvider("voucher");
  voucher.refundResults.push(
    providerResult("Approved", {
      voucherReservationToken: "vpr_refund_handle",
    }),
  );
  const voucherMaterials = new FakeVoucherMaterials();
  voucherMaterials.approved = {
    reference: "REFUND-VOUCHER-SECRET",
    reservationToken: null,
  };
  const adapter = createAdapter({
    store,
    provenance,
    providers: new ProviderRegistry(square, voucher),
    voucherMaterials,
  });

  const result = await adapter.beginOrRecover(ACTION_ID);

  assert.equal(result.kind, "approved");
  if (result.kind !== "approved" || !("refunds" in result)) {
    assert.fail("Expected approved refunds.");
  }
  assert.equal(result.refunds.length, 3);
  assert.deepEqual(
    result.refunds.map((entry) => ({
      method: entry.refund.method,
      sourcePaymentGuid: entry.sourcePaymentGuid,
      evidenceId: entry.originalTenderEvidenceId,
      idempotencyKey: entry.refund.idempotencyKey,
    })),
    [
      {
        method: "cash",
        sourcePaymentGuid: sourcePaymentGuid("01"),
        evidenceId: "evidence-01",
        idempotencyKey: ACTION_ID,
      },
      {
        method: "card",
        sourcePaymentGuid: sourcePaymentGuid("02"),
        evidenceId: "evidence-02",
        idempotencyKey: ACTION_ID,
      },
      {
        method: "voucher",
        sourcePaymentGuid: sourcePaymentGuid("03"),
        evidenceId: "evidence-03",
        idempotencyKey: ACTION_ID,
      },
    ],
  );
  assert.equal(square.calls[0]?.attempt.operation, "refund");
  assert.equal(square.calls[0]?.attempt.amount.cents, -1_200);
  assert.equal(
    square.calls[0]?.attempt.references.paymentId,
    "ORIGINAL-SQUARE-PAYMENT",
  );
  assert.equal(voucher.calls[0]?.attempt.operation, "refund");
  assert.equal(store.cashApprovalCalls, 1);
  assert.equal(store.events.at(-1), "approve-cash");
});

test("远程 provenance 必须完整、同 scope、paymentGuid 唯一且金额按全部已记录付款闭合", async () => {
  const invalidSnapshots: InstallmentRefundProvenanceSnapshot[] = [
    {
      ...provenanceSnapshot([originalTender("cash", 500, "01")]),
      complete: false,
    },
    {
      ...provenanceSnapshot([originalTender("cash", 500, "01")]),
      storeCode: "OTHER-STORE",
    },
    provenanceSnapshot([
      originalTender("cash", 500, "01"),
      originalTender("voucher", 800, "01", "voucher"),
    ]),
    {
      ...provenanceSnapshot([originalTender("cash", 500, "01")]),
      paidAmountCents: 501,
    },
  ];

  for (const snapshot of invalidSnapshots) {
    const store = new MemoryAttemptStore(cancelAction());
    const provider = new ScriptedProvider("voucher");
    const adapter = createAdapter({
      store,
      provenance: new FakeProvenance(snapshot),
      providers: new ProviderRegistry(provider),
    });
    await assert.rejects(
      () => adapter.beginOrRecover(ACTION_ID),
      (error) =>
        error instanceof InstallmentPaymentAdapterError &&
        error.code === "INSTALLMENT_REFUND_PROVENANCE_INVALID",
    );
    assert.equal(store.plans.size, 0);
    assert.equal(provider.calls.length, 0);
  }
});

test("部分 provider 已退款后另一笔 Declined 必须保持 Unknown，禁止释放 action 或提前现金退款", async () => {
  const store = new MemoryAttemptStore(cancelAction());
  const provenance = new FakeProvenance(
    provenanceSnapshot([
      originalTender("card", 1_000, "01", "square"),
      originalTender("voucher", 500, "02", "voucher"),
      originalTender("cash", 300, "03"),
    ]),
  );
  const square = new ScriptedProvider("square");
  square.refundResults.push(
    approvedCardResult(
      "square",
      { paymentId: "ORIGINAL-SQUARE-PAYMENT" },
      cardEvidence("square", "refund", 1_000),
    ),
  );
  const voucher = new ScriptedProvider("voucher");
  voucher.refundResults.push(providerResult("Declined"));
  const adapter = createAdapter({
    store,
    provenance,
    providers: new ProviderRegistry(square, voucher),
  });

  assert.deepEqual(await adapter.beginOrRecover(ACTION_ID), { kind: "unknown" });
  assert.equal(store.cashApprovalCalls, 0);
  assert.equal(
    store.plans
      .get(ACTION_ID)
      ?.attempts.some((record) => record.attempt.state === "Approved"),
    true,
  );
});

test("provider 传输异常先耐久 Unknown；恢复只调用同一 attempt 的 recover", async () => {
  const store = new MemoryAttemptStore(paymentAction("card"));
  const square = new ScriptedProvider("square");
  square.submitErrors.push(new Error("network secret must not escape"));
  square.recoverResults.push(
    approvedCardResult(
      "square",
      { paymentId: "payment-protected" },
      cardEvidence("square", "purchase", 2_500),
    ),
  );
  const adapter = createAdapter({
    store,
    providers: new ProviderRegistry(square),
    configuredCardProviders: ["square"],
  });

  assert.deepEqual(await adapter.beginOrRecover(ACTION_ID), { kind: "unknown" });
  const attemptId =
    store.plans.get(ACTION_ID)?.attempts[0]?.attempt.attemptId;
  assert.equal(
    store.plans.get(ACTION_ID)?.attempts[0]?.attempt.state,
    "Unknown",
  );

  assert.equal((await adapter.recoverBlocking(ACTION_ID)).kind, "approved");
  assert.equal(square.calls[1]?.kind, "recover");
  assert.equal(square.calls[1]?.attempt.attemptId, attemptId);
});

test("卡批准缺少受保护证据或 provider reference 冲突时保持 Unknown", async () => {
  for (const result of [
    providerResult("Approved", { paymentId: "PAYMENT-1" }),
    approvedCardResult(
      "square",
      { paymentId: "PAYMENT-1" },
      cardEvidence("square", "purchase", 2_500),
    ),
  ]) {
    const store = new MemoryAttemptStore(paymentAction("card"));
    if (result.protectedSyncEvidence) {
      const prebound = createAttemptRecord(
        paymentAction("card"),
        "square",
        "attempt-prebound",
      );
      prebound.attempt = {
        ...prebound.attempt,
        references: {
          ...prebound.attempt.references,
          paymentId: "PAYMENT-OTHER",
        },
      };
      store.plans.set(ACTION_ID, {
        actionId: ACTION_ID,
        attempts: [prebound],
        cashSettlements: [],
      });
    }
    const square = new ScriptedProvider("square");
    square.submitResults.push(result);
    square.recoverResults.push(result);
    const adapter = createAdapter({
      store,
      providers: new ProviderRegistry(square),
      configuredCardProviders: ["square"],
    });

    assert.deepEqual(await adapter.beginOrRecover(ACTION_ID), {
      kind: "unknown",
    });
  }
});

test("非法 provider 回单不进入受保护材料或错误文本，并耐久降级为 Unknown", async () => {
  const store = new MemoryAttemptStore(paymentAction("card"));
  const square = new ScriptedProvider("square");
  square.submitResults.push({
    ...approvedCardResult(
      "square",
      { paymentId: "PAYMENT-1" },
      cardEvidence("square", "purchase", 2_500),
    ),
    receiptText: "SECRET\u0000RECEIPT",
  });
  const adapter = createAdapter({
    store,
    providers: new ProviderRegistry(square),
    configuredCardProviders: ["square"],
  });

  assert.deepEqual(await adapter.beginOrRecover(ACTION_ID), { kind: "unknown" });
  assert.equal(
    store.plans.get(ACTION_ID)?.attempts[0]?.attempt.state,
    "Unknown",
  );
  assert.equal(store.protectedMaterialJson.includes("SECRET"), false);
  assert.equal(
    JSON.stringify(store.plans.get(ACTION_ID)).includes("SECRET"),
    false,
  );
});

function createAdapter(
  overrides: Readonly<{
    store?: MemoryAttemptStore;
    ids?: StableIds;
    providers?: ProviderRegistry;
    configuredCardProviders?: readonly ("square" | "linkly-cloud")[];
    provenance?: FakeProvenance;
    voucherMaterials?: FakeVoucherMaterials;
  }> = {},
): ProductionInstallmentPaymentAdapter {
  const ids = overrides.ids ?? new StableIds();
  return new ProductionInstallmentPaymentAdapter({
    store:
      overrides.store ??
      new MemoryAttemptStore(paymentAction("cash")),
    providers: overrides.providers ?? new ProviderRegistry(),
    cardProviderSelection: {
      loadEnabledProviders: async () =>
        overrides.configuredCardProviders ?? ["square"],
    },
    provenance:
      overrides.provenance ??
      new FakeProvenance(provenanceSnapshot([originalTender("cash", 1, "01")])),
    voucherMaterials:
      overrides.voucherMaterials ?? new FakeVoucherMaterials(),
    createId: ids.create,
    nowIso: () => NOW,
  });
}

class MemoryAttemptStore implements InstallmentProviderAttemptStorePort {
  public readonly plans = new Map<string, MutablePlan>();
  public readonly approvedMaterials = new Map<
    string,
    InstallmentApprovedPaymentMaterial
  >();
  public readonly events: string[] = [];
  public originalTenderWrites = 0;
  public approvedMaterialWrites = 0;
  public cashApprovalCalls = 0;
  public protectedMaterialJson = "";

  public constructor(public action: PersistedInstallmentAction) {}

  public async loadAction(actionId: string): Promise<PersistedInstallmentAction | null> {
    return this.action.action.actionId === actionId ? this.action : null;
  }

  public async loadPlan(actionId: string): Promise<InstallmentProviderAttemptPlan | null> {
    return this.plans.get(actionId) ?? null;
  }

  public async bindPlanOrGet(
    candidate: InstallmentProviderAttemptPlan,
  ): Promise<InstallmentProviderAttemptPlan> {
    const existing = this.plans.get(candidate.actionId);
    if (existing) return existing;
    this.events.push("bind-plan");
    const mutable: MutablePlan = {
      actionId: candidate.actionId,
      attempts: candidate.attempts.map((record) => ({
        ...record,
        attempt: { ...record.attempt, references: { ...record.attempt.references } },
      })),
      cashSettlements: candidate.cashSettlements.map((entry) => ({ ...entry })),
    };
    this.plans.set(candidate.actionId, mutable);
    return mutable;
  }

  public async compareAndUpdateAttempt(input: Readonly<{
    expected: InstallmentProviderAttemptRecord;
    nextAttempt: PaymentAttempt;
    approvedMaterial?: InstallmentApprovedPaymentMaterial;
  }>): Promise<boolean> {
    const plan = this.plans.get(input.expected.actionId);
    const current = plan?.attempts.find(
      (record) => record.attempt.attemptId === input.expected.attempt.attemptId,
    );
    if (
      !current ||
      current.attempt.state !== input.expected.attempt.state ||
      JSON.stringify(current.attempt.references) !==
        JSON.stringify(input.expected.attempt.references)
    ) {
      return false;
    }
    this.events.push(
      `attempt:${current.attempt.state}->${input.nextAttempt.state}`,
    );
    current.attempt = {
      ...input.nextAttempt,
      references: { ...input.nextAttempt.references },
    };
    if (input.approvedMaterial) {
      this.approvedMaterials.set(
        current.attempt.attemptId,
        input.approvedMaterial,
      );
      this.protectedMaterialJson = JSON.stringify(input.approvedMaterial);
      this.approvedMaterialWrites += 1;
      if (current.attempt.operation === "purchase") {
        this.originalTenderWrites += 1;
      }
    }
    return true;
  }

  public async loadApprovedMaterial(
    attemptId: string,
  ): Promise<InstallmentApprovedPaymentMaterial | null> {
    return this.approvedMaterials.get(attemptId) ?? null;
  }

  public async approveCashSettlements(
    actionId: string,
  ): Promise<readonly InstallmentCashSettlement[]> {
    const plan = this.plans.get(actionId);
    if (!plan) throw new Error("plan missing");
    this.events.push("approve-cash");
    this.cashApprovalCalls += 1;
    for (const settlement of plan.cashSettlements) {
      if (settlement.state === "Prepared") {
        settlement.state = "Approved";
        if (settlement.operation === "purchase") {
          this.originalTenderWrites += 1;
        }
      }
    }
    return plan.cashSettlements;
  }
}

type MutableAttemptRecord = Omit<InstallmentProviderAttemptRecord, "attempt"> & {
  attempt: PaymentAttempt;
};
type MutableCashSettlement = Omit<InstallmentCashSettlement, "state"> & {
  state: "Prepared" | "Approved";
};
type MutablePlan = {
  actionId: string;
  attempts: MutableAttemptRecord[];
  cashSettlements: MutableCashSettlement[];
};

class ProviderRegistry implements PaymentProviderRegistryPort {
  private readonly values = new Map<PaymentProvider, OnlinePaymentPort>();

  public constructor(...providers: readonly OnlinePaymentPort[]) {
    for (const provider of providers) this.values.set(provider.provider, provider);
  }

  public get(provider: PaymentProvider): OnlinePaymentPort {
    const value = this.values.get(provider);
    if (!value) throw new Error(`provider unavailable: ${provider}`);
    return value;
  }
}

class ScriptedProvider implements OnlinePaymentPort {
  public readonly calls: Readonly<{
    kind: "submit" | "recover" | "cancel" | "refund";
    attempt: PaymentAttempt;
  }>[] = [];
  public readonly submitResults: PaymentProviderResult[] = [];
  public readonly recoverResults: PaymentProviderResult[] = [];
  public readonly refundResults: PaymentProviderResult[] = [];
  public readonly submitErrors: Error[] = [];

  public constructor(public readonly provider: PaymentProvider) {}

  public submit(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    return this.next("submit", attempt, this.submitResults, this.submitErrors);
  }

  public recover(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    return this.next("recover", attempt, this.recoverResults);
  }

  public cancel(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    return this.next("cancel", attempt, []);
  }

  public refund(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    return this.next("refund", attempt, this.refundResults);
  }

  private next(
    kind: "submit" | "recover" | "cancel" | "refund",
    attempt: PaymentAttempt,
    results: PaymentProviderResult[],
    errors: Error[] = [],
  ): Promise<PaymentProviderResult> {
    this.calls.push({ kind, attempt });
    const error = errors.shift();
    if (error) return Promise.reject(error);
    const result = results.shift();
    if (!result) throw new Error(`Missing ${this.provider} ${kind} result.`);
    return Promise.resolve(result);
  }
}

class FakeProvenance implements InstallmentRefundProvenanceRemotePort {
  public readonly seedCalls: Readonly<{
    paymentGuid: string;
    attemptId: string;
  }>[] = [];

  public constructor(public snapshot: InstallmentRefundProvenanceSnapshot) {}

  public async resolveOrImport(): Promise<InstallmentRefundProvenanceSnapshot> {
    return this.snapshot;
  }

  public async seedRefundAttempt(input: Parameters<
    InstallmentRefundProvenanceRemotePort["seedRefundAttempt"]
  >[0]): Promise<PaymentAttempt> {
    this.seedCalls.push({
      paymentGuid: input.evidence.sourcePaymentGuid,
      attemptId: input.attempt.attemptId,
    });
    if (input.evidence.provider === "square") {
      return {
        ...input.attempt,
        references: {
          ...input.attempt.references,
          paymentId: "ORIGINAL-SQUARE-PAYMENT",
        },
      };
    }
    if (input.evidence.provider === "linkly-cloud") {
      return {
        ...input.attempt,
        references: {
          ...input.attempt.references,
          rfn: "ORIGINAL-LINKLY-RFN",
        },
      };
    }
    return input.attempt;
  }
}

class FakeVoucherMaterials implements InstallmentVoucherMaterialPort {
  public readonly prepareCalls: unknown[] = [];
  public readonly resolveCalls: unknown[] = [];
  public approved: Readonly<{
    reference: string;
    reservationToken: string | null;
  }> = {
    reference: "VOUCHER",
    reservationToken: "TOKEN",
  };

  public async prepare(input: unknown): Promise<void> {
    this.prepareCalls.push(input);
  }

  public async resolveApproved(input: unknown): Promise<Readonly<{
    reference: string;
    reservationToken: string | null;
  }>> {
    this.resolveCalls.push(input);
    return this.approved;
  }
}

class StableIds {
  public calls = 0;
  public readonly create = (): string => {
    this.calls += 1;
    return `90000000-0000-4000-8000-${this.calls
      .toString()
      .padStart(12, "0")}`;
  };
}

function paymentAction(
  method: "cash" | "card" | "voucher",
  cardProvider?: "square" | "linkly-cloud",
): PersistedInstallmentAction {
  return Object.freeze({
    action: Object.freeze({
      actionId: ACTION_ID,
      idempotencyKey: ACTION_ID,
      kind: "repayment" as const,
      installmentGuid: INSTALLMENT_GUID,
      paymentGuid: PAYMENT_GUID,
      method,
      amountCents: 2_500,
    }),
    command: Object.freeze({
      kind: "repayment" as const,
      installmentGuid: INSTALLMENT_GUID,
      deviceCode: DEVICE_CODE,
      cashierId: "cashier-1",
      cashierName: "Alice",
      ...(cardProvider
        ? {
            cardProvider,
          }
        : {}),
    }),
    deviceCode: DEVICE_CODE,
    intentFingerprint: `{"method":"${method}"}`,
    state: "ProviderPending" as const,
    storeCode: STORE_CODE,
  });
}

function cancelAction(): PersistedInstallmentAction {
  return Object.freeze({
    action: Object.freeze({
      actionId: ACTION_ID,
      idempotencyKey: ACTION_ID,
      kind: "cancel-refund" as const,
      installmentGuid: INSTALLMENT_GUID,
      paymentGuid: null,
      method: null,
      amountCents: null,
    }),
    command: Object.freeze({
      kind: "cancel-refund" as const,
      installmentGuid: INSTALLMENT_GUID,
      deviceCode: DEVICE_CODE,
      cashierId: "cashier-1",
      cashierName: "Alice",
      cancelledAtIso: NOW,
      reason: "Customer cancellation",
      idempotencyKey: ACTION_ID,
    }),
    deviceCode: DEVICE_CODE,
    intentFingerprint: '{"kind":"cancel-refund"}',
    state: "ProviderPending" as const,
    storeCode: STORE_CODE,
  });
}

function createAttemptRecord(
  action: PersistedInstallmentAction,
  provider: PaymentProvider,
  attemptId: string,
): MutableAttemptRecord {
  return {
    actionId: action.action.actionId,
    paymentGuid: action.action.paymentGuid ?? PAYMENT_GUID,
    sourcePaymentGuid: null,
    originalTenderEvidenceId: "evidence-new",
    sourceAttemptId: null,
    sequence: 0,
    attempt: {
      attemptId,
      idempotencyKey: "attempt-idempotency",
      orderGuid: action.action.installmentGuid,
      provider,
      operation: "purchase",
      amount: { currency: "AUD", cents: action.action.amountCents ?? 1 },
      state: "Created",
      references: emptyReferences(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      lastErrorCode: null,
      receiptText: null,
      responseCode: null,
    },
  };
}

function provenanceSnapshot(
  tenders: InstallmentRefundProvenanceSnapshot["tenders"],
): InstallmentRefundProvenanceSnapshot {
  return {
    complete: true,
    installmentGuid: INSTALLMENT_GUID,
    storeCode: STORE_CODE,
    requestingDeviceCode: DEVICE_CODE,
    paidAmountCents: tenders.reduce(
      (sum, tender) => sum + tender.amountCents,
      0,
    ),
    tenders,
  };
}

function originalTender(
  method: "cash" | "card" | "voucher",
  amountCents: number,
  suffix: string,
  provider: PaymentProvider | null = null,
): InstallmentRefundProvenanceSnapshot["tenders"][number] {
  return {
    evidenceId: `evidence-${suffix}`,
    sourceAttemptId: `source-attempt-${suffix}`,
    sourcePaymentGuid: sourcePaymentGuid(suffix),
    installmentGuid: INSTALLMENT_GUID,
    method,
    amountCents,
    provider,
    provenance: "hbpos-protected-details",
  };
}

function sourcePaymentGuid(suffix: string): string {
  return `40000000-0000-4000-8000-${suffix.padStart(12, "0")}`;
}

function approvedCardResult(
  provider: "square" | "linkly-cloud",
  references: Partial<PaymentAttempt["references"]>,
  evidence: CardSyncEvidenceV1,
): PaymentProviderResult {
  return {
    ...providerResult("Approved", references),
    protectedSyncEvidence: evidence,
  };
}

function providerResult(
  state: PaymentProviderResult["state"],
  references: Partial<PaymentAttempt["references"]> = {},
): PaymentProviderResult {
  return {
    state,
    references: {
      ...emptyReferences(),
      ...references,
    },
    receiptText: state === "Approved" ? "CARD RECEIPT" : null,
    responseCode: state,
  };
}

function cardEvidence(
  provider: "square" | "linkly-cloud",
  operation: "purchase" | "refund",
  amountCents: number,
): CardSyncEvidenceV1 {
  return {
    version: 1,
    provider,
    operation,
    processor: provider === "square" ? "Square" : "ANZ",
    txnRef: `TXN-${provider}-${operation}`,
    authCode: "AUTH-PROTECTED",
    cardType: "VISA",
    cardBin: 411111,
    maskedCardNumber: "****1111",
    merchantId: "MID-PROTECTED",
    responseCode: "00",
    responseText: "APPROVED",
    stan: "STAN-PROTECTED",
    bankDateTimeIso: NOW,
    amountCents,
    refundReference:
      operation === "refund"
        ? `REFUND-${provider}`
        : provider === "linkly-cloud"
          ? "RFN-PROTECTED"
          : null,
  };
}

function emptyReferences(): PaymentAttempt["references"] {
  return {
    checkoutId: null,
    paymentId: null,
    sessionId: null,
    txnRef: null,
    rfn: null,
    voucherReservationToken: null,
  };
}
