import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import {
  normalizeRemoteHistoryQuery,
  type RemoteOrderHistoryDetails,
  type RemoteOrderHistoryLine,
  type RemoteOrderPaymentPreview,
  type RemoteOrderHistoryPort,
  type RemoteOrderHistoryQuery,
  type RemoteOrderHistorySummary,
} from "@/core/contracts/remote-history";
import type { components } from "@/generated/hbpos/schema";

type GeneratedHistoryResponse =
  components["schemas"]["OrderHistoryQueryResponse"];
type GeneratedHistorySummary =
  components["schemas"]["OrderHistorySummaryDto"];
type GeneratedHistoryDetails =
  components["schemas"]["OrderHistoryDetailsDto"];
type GeneratedHistoryLine = components["schemas"]["OrderHistoryLineDto"];
type GeneratedHistoryPayment =
  components["schemas"]["OrderHistoryPaymentDto"];
type GeneratedCardTransaction = components["schemas"]["CardTransactionDto"];

/**
 * 远程历史只读适配器。门店由构造时的可信会话固定，调用方 query 中的门店永远不会下传。
 */
export class HbposRemoteHistoryApi implements RemoteOrderHistoryPort {
  private readonly trustedStoreCode: string;

  public constructor(
    private readonly transport: HbposTransport,
    trustedStoreCode: string,
  ) {
    this.trustedStoreCode = requiredText(
      trustedStoreCode,
      "Remote history trusted store",
      64,
    );
  }

  public async list(
    input: RemoteOrderHistoryQuery,
  ): Promise<readonly RemoteOrderHistorySummary[]> {
    const query = normalizeRemoteHistoryQuery(input, this.trustedStoreCode);
    const response = await this.transport.request<
      HbposEnvelope<GeneratedHistoryResponse>
    >({
      method: "GET",
      url: "/api/v1/orders/history",
      params: {
        storeCode: this.trustedStoreCode,
        deviceCode: query.deviceCode ?? undefined,
        soldFrom: query.soldFromIso,
        soldTo: query.soldToIso,
        keyword: query.keyword ?? undefined,
        take: 100,
      },
    });
    const body = unwrapHbposEnvelope(response.data);
    const orders = requiredArray(body.orders, "Remote history orders");
    return Object.freeze(
      orders.map((order) => this.mapSummary(order)),
    );
  }

  public async getDetails(
    orderGuid: string,
  ): Promise<RemoteOrderHistoryDetails | null> {
    const requestedOrderGuid = requiredUuid(
      orderGuid,
      "Remote history order",
    );
    const response = await this.transport.request<
      HbposEnvelope<GeneratedHistoryDetails | null>
    >({
      method: "GET",
      url: `/api/v1/orders/history/${encodeURIComponent(requestedOrderGuid)}`,
    });
    const body = unwrapHbposEnvelope(response.data);
    if (body === null) return null;

    const mappedOrderGuid = requiredUuid(
      body.orderGuid,
      "Remote history details order",
    );
    if (mappedOrderGuid !== requestedOrderGuid) {
      throw new TypeError("Remote history details order does not match request.");
    }
    return Object.freeze({
      orderGuid: mappedOrderGuid,
      storeCode: this.checkedStore(body.storeCode),
      deviceCode: requiredText(
        body.deviceCode,
        "Remote history details device",
        128,
      ),
      cashierName: requiredText(
        body.cashierName,
        "Remote history details cashier",
        128,
      ),
      soldAtIso: requiredIso(body.soldAt, "Remote history details soldAt"),
      totalCents: moneyToCents(
        body.totalAmount,
        "Remote history details total money",
      ),
      discountCents: moneyToCents(
        body.discountAmount,
        "Remote history details discount money",
      ),
      actualAmountCents: moneyToCents(
        body.actualAmount,
        "Remote history details actual money",
      ),
      lines: Object.freeze(
        requiredArray(body.lines, "Remote history details lines").map(
          mapLine,
        ),
      ),
      payments: Object.freeze(
        requiredArray(body.payments, "Remote history details payments").map(
          mapPayment,
        ),
      ),
    });
  }

  private mapSummary(
    value: GeneratedHistorySummary,
  ): RemoteOrderHistorySummary {
    return Object.freeze({
      orderGuid: requiredUuid(value.orderGuid, "Remote history order"),
      storeCode: this.checkedStore(value.storeCode),
      deviceCode: requiredText(
        value.deviceCode,
        "Remote history device",
        128,
      ),
      cashierName: requiredText(
        value.cashierName,
        "Remote history cashier",
        128,
      ),
      soldAtIso: requiredIso(value.soldAt, "Remote history soldAt"),
      totalCents: moneyToCents(
        value.totalAmount,
        "Remote history total money",
      ),
      discountCents: moneyToCents(
        value.discountAmount,
        "Remote history discount money",
      ),
      actualAmountCents: moneyToCents(
        value.actualAmount,
        "Remote history actual money",
      ),
      lineCount: requiredNonNegativeInteger(
        value.lineCount,
        "Remote history line count",
      ),
      paymentSummary: optionalText(
        value.paymentSummary,
        "Remote history payment summary",
        128,
      ),
      statusLabel: optionalText(
        value.statusLabel,
        "Remote history status",
        64,
      ),
    });
  }

  private checkedStore(value: unknown): string {
    const responseStore = requiredText(
      value,
      "Remote history response store",
      64,
    );
    if (responseStore.toUpperCase() !== this.trustedStoreCode.toUpperCase()) {
      throw new TypeError(
        "Remote history response store is outside the trusted store.",
      );
    }
    return this.trustedStoreCode;
  }
}

function mapLine(value: GeneratedHistoryLine): RemoteOrderHistoryLine {
  return Object.freeze({
    orderLineGuid: requiredUuid(
      value.orderLineGuid,
      "Remote history line id",
    ),
    productCode: requiredText(
      value.productCode,
      "Remote history product",
      128,
    ),
    referenceCode: optionalText(
      value.referenceCode,
      "Remote history product reference",
      128,
    ),
    displayName: requiredText(
      value.displayName,
      "Remote history product name",
      256,
    ),
    lookupCode: optionalText(
      value.lookupCode,
      "Remote history lookup code",
      128,
    ),
    itemNumber: optionalText(
      value.itemNumber,
      "Remote history item number",
      128,
    ),
    quantity: decimalNumberToText(
      value.quantity,
      "Remote history quantity",
    ),
    unitPriceCents: moneyToCents(
      value.unitPrice,
      "Remote history unit price money",
    ),
    discountCents: moneyToCents(
      value.discountAmount,
      "Remote history line discount money",
    ),
    actualAmountCents: moneyToCents(
      value.actualAmount,
      "Remote history line actual money",
    ),
    kind: value.kind === 1 ? "sale" : value.kind === 2 ? "return" : invalidKind(),
  });
}

function mapPayment(
  value: GeneratedHistoryPayment,
): RemoteOrderPaymentPreview {
  const cards = Array.isArray(value.cardTransactions)
    ? value.cardTransactions
    : [];
  return Object.freeze({
    paymentGuid: requiredUuid(
      value.paymentGuid,
      "Remote history payment id",
    ),
    method: paymentMethod(value.method),
    amountCents: moneyToCents(
      value.amount,
      "Remote history payment money",
    ),
    // Payment.Reference 可能是 provider ID、券码或 reservation token，历史预览一律不保留。
    displayReference: null,
    cardType: firstSafeCardValue(cards, (card) =>
      safeOptionalText(card.cardType, 32),
    ),
    maskedCardNumber: firstSafeCardValue(cards, (card) =>
      safeMaskedCardNumber(card.maskedCardNumber),
    ),
  });
}

function firstSafeCardValue(
  values: readonly GeneratedCardTransaction[],
  select: (value: GeneratedCardTransaction) => string | null,
): string | null {
  for (const value of values) {
    const selected = select(value);
    if (selected !== null) return selected;
  }
  return null;
}

function safeMaskedCardNumber(value: unknown): string | null {
  const normalized = safeOptionalText(value, 32);
  if (
    normalized === null ||
    !/^[0-9xX*•\s-]+$/u.test(normalized) ||
    !/[xX*•]/u.test(normalized)
  ) {
    return null;
  }
  // 最多只接受 BIN + last4 的已掩码展示；连续或完整 PAN 会被丢弃。
  const digitCount = (normalized.match(/\d/gu) ?? []).length;
  return digitCount <= 10 ? normalized : null;
}

function safeOptionalText(value: unknown, maximum: number): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    return null;
  }
  return normalized;
}

function paymentMethod(value: unknown): "cash" | "card" | "voucher" {
  if (value === 1) return "cash";
  if (value === 2) return "card";
  if (value === 3) return "voucher";
  throw new TypeError("Remote history payment method is invalid.");
}

function invalidKind(): never {
  throw new TypeError("Remote history line kind is invalid.");
}

function moneyToCents(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new TypeError(`${label} is invalid.`);
  }
  const scaled = value * 100;
  const cents = Math.round(scaled);
  if (
    !Number.isSafeInteger(cents) ||
    Math.abs(scaled - cents) > 0.000001
  ) {
    throw new TypeError(`${label} must have no more than two decimals.`);
  }
  return Object.is(cents, -0) ? 0 : cents;
}

function decimalNumberToText(value: unknown, label: string): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new TypeError(`${label} is invalid.`);
  }
  const text = Object.is(value, -0) ? "0" : String(value).toLowerCase();
  if (!text.includes("e")) return text;

  const [mantissa = "", exponentText = ""] = text.split("e");
  const exponent = Number(exponentText);
  if (!Number.isInteger(exponent)) throw new TypeError(`${label} is invalid.`);
  const negative = mantissa.startsWith("-");
  const unsigned = negative ? mantissa.slice(1) : mantissa;
  const [whole = "", fraction = ""] = unsigned.split(".");
  const digits = `${whole}${fraction}`;
  const decimalIndex = whole.length + exponent;
  const expanded =
    decimalIndex <= 0
      ? `0.${"0".repeat(-decimalIndex)}${digits}`
      : decimalIndex >= digits.length
        ? `${digits}${"0".repeat(decimalIndex - digits.length)}`
        : `${digits.slice(0, decimalIndex)}.${digits.slice(decimalIndex)}`;
  return negative ? `-${expanded}` : expanded;
}

function requiredIso(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    !/(?:Z|[+-]\d{2}:\d{2})$/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) throw new TypeError(`${label} is invalid.`);
  return new Date(timestamp).toISOString();
}

function requiredUuid(value: unknown, label: string): string {
  const normalized = requiredText(value, label, 36).toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized;
}

function requiredNonNegativeInteger(value: unknown, label: string): number {
  if (!Number.isSafeInteger(value) || Number(value) < 0) {
    throw new TypeError(`${label} is invalid.`);
  }
  return Number(value);
}

function requiredText(
  value: unknown,
  label: string,
  maximum: number,
): string {
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

function requiredArray<T>(
  value: readonly T[] | null | undefined,
  label: string,
): readonly T[] {
  if (!Array.isArray(value)) throw new TypeError(`${label} is invalid.`);
  return value;
}
