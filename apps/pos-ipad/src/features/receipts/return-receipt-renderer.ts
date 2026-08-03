import {
  buildSaleReceiptDocument,
  documentToEscPosBytes,
  type EscPosDocument,
  type ReceiptLocale,
  type ReceiptPaper,
} from "./receipt-document";

import type { LocalOrder, OrderRepositoryPort } from "@/core/contracts";

export type FrozenReturnReceiptSettings = Readonly<{
  printerId: string;
  paper: ReceiptPaper;
  locale: ReceiptLocale;
  store: Readonly<{
    brandName: string;
    storeName: string;
    address: string;
    phone: string;
    abn: string;
  }>;
}>;

export interface ReturnReceiptSettingsPort {
  getFrozenReturnReceiptSettings(): Promise<FrozenReturnReceiptSettings | null>;
}

export type RenderedReturnReceipt = Readonly<{
  printerId: string;
  receiptBytes: Uint8Array;
}>;

/**
 * 退货小票只从已完成的本地 return order 和一次冻结的外设设置构造。
 * 账本中的 provider reference / reservation token 绝不进入 document 输入。
 */
export class OrderRepositoryReturnReceiptRenderer {
  public constructor(
    private readonly orders: Pick<OrderRepositoryPort, "getByGuid">,
    private readonly settings: ReturnReceiptSettingsPort,
  ) {}

  public async render(returnOrderGuidInput: string): Promise<RenderedReturnReceipt> {
    const returnOrderGuid = requiredText(returnOrderGuidInput, "RETURN_RECEIPT_ID_REQUIRED");
    const order = await this.orders.getByGuid(returnOrderGuid);
    if (!isRenderableReturnOrder(order, returnOrderGuid)) {
      throw new Error("RETURN_RECEIPT_ORDER_INVALID");
    }
    const settings = await this.settings.getFrozenReturnReceiptSettings();
    if (!isValidSettings(settings)) {
      throw new Error("RETURN_RECEIPT_SETTINGS_MISSING");
    }

    const receiptBytes = documentToEscPosBytes(
      refundDocument(order, settings),
    );
    if (!receiptBytes.byteLength) throw new Error("RETURN_RECEIPT_BYTES_INVALID");
    return {
      printerId: settings.printerId.trim(),
      receiptBytes,
    };
  }
}

function refundDocument(
  order: LocalOrder,
  settings: FrozenReturnReceiptSettings,
): EscPosDocument {
  const locale = settings.locale;
  const document = buildSaleReceiptDocument({
    locale,
    paper: settings.paper,
    store: settings.store,
    orderNumber: order.orderGuid,
    orderGuid: order.orderGuid,
    orderDisplay: `#${order.localSequence}`,
    soldAtIso: order.soldAtIso,
    cashierName: order.cashierName,
    storeCode: order.storeCode,
    deviceCode: order.deviceCode,
    lines: order.lines.map((line) => ({
      name: line.displayName,
      lookupCode: line.lookupCode,
      // 账本 quantity 是正数，显示层显式加退款负号而不改写持久事实。
      quantity: `-${line.quantity}`,
      discountCents: 0,
      totalCents: line.actualAmount.cents,
    })),
    subtotalCents: order.total.cents,
    discountCents: 0,
    totalCents: order.actualAmount.cents,
    tenders: order.tenders.map((tender) => ({
      method: tender.method,
      amountCents: tender.amount.cents,
      // 原支付 reference、RFN、券 reservation token 均不能打印。
      reference: null,
    })),
    cashChangeCents: null,
    title: locale === "zh-CN" ? "退款小票" : "REFUND RECEIPT",
    statusText: locale === "zh-CN" ? "*** 已退款 ***" : "*** Refunded ***",
    includeMachineCodes: true,
    printedAtIso: order.soldAtIso,
  });
  return replaceFooter(document, locale === "zh-CN" ? "退款已处理" : "Refund processed");
}

function replaceFooter(document: EscPosDocument, replacement: string): EscPosDocument {
  const previous = ["Thank you for your purchase!", "感谢惠顾"];
  let footerIndex = -1;
  for (let index = document.lines.length - 1; index >= 0; index -= 1) {
    const line = document.lines[index];
    if (line?.kind === "text" && previous.includes(line.text)) {
      footerIndex = index;
      break;
    }
  }
  if (footerIndex < 0) throw new Error("RETURN_RECEIPT_FOOTER_INVALID");
  return {
    ...document,
    lines: document.lines.map((line, index) => (
      index === footerIndex && line.kind === "text"
        ? { ...line, text: replacement }
        : line
    )),
  };
}

function isRenderableReturnOrder(
  order: LocalOrder | null,
  returnOrderGuid: string,
): order is LocalOrder {
  if (
    !order ||
    order.orderGuid !== returnOrderGuid ||
    !hasText(order.cashierName) ||
    !hasText(order.deviceCode) ||
    !isCompletedReturnState(order.state) ||
    (order.originalOrderGuid !== null && !hasText(order.originalOrderGuid)) ||
    !isNegativeAud(order.total) ||
    !isZeroAud(order.discount) ||
    !isNegativeAud(order.actualAmount) ||
    !order.lines.length ||
    !order.tenders.length
  ) return false;

  let lineTotal = 0;
  for (const line of order.lines) {
    const quantity = parsePositiveQuantity(line.quantity);
    if (
      line.kind !== "return" ||
      !hasText(line.displayName) ||
      !hasText(line.productCode) ||
      !hasText(line.lookupCode) ||
      quantity === null ||
      !isPositiveAud(line.unitPrice) ||
      !isZeroAud(line.discount) ||
      !isNegativeAud(line.actualAmount) ||
      !safeEqualsNegativeProduct(line.actualAmount.cents, quantity, line.unitPrice.cents)
    ) return false;
    const nextLineTotal = safeAdd(lineTotal, line.actualAmount.cents);
    if (nextLineTotal === null) return false;
    lineTotal = nextLineTotal;
  }
  if (lineTotal !== order.actualAmount.cents || order.total.cents !== order.actualAmount.cents) return false;

  let tenderTotal = 0;
  for (const tender of order.tenders) {
    if (!isNegativeAud(tender.amount) || !isTenderMethod(tender.method)) return false;
    const nextTenderTotal = safeAdd(tenderTotal, tender.amount.cents);
    if (nextTenderTotal === null) return false;
    tenderTotal = nextTenderTotal;
  }
  return tenderTotal === order.actualAmount.cents;
}

function isValidSettings(
  value: FrozenReturnReceiptSettings | null,
): value is FrozenReturnReceiptSettings {
  return Boolean(
    value &&
      hasText(value.printerId) &&
      (value.paper === "58mm" || value.paper === "80mm") &&
      (value.locale === "en" || value.locale === "zh-CN") &&
      hasText(value.store.brandName) &&
      isSafeReceiptText(value.store.storeName) &&
      isSafeReceiptText(value.store.address) &&
      isSafeReceiptText(value.store.phone) &&
      isSafeReceiptText(value.store.abn),
  );
}

function isCompletedReturnState(value: LocalOrder["state"]): boolean {
  return value === "CompletedLocal" || value === "PendingSync" || value === "Syncing" || value === "Synced" || value === "Blocked403" || value === "Rejected";
}

function isNegativeAud(value: unknown): value is Readonly<{ currency: "AUD"; cents: number }> {
  return isAud(value) && value.cents < 0;
}

function isPositiveAud(value: unknown): value is Readonly<{ currency: "AUD"; cents: number }> {
  return isAud(value) && value.cents > 0;
}

function isZeroAud(value: unknown): value is Readonly<{ currency: "AUD"; cents: number }> {
  return isAud(value) && value.cents === 0;
}

function isAud(value: unknown): value is Readonly<{ currency: "AUD"; cents: number }> {
  return Boolean(value && typeof value === "object" && (value as { currency?: unknown }).currency === "AUD" && Number.isSafeInteger((value as { cents?: unknown }).cents));
}

function parsePositiveQuantity(value: string): number | null {
  if (!/^\d+$/u.test(value)) return null;
  const quantity = Number(value);
  return Number.isSafeInteger(quantity) && quantity > 0 ? quantity : null;
}

function safeEqualsNegativeProduct(amount: number, quantity: number, unitPrice: number): boolean {
  const product = quantity * unitPrice;
  return Number.isSafeInteger(product) && amount === -product;
}

function safeAdd(left: number, right: number): number | null {
  const total = left + right;
  return Number.isSafeInteger(total) ? total : null;
}

function isTenderMethod(value: string): boolean {
  return value === "cash" || value === "card" || value === "voucher";
}

function isSafeReceiptText(value: unknown): value is string {
  return typeof value === "string" && !/[\u0000-\u001F\u007F]/u.test(value);
}

function hasText(value: unknown): value is string {
  return isSafeReceiptText(value) && value.trim().length > 0;
}

function requiredText(value: string, code: string): string {
  if (!hasText(value)) throw new Error(code);
  return value.trim();
}
