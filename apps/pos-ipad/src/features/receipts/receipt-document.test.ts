import assert from "node:assert/strict";
import test from "node:test";

import {
  buildBankReceiptDocument,
  buildDailyCloseDocument,
  buildSaleReceiptDocument,
  documentToEscPosBytes,
  displayWidth,
} from "./receipt-document";

const sale = {
  locale: "en" as const,
  paper: "58mm" as const,
  store: { brandName: "Hot Bargain", storeName: "Brisbane", address: "1 Queen St", phone: "0712345678", abn: "12 345 678 901" },
  orderNumber: "S1-100", soldAtIso: "2026-07-28T10:11:12.000Z", cashierName: "Alice", deviceCode: "IPAD-1",
  lines: [{ name: "Long bottled spring water with a very long name", quantity: "2", unitPriceCents: 299, discountCents: 50, totalCents: 548 }],
  subtotalCents: 598, discountCents: 50, totalCents: 548, tenders: [{ method: "cash" as const, amountCents: 600 }], cashChangeCents: 52,
};

test("58mm 英文销售小票按整数分币输出确定性文档与 ESC/POS 字节", () => {
  const document = buildSaleReceiptDocument(sale);
  const text = document.lines.map((line) => line.text).join("\n");
  const bytes = documentToEscPosBytes(document);

  assert.match(text, /TAX INVOICE/);
  assert.match(text, /Order: S1-100/);
  assert.match(text, /GST\s+\$0\.50/);
  assert.match(text, /Change\s+\$0\.52/);
  assert.ok(document.lines.every((line) => line.kind !== "text" || displayWidth(line.text) <= 32));
  assert.deepEqual([...bytes.slice(0, 3)], [0x1b, 0x40, 0x1b]);
  assert.deepEqual(
    [...bytes.slice(-3)],
    [0x1d, 0x56, 0x00],
    "每个冻结小票作业只在字节尾部切纸一次",
  );
});

test("中文 CJK 双宽换行与 80mm 对齐不截断 surrogate，退货和重打明确标记", () => {
  const document = buildSaleReceiptDocument({
    ...sale,
    locale: "zh-CN",
    paper: "80mm",
    isReprint: true,
    title: "退货小票",
    lines: [{ name: "超长中文商品名称测试用矿泉水😀", quantity: "-1", unitPriceCents: 500, discountCents: 0, totalCents: -500 }],
    subtotalCents: -500,
    discountCents: 0,
    totalCents: -500,
    tenders: [{ method: "cash", amountCents: -500 }],
    cashChangeCents: 0,
  });
  const text = document.lines.map((line) => line.text).join("\n");

  assert.match(text, /重打/);
  assert.match(text, /退货小票/);
  assert.match(text, /-\$5\.00/);
  assert.ok(document.lines.every((line) => line.kind !== "text" || displayWidth(line.text) <= 48));
  assert.ok(document.lines.every((line) => !/[\ud800-\udbff]$/.test(line.text)));
});

test("零金额订单和日结文档不产生浮点误差", () => {
  const zero = buildSaleReceiptDocument({ ...sale, totalCents: 0, subtotalCents: 0, discountCents: 0, tenders: [], cashChangeCents: null });
  const close = buildDailyCloseDocument({
    locale: "en", paper: "80mm", storeName: "Hot Bargain", businessDate: "2026-07-28", deviceCode: "IPAD-1", cashierName: "Alice",
    paymentTotals: [{ method: "cash", salesCents: 105, refundCents: -25, netCents: 80 }], orderCount: 2, salesCents: 105, refundCents: -25, netCents: 80,
    expectedCashCents: 80, countedCashCents: 75, differenceCents: -5,
  });
  assert.match(zero.lines.map((line) => line.text).join("\n"), /\$0\.00/);
  assert.match(close.lines.map((line) => line.text).join("\n"), /Cash Difference\s+-\$0\.05/);
});

test("银行卡回单只保留掩码引用，禁止 PAN、token 与 voucher 明文", () => {
  const document = buildBankReceiptDocument({
    locale: "en", paper: "58mm", status: "APPROVED", cardType: "VISA", maskedCardNumber: "4111 1111 1111 1234",
    reference: "ABCDEF123456", rawText: "PAN 4111 1111 1111 1111\nTOKEN: secret-token\nVOUCHER: ABC123\nRRN: 9876543210",
  });
  const text = document.lines.map((line) => line.text).join("\n");
  assert.match(text, /\*\*\*\*1234/);
  assert.match(text, /\*\*\*\*3210/);
  assert.doesNotMatch(text, /4111 1111|secret-token|VOUCHER: ABC123|ABCDEF123456/);
});
