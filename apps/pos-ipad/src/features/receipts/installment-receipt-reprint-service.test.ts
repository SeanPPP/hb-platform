import assert from "node:assert/strict";
import test from "node:test";

import {
  InstallmentReceiptReprintPreparationService,
  isInstallmentReceiptReprintEligible,
} from "./installment-receipt-reprint-service";
import type {
  FrozenReceiptReprintSettings,
  ReceiptReprintSettingsSource,
} from "./receipt-reprint-service";

import type { InstallmentDetails, InstallmentsRemotePort } from "@/features/installments/installment-models";

const decoder = new TextDecoder();
const installmentGuid = "12345678-1234-1234-1234-abcdef47c164";

const settings: FrozenReceiptReprintSettings = {
  printerId: "printer-installment",
  paper: "80mm",
  locale: "en",
  store: {
    brandName: "Hot Bargain",
    storeName: "Brisbane",
    address: "1 Queen St",
    phone: "0712345678",
    abn: "12 345 678 901",
  },
};

function payment(
  paymentGuid: string,
  amountCents: number,
  recordedAtIso: string,
  overrides: Partial<InstallmentDetails["payments"][number]> = {},
): InstallmentDetails["payments"][number] {
  return {
    paymentGuid,
    method: "card",
    amountCents,
    status: "Recorded",
    recordedAtIso,
    cashierId: "cashier-1",
    deviceCode: "POS-1",
    cardType: "VISA",
    maskedCardNumber: "****4321",
    ...overrides,
  };
}

function details(overrides: Partial<InstallmentDetails> = {}): InstallmentDetails {
  return {
    installmentGuid,
    installmentNumber: "INS-100",
    storeCode: "BNE",
    deviceCode: "POS-1",
    cashierId: "cashier-1",
    cashierName: "Alice",
    customerName: "Customer One",
    customerPhone: "0400000000",
    createdAtIso: "2026-08-01T01:00:00.000Z",
    updatedAtIso: "2026-08-02T01:00:00.000Z",
    totalCents: 10_000,
    minimumDownPaymentCents: 2_000,
    downPaymentCents: 4_000,
    paidCents: 10_000,
    balanceCents: 0,
    status: "PaidOff",
    lines: [{
      installmentLineGuid: "12345678-1234-1234-1234-000000000001",
      productCode: "P-1",
      referenceCode: null,
      displayName: "Spring water",
      lookupCode: "930000000001",
      quantity: "2",
      unitPriceCents: 5_000,
      discountCents: 0,
      actualAmountCents: 10_000,
      itemNumber: "SKU-1",
    }],
    payments: [
      payment("12345678-1234-1234-1234-000000000003", 6_000, "2026-08-02T02:00:00.000Z"),
      payment("12345678-1234-1234-1234-000000000002", 4_000, "2026-08-01T02:00:00.000Z"),
      payment("12345678-1234-1234-1234-000000000004", 999, "2026-08-03T02:00:00.000Z", {
        status: "Voided",
        maskedCardNumber: "****9999",
      }),
    ],
    pickupInfo: null,
    cancellationInfo: null,
    note: null,
    ...overrides,
  };
}

function createService(input: Readonly<{
  response?: InstallmentDetails | null;
  getDetails?: InstallmentsRemotePort["getDetails"];
  settingValue?: FrozenReceiptReprintSettings | null;
  trustedStoreCode?: string;
  trustedDeviceCode?: string;
}>) {
  const detailCalls: string[] = [];
  let settingsCalls = 0;
  const installments: Pick<InstallmentsRemotePort, "getDetails"> = {
    async getDetails(requestedGuid) {
      detailCalls.push(requestedGuid);
      return input.getDetails
        ? input.getDetails(requestedGuid)
        : (input.response ?? null);
    },
  };
  const settingSource: ReceiptReprintSettingsSource = {
    async getFrozenReceiptSettings() {
      settingsCalls += 1;
      return input.settingValue === undefined ? settings : input.settingValue;
    },
  };
  return {
    detailCalls,
    get settingsCalls() {
      return settingsCalls;
    },
    service: new InstallmentReceiptReprintPreparationService({
      installments,
      settings: settingSource,
      trustedStoreCode: input.trustedStoreCode ?? "BNE",
      trustedDeviceCode: input.trustedDeviceCode ?? "POS-1",
      nowIso: () => "2026-08-03T03:04:05.000Z",
    }),
  };
}

test("分期重打纯资格与 prepare 共用金额和状态约束", () => {
  assert.equal(isInstallmentReceiptReprintEligible(details()), true);
  assert.equal(isInstallmentReceiptReprintEligible(details({
    lines: [{ ...details().lines[0]!, actualAmountCents: 9_999 }],
  })), false);
  assert.equal(isInstallmentReceiptReprintEligible(details({
    paidCents: 9_999,
  })), false);
  assert.equal(isInstallmentReceiptReprintEligible(details({
    customerName: "Customer\u001b@",
  })), false);
});

test("合法 VoidCancel 保留原定金和未付余额时仍可重打", async () => {
  const voided = details({
    totalCents: 8_000,
    minimumDownPaymentCents: 2_000,
    downPaymentCents: 2_000,
    paidCents: 2_000,
    balanceCents: 6_000,
    status: "Cancelled",
    lines: [{
      ...details().lines[0]!,
      unitPriceCents: 4_000,
      actualAmountCents: 8_000,
    }],
    payments: [payment(
      "12345678-1234-1234-1234-000000000002",
      2_000,
      "2026-08-01T02:00:00.000Z",
      { method: "cash", cardType: null, maskedCardNumber: null },
    )],
    cancellationInfo: {
      kind: "VoidCancel",
      cancelledAtIso: "2026-08-03T01:02:03.000Z",
      cancelledBy: "Alice",
      reason: "Entered by mistake",
    },
  });

  assert.equal(isInstallmentReceiptReprintEligible(voided), true);
  const prepared = await createService({ response: voided }).service.prepare(installmentGuid);
  const receipt = decoder.decode(prepared?.receiptBytes);
  assert.equal(prepared?.orderGuid, installmentGuid);
  assert.match(receipt, /\*\*\* Installment Cancelled \*\*\*/u);
  assert.match(receipt, /Deposit paid: \$20\.00/u);
  assert.match(receipt, /Balance due: \$60\.00/u);
  assert.match(receipt, /Cash\s+\$20\.00/u);
});

test("合法 RefundCancel 使用正定金与负退款的 Recorded 净额归零", () => {
  const refunded = details({
    totalCents: 8_000,
    minimumDownPaymentCents: 2_000,
    downPaymentCents: 2_000,
    paidCents: 0,
    balanceCents: 0,
    status: "Cancelled",
    lines: [{
      ...details().lines[0]!,
      unitPriceCents: 4_000,
      actualAmountCents: 8_000,
    }],
    payments: [
      payment(
        "12345678-1234-1234-1234-000000000002",
        2_000,
        "2026-08-01T02:00:00.000Z",
        { method: "cash", cardType: null, maskedCardNumber: null },
      ),
      payment(
        "12345678-1234-1234-1234-000000000005",
        -2_000,
        "2026-08-03T01:02:03.000Z",
        { method: "cash", cardType: null, maskedCardNumber: null },
      ),
    ],
    cancellationInfo: {
      kind: "RefundCancel",
      cancelledAtIso: "2026-08-03T01:02:03.000Z",
      cancelledBy: "Alice",
      reason: "Customer request",
    },
  });

  assert.equal(isInstallmentReceiptReprintEligible(refunded), true);
});

test("prepare 点击时重读可信分期并按 WPF 字段生成带 REPRINT 的票据", async () => {
  const harness = createService({ response: details() });

  const prepared = await harness.service.prepare(installmentGuid);
  const receipt = decoder.decode(prepared?.receiptBytes);

  assert.deepEqual(harness.detailCalls, [installmentGuid]);
  assert.equal(harness.settingsCalls, 1);
  assert.equal(prepared?.orderGuid, installmentGuid);
  assert.equal(
    (prepared as (typeof prepared & { externalOrderGuid?: string }))
      ?.externalOrderGuid,
    installmentGuid,
  );
  assert.equal(prepared?.printerId, "printer-installment");
  assert.match(receipt, /\*\*\* REPRINT \*\*\*/u);
  assert.match(receipt, /\*\*\* Paid - Pickup Pending \*\*\*/u);
  assert.match(receipt, /Order: INS-100/u);
  assert.match(receipt, /Installment No: INS-100/u);
  assert.match(receipt, /Customer: Customer One/u);
  assert.match(receipt, /Phone: 0400000000/u);
  assert.match(receipt, /Deposit paid: \$40\.00/u);
  assert.match(receipt, /Balance due: \$0\.00/u);
  assert.match(receipt, /Payment history:/u);
  assert.ok(receipt.indexOf("$40.00") < receipt.indexOf("$60.00"));
  assert.match(receipt, /VISA/u);
  assert.match(receipt, /\*\*\*\*4321/u);
  assert.doesNotMatch(receipt, /\*\*\*\*9999/u);
  assert.match(receipt, new RegExp(installmentGuid, "u"));
});

test("prepare 对四种分期状态使用 WPF 状态与提货语义", async () => {
  const scenarios = [
    {
      value: details({
        status: "Active",
        paidCents: 4_000,
        balanceCents: 6_000,
        payments: [payment("12345678-1234-1234-1234-000000000002", 4_000, "2026-08-01T02:00:00.000Z")],
      }),
      expected: "*** Deposit Received ***",
    },
    { value: details(), expected: "*** Paid - Pickup Pending ***" },
    {
      value: details({
        status: "PickedUp",
        pickupInfo: {
          pickedUpAtIso: "2026-08-03T01:02:03.000Z",
          pickedUpBy: "Bob",
          note: "Back door",
        },
      }),
      expected: "*** Paid - Picked Up ***",
    },
    {
      value: details({
        status: "Cancelled",
        paidCents: 0,
        balanceCents: 0,
        payments: [
          payment(
            "12345678-1234-1234-1234-000000000002",
            10_000,
            "2026-08-01T02:00:00.000Z",
          ),
          payment(
            "12345678-1234-1234-1234-000000000005",
            -10_000,
            "2026-08-03T01:02:03.000Z",
          ),
        ],
        cancellationInfo: {
          kind: "RefundCancel",
          cancelledAtIso: "2026-08-03T01:02:03.000Z",
          cancelledBy: "Alice",
          reason: "Customer request",
        },
      }),
      expected: "*** Installment Cancelled ***",
    },
  ] as const;

  for (const scenario of scenarios) {
    const prepared = await createService({ response: scenario.value }).service.prepare(installmentGuid);
    const receipt = decoder.decode(prepared?.receiptBytes);
    assert.match(receipt, new RegExp(scenario.expected.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&"), "u"));
  }
  const pickedUp = decoder.decode(
    (await createService({ response: scenarios[2].value }).service.prepare(installmentGuid))?.receiptBytes,
  );
  assert.match(pickedUp, /Pickup: Confirmed/u);
  assert.match(pickedUp, /Picked up by: Bob/u);
  assert.match(pickedUp, /Pickup note: Back door/u);
});

test("prepare 对 GUID、门店或当前设备不一致 fail closed 且不读取设置", async () => {
  for (const response of [
    details({ installmentGuid: `${installmentGuid}-other` }),
    details({ storeCode: "SYD" }),
    details({ deviceCode: "POS-2" }),
  ]) {
    const harness = createService({ response });
    assert.equal(await harness.service.prepare(installmentGuid), null);
    assert.deepEqual(harness.detailCalls, [installmentGuid]);
    assert.equal(harness.settingsCalls, 0);
  }
});

test("prepare 对金额不一致、无效设置、控制字符和远程异常保守拒绝", async () => {
  const rejected = [
    details({ lines: [{ ...details().lines[0]!, actualAmountCents: 9_999 }] }),
    details({ paidCents: 9_999 }),
    details({ customerName: "Customer\u001b@" }),
  ];
  for (const response of rejected) {
    assert.equal(await createService({ response }).service.prepare(installmentGuid), null);
  }
  assert.equal(await createService({
    response: details(),
    settingValue: { ...settings, printerId: " " },
  }).service.prepare(installmentGuid), null);
  assert.equal(await createService({
    getDetails: async () => {
      throw new Error("network unavailable");
    },
  }).service.prepare(installmentGuid), null);
});
