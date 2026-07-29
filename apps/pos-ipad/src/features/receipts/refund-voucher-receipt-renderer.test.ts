import assert from "node:assert/strict";
import test from "node:test";

import {
  ProtectedRefundVoucherReceiptRenderer,
  type ProtectedRefundVoucherPrintMaterial,
} from "./refund-voucher-receipt-renderer";
import type { FrozenReturnReceiptSettings } from "./return-receipt-renderer";

import type { LocalOrder } from "@/core/contracts";

const encoder = new TextDecoder();

test("单一券退款只在打印瞬间解析受保护券码，并生成独立 CODE128/QR 券面", async () => {
  const materialReads: {
    actionId: string;
    returnOrderGuid: string;
  }[] = [];
  const renderer = new ProtectedRefundVoucherReceiptRenderer(
    {
      async getByGuid() {
        return pureVoucherReturn();
      },
    },
    {
      async resolveApprovedRefundVoucher(actionId, returnOrderGuid) {
        materialReads.push({ actionId, returnOrderGuid });
        return protectedMaterial();
      },
    },
    {
      async getFrozenReturnReceiptSettings() {
        return settings();
      },
    },
    () => new Date(2026, 6, 10, 9, 30, 0),
  );

  const rendered = await renderer.render(
    "return-action-1",
    "return-order-1",
  );
  const text = encoder.decode(rendered.receiptBytes);
  const bytes = [...rendered.receiptBytes];

  assert.equal(rendered.printerId, "printer-1");
  assert.deepEqual(materialReads, [{
    actionId: "return-action-1",
    returnOrderGuid: "return-order-1",
  }]);
  assert.match(text, /REFUND VOUCHER/);
  assert.match(text, /Voucher: RF123/);
  assert.match(text, /Amount: \$8\.00/);
  assert.match(text, /Order: return-order-1/);
  assert.match(text, /Print Time: 2026-07-10 09:30:00/);
  assert.doesNotMatch(text, /TAX INVOICE|Secret product|Payment:/);
  assert.doesNotMatch(text, /return-action-1/);
  assert.equal(
    JSON.stringify(rendered).includes("return-action-1"),
    false,
    "actionId 不得进入打印结果公开快照",
  );
  assert.equal(
    containsSequence(bytes, [0x1d, 0x6b, 0x49]),
    true,
    "必须包含 CODE128 指令",
  );
  assert.equal(
    containsSequence(bytes, [0x1d, 0x28, 0x6b]),
    true,
    "必须包含 QR 指令",
  );
  assert.deepEqual(bytes.slice(-3), [0x1d, 0x56, 0x00]);
});

test("CODE128 转义券码中的左花括号并按转义后长度编码，QR 与明文保持原值", async () => {
  const voucherCode = "AB{C12";
  const renderer = new ProtectedRefundVoucherReceiptRenderer(
    {
      async getByGuid() {
        return pureVoucherReturn();
      },
    },
    {
      async resolveApprovedRefundVoucher() {
        return { ...protectedMaterial(), voucherCode };
      },
    },
    {
      async getFrozenReturnReceiptSettings() {
        return settings();
      },
    },
    () => new Date(2026, 6, 10, 9, 30, 0),
  );

  const rendered = await renderer.render(
    "return-action-1",
    "return-order-1",
  );
  const bytes = [...rendered.receiptBytes];
  const code128Payload = [...new TextEncoder().encode("{BAB{{C12")];
  const qrPayload = [...new TextEncoder().encode(voucherCode)];

  assert.equal(code128Payload.length, 9);
  assert.equal(
    containsSequence(bytes, [
      0x1d,
      0x6b,
      0x49,
      code128Payload.length,
      ...code128Payload,
      0x0a,
    ]),
    true,
    "CODE128 长度和 payload 必须以转义后的数据为准",
  );
  assert.equal(
    containsSequence(bytes, [
      0x1d,
      0x28,
      0x6b,
      qrPayload.length + 3,
      0x00,
      0x31,
      0x50,
      0x30,
      ...qrPayload,
      0x1d,
      0x28,
      0x6b,
      0x03,
      0x00,
      0x31,
      0x51,
      0x30,
    ]),
    true,
    "QR payload 必须保留原始券码",
  );
  assert.match(encoder.decode(rendered.receiptBytes), /Voucher: AB\{C12/);
});

test("订单、金额、券码或设置不满足冻结身份时失败关闭", async (t) => {
  const baseOrder = pureVoucherReturn();
  const baseMaterial = protectedMaterial();
  const cases: readonly Readonly<{
    name: string;
    order: LocalOrder | null;
    material: ProtectedRefundVoucherPrintMaterial | null;
    settings: FrozenReturnReceiptSettings | null;
  }>[] = [
    {
      name: "missing material",
      order: baseOrder,
      material: null,
      settings: settings(),
    },
    {
      name: "amount mismatch",
      order: baseOrder,
      material: { ...baseMaterial, refundAmountCents: 799 },
      settings: settings(),
    },
    {
      name: "unsafe voucher code",
      order: baseOrder,
      material: { ...baseMaterial, voucherCode: "RF123\nOPEN DRAWER" },
      settings: settings(),
    },
    {
      name: "multiple tenders",
      order: {
        ...baseOrder,
        tenders: [
          ...baseOrder.tenders,
          {
            tenderGuid: "voucher-tender-2",
            method: "voucher",
            amount: { currency: "AUD", cents: -1 },
            reference: null,
            reservationToken: null,
          },
        ],
      },
      material: baseMaterial,
      settings: settings(),
    },
    {
      name: "missing settings",
      order: baseOrder,
      material: baseMaterial,
      settings: null,
    },
  ];

  for (const current of cases) {
    await t.test(current.name, async () => {
      const renderer = new ProtectedRefundVoucherReceiptRenderer(
        { async getByGuid() { return current.order; } },
        {
          async resolveApprovedRefundVoucher() {
            return current.material;
          },
        },
        {
          async getFrozenReturnReceiptSettings() {
            return current.settings;
          },
        },
        () => new Date(2026, 6, 10, 9, 30, 0),
      );
      await assert.rejects(
        () => renderer.render("return-action-1", "return-order-1"),
        /REFUND_VOUCHER_/,
      );
    });
  }
});

function pureVoucherReturn(): LocalOrder {
  return {
    orderGuid: "return-order-1",
    localSequence: 8,
    storeCode: "S001",
    deviceCode: "IPAD-1",
    cashierId: "cashier-1",
    cashierName: "Cashier",
    soldAtIso: "2026-07-10T00:00:00.000Z",
    state: "PendingSync",
    total: { currency: "AUD", cents: -800 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: -800 },
    originalOrderGuid: "sale-order-1",
    lines: [
      {
        lineId: "return-line-1",
        productCode: "P-SECRET",
        itemNumber: "I-SECRET",
        lookupCode: "SECRET",
        displayName: "Secret product",
        quantity: "1",
        unitPrice: { currency: "AUD", cents: 800 },
        discount: { currency: "AUD", cents: 0 },
        actualAmount: { currency: "AUD", cents: -800 },
        priceSource: "catalog",
        kind: "return",
        returnSourceKey: "source-1",
        originalOrderGuid: "sale-order-1",
        originalOrderDetailGuid: "sale-line-1",
      },
    ],
    tenders: [
      {
        tenderGuid: "voucher-tender-1",
        method: "voucher",
        amount: { currency: "AUD", cents: -800 },
        reference: null,
        reservationToken: null,
      },
    ],
  };
}

function protectedMaterial(): ProtectedRefundVoucherPrintMaterial {
  return {
    returnOrderGuid: "return-order-1",
    voucherCode: "RF123",
    refundAmountCents: 800,
  };
}

function settings(): FrozenReturnReceiptSettings {
  return {
    printerId: "printer-1",
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
}

function containsSequence(
  source: readonly number[],
  expected: readonly number[],
): boolean {
  return source.some((_, index) =>
    expected.every((value, offset) => source[index + offset] === value),
  );
}
