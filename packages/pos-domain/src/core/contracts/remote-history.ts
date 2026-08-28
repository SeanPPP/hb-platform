export type RemoteOrderHistoryQuery = Readonly<{
  /** 调用方传入值不会覆盖当前可信收银会话门店。 */
  storeCode: string;
  deviceCode: string | null;
  soldFromIso: string;
  soldToIso: string;
  keyword: string | null;
  /** WPF 只读取首批 100；现有后端没有 cursor/skip。 */
  take: 100;
}>;

export type RemoteOrderHistorySummary = Readonly<{
  orderGuid: string;
  storeCode: string;
  deviceCode: string;
  cashierName: string;
  soldAtIso: string;
  totalCents: number;
  discountCents: number;
  actualAmountCents: number;
  lineCount: number;
  paymentSummary: string | null;
  statusLabel: string | null;
}>;

export type RemoteOrderHistoryLine = Readonly<{
  orderLineGuid: string;
  productCode: string;
  referenceCode: string | null;
  displayName: string;
  lookupCode: string | null;
  itemNumber: string | null;
  quantity: string;
  unitPriceCents: number;
  discountCents: number;
  actualAmountCents: number;
  kind: "sale" | "return";
}>;

/**
 * 远程历史付款只暴露读者所需的脱敏预览。
 * provider ID、授权码、PAN、券码、reservation token 和原始回单不属于此合同。
 */
export type RemoteOrderPaymentPreview = Readonly<{
  paymentGuid: string;
  method: "cash" | "card" | "voucher";
  amountCents: number;
  displayReference: string | null;
  cardType: string | null;
  maskedCardNumber: string | null;
}>;

export type RemoteOrderHistoryDetails = Readonly<{
  orderGuid: string;
  storeCode: string;
  deviceCode: string;
  cashierName: string;
  soldAtIso: string;
  totalCents: number;
  discountCents: number;
  actualAmountCents: number;
  lines: readonly RemoteOrderHistoryLine[];
  payments: readonly RemoteOrderPaymentPreview[];
}>;

export interface RemoteOrderHistoryPort {
  list(
    query: RemoteOrderHistoryQuery,
  ): Promise<readonly RemoteOrderHistorySummary[]>;
  getDetails(
    orderGuid: string,
  ): Promise<RemoteOrderHistoryDetails | null>;
}

export function normalizeRemoteHistoryQuery(
  input: Readonly<{
    storeCode?: string | null;
    deviceCode?: string | null;
    soldFromIso: string;
    soldToIso: string;
    keyword?: string | null;
    take?: number;
  }>,
  trustedStoreCode: string,
): RemoteOrderHistoryQuery {
  const storeCode = requiredText(
    trustedStoreCode,
    "remote history trusted store",
    64,
  );
  const soldFromIso = canonicalIso(input.soldFromIso);
  const soldToIso = canonicalIso(input.soldToIso);
  if (
    soldFromIso === null ||
    soldToIso === null ||
    Date.parse(soldFromIso) > Date.parse(soldToIso)
  ) {
    throw new TypeError("Remote history date range is invalid.");
  }
  return Object.freeze({
    storeCode,
    deviceCode: optionalText(
      input.deviceCode,
      "remote history device",
      128,
    ),
    soldFromIso,
    soldToIso,
    keyword: optionalText(input.keyword, "remote history keyword", 128),
    take: 100 as const,
  });
}

function canonicalIso(value: unknown): string | null {
  if (
    typeof value !== "string" ||
    !/(?:Z|[+-]\d{2}:\d{2})$/u.test(value)
  ) {
    return null;
  }
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? new Date(timestamp).toISOString() : null;
}

function requiredText(value: unknown, label: string, maximum: number): string {
  const normalized = optionalText(value, label, maximum);
  if (normalized === null) throw new TypeError(`${label} is required.`);
  return normalized;
}

function optionalText(
  value: unknown,
  label: string,
  maximum: number,
): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw new TypeError(`${label} is invalid.`);
  const normalized = value.trim();
  if (normalized.length === 0) return null;
  if (
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized;
}
