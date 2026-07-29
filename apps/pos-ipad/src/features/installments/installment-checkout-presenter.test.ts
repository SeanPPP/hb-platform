import assert from "node:assert/strict";
import test from "node:test";

import {
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
} from "./installment-authorization";
import { InstallmentCheckoutPresenter } from "./installment-checkout-presenter";
import type {
  InstallmentDetails,
} from "./installment-models";
import type {
  InstallmentWorkflowCreateInput,
  InstallmentWorkflowPort,
  InstallmentWorkflowRepaymentInput,
} from "./installment-presenter";

import { PAYMENT_PERMISSION } from "@/features/payments/runtime/payment-checkout-runtime";
import {
  installmentCreatePaymentEntry,
  installmentRepaymentPaymentEntry,
} from "@/features/payments/ui/unified-payment-entry";

const CHECKOUT_ID = "11111111-1111-4111-8111-111111111111";
const INSTALLMENT_ID = "22222222-2222-4222-8222-222222222222";

test("新分期只暂存一个现金 tender，并区分实收、入账和找零", async () => {
  const createInputs: InstallmentWorkflowCreateInput[] = [];
  const workflow = workflowStub({
    create: async (input) => {
      createInputs.push(input);
      return details({ balanceCents: 0, status: "PaidOff" });
    },
  });
  const presenter = new InstallmentCheckoutPresenter({
    entry: installmentCreatePaymentEntry({
      checkoutIntentId: CHECKOUT_ID,
      expectedCartRevision: 7,
    }),
    createDrafts: {
      getSnapshot: () => ({
        revision: 7,
        totalCents: 5_000,
        lines: [
          {
            lineKey: "line-1",
            displayName: "测试商品",
            quantity: "1",
            actualAmountCents: 5_000,
          },
        ],
      }),
      subscribe: () => () => undefined,
    },
    initialOnline: true,
    permissions: createPermissions(),
    workflow,
    createTenderId: () => "tender-1",
  });

  assert.equal(await presenter.initialize(), true);
  presenter.openInstallmentCustomerEditor();
  presenter.setInstallmentCustomerDraftName("顾客甲");
  presenter.setInstallmentCustomerDraftPhone("0400000000");
  presenter.saveInstallmentCustomer();
  assert.equal(presenter.selectMethod("cash"), true);
  presenter.setAmountText("60.00");
  assert.equal(await presenter.submitSelected(), true);
  assert.equal(presenter.getState().tenders.length, 1);
  assert.deepEqual(presenter.getState().checkout.cash, {
    tenderedCents: 6_000,
    appliedCents: 5_000,
    changeCents: 1_000,
  });

  assert.equal(await presenter.confirm(), true);
  assert.equal(createInputs[0]?.method, "cash");
  assert.equal(createInputs[0]?.downPaymentCents, 5_000);
  assert.equal(createInputs[0]?.cashTenderedCents, 6_000);
  assert.equal(createInputs[0]?.cardProvider, undefined);
  assert.equal(createInputs[0]?.customerName, "顾客甲");
});

test("还款顾客只读，银行卡拒绝超付并冻结所选 Linkly provider", async () => {
  const repaymentInputs: InstallmentWorkflowRepaymentInput[] = [];
  const presenter = new InstallmentCheckoutPresenter({
    entry: installmentRepaymentPaymentEntry(INSTALLMENT_ID),
    createDrafts: {
      getSnapshot: () => null,
      subscribe: () => () => undefined,
    },
    initialOnline: true,
    permissions: repaymentPermissions(),
    workflow: workflowStub({
      addRepayment: async (input) => {
        repaymentInputs.push(input);
        return details({ balanceCents: 2_000 });
      },
    }),
    createTenderId: () => "tender-2",
  });

  assert.equal(await presenter.initialize(), true);
  const customer = presenter.getState().checkout.installmentCustomer;
  assert.equal(customer?.editable, false);
  assert.equal(customer?.name, "顾客");
  assert.equal(presenter.selectMethod("linkly-cloud"), true);
  presenter.setAmountText("31.00");
  assert.equal(await presenter.submitSelected(), false);
  assert.equal(
    presenter.getState().fieldIssue,
    "amount-exceeds-remaining",
  );
  presenter.setAmountText("10.00");
  assert.equal(await presenter.submitSelected(), true);
  assert.equal(await presenter.confirm(), true);
  assert.equal(repaymentInputs[0]?.method, "card");
  assert.equal(repaymentInputs[0]?.cardProvider, "linkly-cloud");
  assert.equal(repaymentInputs[0]?.cashTenderedCents, undefined);
});

function createPermissions(): readonly string[] {
  return [
    INSTALLMENTS_CREATE_PERMISSION,
    PAYMENT_PERMISSION.view,
    PAYMENT_PERMISSION.confirm,
    PAYMENT_PERMISSION.takeCash,
    PAYMENT_PERMISSION.takeCard,
    PAYMENT_PERMISSION.takeVoucher,
  ];
}

function repaymentPermissions(): readonly string[] {
  return [
    INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
    PAYMENT_PERMISSION.view,
    PAYMENT_PERMISSION.confirm,
    PAYMENT_PERMISSION.takeCash,
    PAYMENT_PERMISSION.takeCard,
    PAYMENT_PERMISSION.takeVoucher,
  ];
}

function workflowStub(
  overrides: Partial<InstallmentWorkflowPort> = {},
): InstallmentWorkflowPort {
  return {
    listPaymentProviderAvailability: async () => [
      { provider: "square", available: true, blocker: null },
      { provider: "linkly-cloud", available: true, blocker: null },
      { provider: "voucher", available: true, blocker: null },
    ],
    list: async () => [],
    getDetails: async () => details({}),
    recoverBlocking: async () => details({}),
    create: async () => details({}),
    addRepayment: async () => details({}),
    cancelWithRefund: async () => details({}),
    void: async () => details({}),
    confirmPickup: async () => details({}),
    ...overrides,
  };
}

function details(
  overrides: Partial<InstallmentDetails>,
): InstallmentDetails {
  return {
    installmentGuid: INSTALLMENT_ID,
    installmentNumber: "IP-1001",
    storeCode: "HB001",
    deviceCode: "IPAD-1",
    cashierName: "Cashier",
    cashierId: "cashier-1",
    customerName: "顾客",
    customerPhone: "0400111222",
    createdAtIso: "2026-07-29T00:00:00.000Z",
    updatedAtIso: "2026-07-29T00:00:00.000Z",
    totalCents: 5_000,
    downPaymentCents: 2_000,
    paidCents: 2_000,
    balanceCents: 3_000,
    status: "Active",
    minimumDownPaymentCents: 2_000,
    lines: [
      {
        installmentLineGuid: "33333333-3333-4333-8333-333333333333",
        productCode: "P1",
        referenceCode: null,
        displayName: "测试商品",
        lookupCode: "P1",
        quantity: "1",
        unitPriceCents: 5_000,
        discountCents: 0,
        actualAmountCents: 5_000,
        itemNumber: null,
      },
    ],
    payments: [],
    pickupInfo: null,
    cancellationInfo: null,
    note: null,
    ...overrides,
  };
}
