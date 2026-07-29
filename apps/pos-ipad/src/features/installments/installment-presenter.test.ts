import assert from "node:assert/strict";
import test from "node:test";

import {
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_VIEW_PERMISSION,
} from "./installment-authorization";
import type { InstallmentDetails } from "./installment-models";
import {
  InstallmentPresenter,
  InstallmentWorkflowError,
  type InstallmentCreateDraft,
  type InstallmentCreateDraftPort,
  type InstallmentWorkflowPort,
} from "./installment-presenter";

import type { InstallmentStatus, InstallmentSummary } from "@/core/contracts";


const allPermissions = [
  INSTALLMENTS_VIEW_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
];

test("查看权限控制历史读取；在线与离线来源由可信 workflow 区分", async () => {
  const workflow = new FakeWorkflow();
  const unauthorized = presenter(workflow, {
    permissions: [INSTALLMENTS_CREATE_PERMISSION],
  });

  await unauthorized.load();

  assert.equal(unauthorized.getState().kind, "unauthorized");
  assert.equal(workflow.listInputs.length, 0);

  const authorized = presenter(workflow);
  authorized.setSearchQuery(" Bob ");
  authorized.setStatusFilter("Active");
  authorized.setOnline(false);
  await authorized.load();

  assert.deepEqual(workflow.listInputs, [
    {
      keyword: "Bob",
      online: false,
      status: "Active",
      take: 100,
    },
  ]);
  assert.equal(authorized.getState().orders.length, 2);
  assert.equal(authorized.getState().kind, "ready");
});

test("选择历史行读取详情，较旧异步结果不能覆盖新选择", async () => {
  const workflow = new FakeWorkflow();
  const first = deferred<InstallmentDetails | null>();
  workflow.detailResults.set(GUID_ACTIVE, first.promise);
  workflow.detailResults.set(
    GUID_PAID,
    Promise.resolve(details("PaidOff")),
  );
  const subject = presenter(workflow);
  await subject.load();

  const selectFirst = subject.select(GUID_ACTIVE);
  await subject.select(GUID_PAID);
  first.resolve(details("Active"));
  await selectFirst;

  assert.equal(subject.getState().selectedGuid, GUID_PAID);
  assert.equal(subject.getState().details?.status, "PaidOff");
});

test("所有写操作离线时失败关闭，不能调用 workflow", async () => {
  const workflow = new FakeWorkflow();
  const subject = presenter(workflow);
  subject.setOnline(false);
  subject.showCreate();
  fillValidCreateForm(subject);

  await subject.create();
  await subject.select(GUID_ACTIVE);
  subject.setRepaymentAmount("10.00");
  await subject.addRepayment();
  await subject.cancelWithRefund();
  await subject.voidSelected();
  await subject.select(GUID_PAID);
  await subject.confirmPickup();

  assert.equal(subject.getState().statusCode, "online-required");
  assert.deepEqual(workflow.writeCalls, []);
});

test("创建严格对齐 WPF 的客户、AUD 50 总额和 AUD 20 首付规则", async () => {
  const workflow = new FakeWorkflow();
  const drafts = new MutableDraftPort({
    revision: 7,
    totalCents: 4_999,
    lines: [
      {
        lineKey: "L1",
        displayName: "Tea",
        quantity: "1",
        actualAmountCents: 4_999,
      },
    ],
  });
  const subject = presenter(workflow, { drafts });
  subject.showCreate();
  fillValidCreateForm(subject);

  await subject.create();
  assert.equal(subject.getState().statusCode, "invalid-create");
  assert.deepEqual(workflow.writeCalls, []);

  drafts.set({
    revision: 8,
    totalCents: 5_000,
    lines: [
      {
        lineKey: "L1",
        displayName: "Tea",
        quantity: "1",
        actualAmountCents: 5_000,
      },
    ],
  });
  subject.setCreateDownPayment("19.99");
  await subject.create();
  assert.equal(subject.getState().statusCode, "invalid-create");

  subject.setCreateDownPayment("20.00");
  await subject.create();

  assert.deepEqual(workflow.writeCalls, [
    {
      kind: "create",
      input: {
        customerName: "Bob",
        customerPhone: "0400000000",
        downPaymentCents: 2_000,
        draftRevision: 8,
        method: "cash",
        note: "Collect Friday",
        voucherReference: null,
        voucherReservationToken: null,
      },
    },
  ]);
  assert.equal(subject.getState().statusCode, "create-complete");
  assert.equal(subject.getState().pane, "history");
});

test("券首付和补款只接收券码，workflow token 固定为 null 且公开状态没有 token 字段", async () => {
  const workflow = new FakeWorkflow();
  const subject = presenter(workflow);
  subject.showCreate();
  fillValidCreateForm(subject);
  subject.setCreatePaymentMethod("voucher");

  await subject.create();
  assert.equal(subject.getState().statusCode, "invalid-create");
  assert.deepEqual(workflow.writeCalls, []);

  subject.setCreateVoucherReference(" VOUCHER-REF ");
  await subject.create();
  assert.deepEqual(workflow.writeCalls[0], {
    kind: "create",
    input: {
      customerName: "Bob",
      customerPhone: "0400000000",
      downPaymentCents: 2_000,
      draftRevision: 1,
      method: "voucher",
      note: "Collect Friday",
      voucherReference: "VOUCHER-REF",
      voucherReservationToken: null,
    },
  });
  assert.equal(subject.getState().createVoucherReference, "");
  assert.equal(
    "createVoucherReservationToken" in subject.getState(),
    false,
  );
  assert.equal("setCreateVoucherReservationToken" in subject, false);

  await subject.select(GUID_ACTIVE);
  subject.setRepaymentAmount("10.00");
  subject.setRepaymentMethod("voucher");
  await subject.addRepayment();
  assert.equal(subject.getState().statusCode, "invalid-repayment");

  subject.setRepaymentVoucherReference(" REPAY-REF ");
  await subject.addRepayment();
  assert.deepEqual(workflow.writeCalls.at(-1), {
    kind: "repayment",
    input: {
      amountCents: 1_000,
      installmentGuid: GUID_ACTIVE,
      method: "voucher",
      voucherReference: "REPAY-REF",
      voucherReservationToken: null,
    },
  });
  assert.equal(subject.getState().repaymentVoucherReference, "");
  assert.equal(
    "repaymentVoucherReservationToken" in subject.getState(),
    false,
  );
  assert.equal("setRepaymentVoucherReservationToken" in subject, false);
});

test("券码在 workflow 开始前从公开状态清空，失败后也不恢复", async () => {
  const workflow = new FakeWorkflow();
  const createResult = deferred<InstallmentDetails>();
  workflow.nextCreate = createResult.promise;
  const subject = presenter(workflow);
  subject.showCreate();
  fillValidCreateForm(subject);
  subject.setCreatePaymentMethod("voucher");
  subject.setCreateVoucherReference(" CREATE-CODE ");

  const pendingCreate = subject.create();
  assert.equal(subject.getState().createVoucherReference, "");
  assert.equal(workflow.writeCalls.length, 1);
  createResult.resolve(details("Active"));
  await pendingCreate;

  await subject.select(GUID_ACTIVE);
  subject.setRepaymentAmount("10.00");
  subject.setRepaymentMethod("voucher");
  subject.setRepaymentVoucherReference(" REPAY-CODE ");
  workflow.repaymentError = new Error(
    "provider failed without echoing voucher material",
  );

  await subject.addRepayment();

  assert.equal(subject.getState().repaymentVoucherReference, "");
  assert.equal(subject.getState().statusCode, "action-failed");
});

test("补款仅允许 Active 且金额不超过余额；重复点击只产生一次写调用", async () => {
  const workflow = new FakeWorkflow();
  const repayment = deferred<InstallmentDetails>();
  workflow.nextRepayment = repayment.promise;
  const subject = presenter(workflow);
  await subject.select(GUID_ACTIVE);
  subject.setRepaymentAmount("80.01");

  await subject.addRepayment();
  assert.equal(subject.getState().statusCode, "invalid-repayment");

  subject.setRepaymentAmount("80.00");
  const first = subject.addRepayment();
  const second = subject.addRepayment();
  assert.equal(first, second);
  assert.equal(
    workflow.writeCalls.filter((call) => call.kind === "repayment").length,
    1,
  );
  repayment.resolve(details("PaidOff"));
  await first;

  assert.equal(subject.getState().details?.status, "PaidOff");
  assert.equal(subject.getState().statusCode, "repayment-complete");
  subject.setRepaymentAmount("1.00");
  await subject.addRepayment();
  assert.equal(subject.getState().statusCode, "invalid-repayment");
});

test("取消退款与作废只允许 Active，取货只允许 PaidOff，并复用 WPF 权限边界", async () => {
  const workflow = new FakeWorkflow();
  const subject = presenter(workflow);
  await subject.select(GUID_ACTIVE);
  subject.setCancelReason(" Customer request ");
  await subject.cancelWithRefund();

  assert.deepEqual(workflow.writeCalls.at(-1), {
    kind: "cancel",
    input: {
      installmentGuid: GUID_ACTIVE,
      reason: "Customer request",
    },
  });

  workflow.detailResults.set(
    GUID_ACTIVE,
    Promise.resolve(details("Active")),
  );
  await subject.select(GUID_ACTIVE);
  subject.setVoidReason(" Incorrect order ");
  await subject.voidSelected();
  assert.deepEqual(workflow.writeCalls.at(-1), {
    kind: "void",
    input: {
      installmentGuid: GUID_ACTIVE,
      reason: "Incorrect order",
    },
  });

  await subject.select(GUID_PAID);
  subject.setPickupNote(" ID checked ");
  await subject.confirmPickup();
  assert.deepEqual(workflow.writeCalls.at(-1), {
    kind: "pickup",
    input: {
      installmentGuid: GUID_PAID,
      note: "ID checked",
    },
  });

  const noCancel = presenter(workflow, {
    permissions: [
      INSTALLMENTS_VIEW_PERMISSION,
      INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
    ],
  });
  await noCancel.select(GUID_ACTIVE);
  await noCancel.cancelWithRefund();
  assert.equal(noCancel.getState().statusCode, "permission-required");
});

test("Unknown 后允许重试同一 durable action，恢复成功后解除提示", async () => {
  const workflow = new FakeWorkflow();
  workflow.createError = new InstallmentWorkflowError(
    "payment-recovery-required",
    "Payment outcome is unknown.",
  );
  const subject = presenter(workflow);
  subject.showCreate();
  fillValidCreateForm(subject);

  await subject.create();
  assert.equal(subject.getState().recoveryRequired, true);
  assert.equal(
    subject.getState().statusCode,
    "payment-recovery-required",
  );

  workflow.createError = null;
  await subject.recoverBlocking();
  assert.equal(
    workflow.writeCalls.filter((call) => call.kind === "create").length,
    1,
  );
  assert.equal(
    workflow.writeCalls.filter((call) => call.kind === "recover").length,
    1,
  );
  assert.equal(subject.getState().recoveryRequired, false);
  assert.equal(subject.getState().statusCode, "recovery-complete");
});

test("草稿变化会同步到创建页，销毁后不再订阅", () => {
  const drafts = new MutableDraftPort(defaultDraft());
  const subject = presenter(new FakeWorkflow(), { drafts });

  drafts.set({
    ...defaultDraft(),
    revision: 2,
    totalCents: 6_000,
  });
  assert.equal(subject.getState().createDraft?.revision, 2);

  subject.destroy();
  drafts.set({
    ...defaultDraft(),
    revision: 3,
    totalCents: 7_000,
  });
  assert.equal(subject.getState().createDraft?.revision, 2);
});

class FakeWorkflow implements InstallmentWorkflowPort {
  public readonly listInputs: unknown[] = [];
  public readonly writeCalls: {
    kind: string;
    input: unknown;
  }[] = [];
  public readonly detailResults = new Map<
    string,
    Promise<InstallmentDetails | null>
  >();
  public nextRepayment: Promise<InstallmentDetails> | null = null;
  public nextCreate: Promise<InstallmentDetails> | null = null;
  public createError: Error | null = null;
  public repaymentError: Error | null = null;

  public async list(input: unknown): Promise<readonly InstallmentSummary[]> {
    this.listInputs.push(input);
    return [
      summary("Active"),
      { ...summary("PaidOff"), installmentGuid: GUID_PAID },
    ];
  }

  public getDetails(input: Readonly<{ installmentGuid: string }>) {
    return (
      this.detailResults.get(input.installmentGuid) ??
      Promise.resolve(
        details(
          input.installmentGuid === GUID_PAID ? "PaidOff" : "Active",
        ),
      )
    );
  }

  public async create(input: unknown): Promise<InstallmentDetails> {
    this.writeCalls.push({ kind: "create", input });
    if (this.createError) throw this.createError;
    return this.nextCreate ?? details("Active");
  }

  public async recoverBlocking(): Promise<InstallmentDetails> {
    this.writeCalls.push({ kind: "recover", input: null });
    if (this.createError) throw this.createError;
    return details("Active");
  }

  public async addRepayment(input: unknown): Promise<InstallmentDetails> {
    this.writeCalls.push({ kind: "repayment", input });
    if (this.repaymentError) throw this.repaymentError;
    return this.nextRepayment ?? details("PaidOff");
  }

  public async cancelWithRefund(input: unknown): Promise<InstallmentDetails> {
    this.writeCalls.push({ kind: "cancel", input });
    return details("Cancelled");
  }

  public async void(input: unknown): Promise<InstallmentDetails> {
    this.writeCalls.push({ kind: "void", input });
    return details("Cancelled");
  }

  public async confirmPickup(input: unknown): Promise<InstallmentDetails> {
    this.writeCalls.push({ kind: "pickup", input });
    return details("PickedUp");
  }
}

class MutableDraftPort implements InstallmentCreateDraftPort {
  private readonly listeners = new Set<() => void>();

  public constructor(private value: InstallmentCreateDraft | null) {}

  public getSnapshot = () => this.value;

  public subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public set(value: InstallmentCreateDraft | null) {
    this.value = value;
    for (const listener of this.listeners) listener();
  }
}

function presenter(
  workflow: FakeWorkflow,
  overrides: Partial<{
    drafts: InstallmentCreateDraftPort;
    permissions: readonly string[];
  }> = {},
) {
  return new InstallmentPresenter({
    createDrafts: overrides.drafts ?? new MutableDraftPort(defaultDraft()),
    initialOnline: true,
    permissions: overrides.permissions ?? allPermissions,
    workflow,
  });
}

function fillValidCreateForm(subject: InstallmentPresenter) {
  subject.setCustomerName(" Bob ");
  subject.setCustomerPhone(" 0400000000 ");
  subject.setCreateNote(" Collect Friday ");
  subject.setCreateDownPayment("20.00");
  subject.setCreatePaymentMethod("cash");
}

function defaultDraft(): InstallmentCreateDraft {
  return {
    revision: 1,
    totalCents: 10_000,
    lines: [
      {
        lineKey: "L1",
        displayName: "Tea",
        quantity: "1",
        actualAmountCents: 10_000,
      },
    ],
  };
}

const GUID_ACTIVE = "10000000-0000-4000-8000-000000000001";
const GUID_PAID = "10000000-0000-4000-8000-000000000002";

function summary(status: InstallmentStatus): InstallmentSummary {
  const balanceCents = status === "Active" ? 8_000 : 0;
  return {
    installmentGuid: GUID_ACTIVE,
    installmentNumber: "IP-0001",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    cashierName: "Alice",
    customerName: "Bob",
    customerPhone: "0400000000",
    createdAtIso: "2026-07-27T01:02:03.000Z",
    totalCents: 10_000,
    downPaymentCents: 2_000,
    paidCents: 10_000 - balanceCents,
    balanceCents,
    status,
    updatedAtIso: "2026-07-27T02:03:04.000Z",
  };
}

function details(status: InstallmentStatus): InstallmentDetails {
  return {
    ...summary(status),
    installmentGuid:
      status === "PaidOff" || status === "PickedUp"
        ? GUID_PAID
        : GUID_ACTIVE,
    cashierId: "C1",
    minimumDownPaymentCents: 2_000,
    lines: [],
    payments: [],
    pickupInfo: null,
    cancellationInfo: null,
    note: null,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}
