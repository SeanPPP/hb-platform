import type { EscPosDocument } from "@/features/receipts/receipt-document";

export type LocalHistoryOrderState =
  | "CompletedLocal"
  | "PendingSync"
  | "Syncing"
  | "Synced"
  | "Blocked403"
  | "Rejected";

export type LocalHistoryTenderSummary = Readonly<{
  method: "cash" | "card" | "voucher";
  amountCents: number;
}>;

export type LocalHistorySummary = Readonly<{
  orderGuid: string;
  localSequence: number;
  soldAtIso: string;
  cashierName: string;
  state: LocalHistoryOrderState;
  totalCents: number;
  discountCents: number;
  actualAmountCents: number;
  lineCount: number;
  paymentSummary: string;
}>;

export type LocalHistoryLine = Readonly<{
  lineId: string;
  productCode: string;
  itemNumber: string | null;
  lookupCode: string;
  displayName: string;
  quantity: string;
  unitPriceCents: number;
  discountCents: number;
  actualAmountCents: number;
  kind: "sale" | "return";
}>;

export type LocalHistoryDetails = Readonly<{
  orderGuid: string;
  localSequence: number;
  soldAtIso: string;
  cashierName: string;
  state: LocalHistoryOrderState;
  totalCents: number;
  discountCents: number;
  actualAmountCents: number;
  lines: readonly LocalHistoryLine[];
  tenders: readonly LocalHistoryTenderSummary[];
}>;

export type LocalHistoryFilters = Readonly<{
  soldFromIso: string;
  soldToIso: string;
  keyword: string | null;
}>;

/**
 * 查询只描述 UI 可控筛选和稳定游标；可信门店/设备永远由仓储构造期绑定。
 */
export type LocalHistoryQuery = LocalHistoryFilters &
  Readonly<{
    cursor: number | null;
    limit: number;
  }>;

export type LocalHistoryPage = Readonly<{
  orders: readonly LocalHistorySummary[];
  nextCursor: number | null;
}>;

export interface LocalHistoryPort {
  list(query: LocalHistoryQuery): Promise<LocalHistoryPage>;
  getDetails(orderGuid: string): Promise<LocalHistoryDetails | null>;
}

/**
 * 重打适配器只接收已经由 presenter 选中的本地订单号。
 */
export interface LocalHistoryReprintPort {
  reprintExistingOrder(orderGuid: string): Promise<void>;
}

/**
 * 预览端口只返回已经脱敏并排版完成的小票文档；页面不得接触原始订单支付引用。
 */
export interface LocalHistoryReceiptPreviewPort {
  getPreview(orderGuid: string): Promise<EscPosDocument | null>;
}

export const LOCAL_HISTORY_KEYWORD_MAX_LENGTH = 128;

export function normalizeLocalHistoryQuery(
  query: LocalHistoryQuery,
): LocalHistoryQuery {
  const soldFromIso = canonicalIso(
    query.soldFromIso,
    "local history soldFromIso",
  );
  const soldToIso = canonicalIso(
    query.soldToIso,
    "local history soldToIso",
  );
  if (Date.parse(soldFromIso) > Date.parse(soldToIso)) {
    throw new TypeError("Local history date range is reversed.");
  }
  if (
    query.cursor !== null &&
    (!Number.isSafeInteger(query.cursor) || query.cursor <= 0)
  ) {
    throw new TypeError("Local history cursor must be a positive integer.");
  }
  if (
    !Number.isSafeInteger(query.limit) ||
    query.limit < 1 ||
    query.limit > 50
  ) {
    throw new TypeError("Local history page size must be between 1 and 50.");
  }
  return Object.freeze({
    soldFromIso,
    soldToIso,
    keyword: optionalText(
      query.keyword,
      "local history keyword",
      LOCAL_HISTORY_KEYWORD_MAX_LENGTH,
    ),
    cursor: query.cursor,
    limit: query.limit,
  });
}

function canonicalIso(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    !/(?:Z|[+-]\d{2}:\d{2})$/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) {
    throw new TypeError(`${label} is invalid.`);
  }
  return new Date(timestamp).toISOString();
}

function optionalText(
  value: unknown,
  label: string,
  maximumLength: number,
): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") {
    throw new TypeError(`${label} is invalid.`);
  }
  const normalized = value.trim();
  if (!normalized) return null;
  if (
    normalized.length > maximumLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized;
}
