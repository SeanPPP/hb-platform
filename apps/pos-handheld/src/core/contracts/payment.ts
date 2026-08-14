import type { Money } from "./money";
import type { PaymentAttemptState } from "./state-machines";

export type PaymentProvider = "square" | "linkly-cloud" | "voucher";
export type PaymentOperation = "purchase" | "refund";

/**
 * 仅供 provider → 加密 attempt payload → 订单同步的内部证据。
 * 该结构不得挂到 PaymentAttempt、presenter、日志、审计或任何公开投影。
 */
export type CardSyncEvidenceV1 = Readonly<{
  version: 1;
  provider: "square" | "linkly-cloud";
  operation: PaymentOperation;
  processor: "Square" | "ANZ";
  txnRef: string | null;
  authCode: string | null;
  cardType: string | null;
  cardBin: number | null;
  maskedCardNumber: string | null;
  merchantId: string | null;
  responseCode: string | null;
  responseText: string | null;
  stan: string | null;
  bankDateTimeIso: string | null;
  /** 始终为正数绝对值；purchase/refund 方向由 operation 表达。 */
  amountCents: number;
  refundReference: string | null;
}>;

export type PaymentProviderReferences = Readonly<{
  checkoutId: string | null;
  paymentId: string | null;
  sessionId: string | null;
  txnRef: string | null;
  rfn: string | null;
  voucherReservationToken: string | null;
}>;

export type PaymentAttempt = Readonly<{
  attemptId: string;
  idempotencyKey: string;
  orderGuid: string;
  provider: PaymentProvider;
  operation: PaymentOperation;
  amount: Money;
  state: PaymentAttemptState;
  references: PaymentProviderReferences;
  createdAtIso: string;
  updatedAtIso: string;
  lastErrorCode: string | null;
  /** 支付恢复所需回单正文；数据库实现必须加密保存，禁止写日志。 */
  receiptText?: string | null;
  /** provider 返回的短响应码，不得包含 PAN、授权码或券码。 */
  responseCode?: string | null;
}>;

export type PaymentProviderResult = Readonly<{
  state: Extract<
    PaymentAttemptState,
    "Pending" | "Approved" | "Declined" | "Cancelled" | "Unknown"
  >;
  references: PaymentProviderReferences;
  receiptText: string | null;
  responseCode: string | null;
  /**
   * 仅 Approved 卡交易允许携带；PaymentAttemptService 必须在同一次 CAS 中
   * 交给仓储加密，绝不能复制到公开 PaymentAttempt。
   */
  protectedSyncEvidence?: CardSyncEvidenceV1 | null;
}>;

export interface OnlinePaymentPort {
  readonly provider: PaymentProvider;
  submit(attempt: PaymentAttempt): Promise<PaymentProviderResult>;
  recover(attempt: PaymentAttempt): Promise<PaymentProviderResult>;
  cancel(attempt: PaymentAttempt): Promise<PaymentProviderResult>;
  refund(attempt: PaymentAttempt): Promise<PaymentProviderResult>;
}

const CARD_SYNC_EVIDENCE_KEYS = new Set([
  "version",
  "provider",
  "operation",
  "processor",
  "txnRef",
  "authCode",
  "cardType",
  "cardBin",
  "maskedCardNumber",
  "merchantId",
  "responseCode",
  "responseText",
  "stan",
  "bankDateTimeIso",
  "amountCents",
  "refundReference",
]);

/** 在任何二次加密写入前建立严格、不可扩展的白名单边界。 */
export function normalizeCardSyncEvidence(
  input: unknown,
): CardSyncEvidenceV1 {
  if (!isRecord(input)) {
    throw new TypeError("Card sync evidence must be an object.");
  }
  for (const key of Object.keys(input)) {
    if (!CARD_SYNC_EVIDENCE_KEYS.has(key)) {
      throw new TypeError(`Card sync evidence contains unsupported field: ${key}.`);
    }
  }
  if (
    input.version !== 1 ||
    (input.provider !== "square" && input.provider !== "linkly-cloud") ||
    (input.operation !== "purchase" && input.operation !== "refund") ||
    (input.processor !== "Square" && input.processor !== "ANZ") ||
    (input.provider === "square") !== (input.processor === "Square")
  ) {
    throw new TypeError("Card sync evidence provider processor is invalid.");
  }
  const amountCents = input.amountCents;
  if (!Number.isSafeInteger(amountCents) || (amountCents as number) <= 0) {
    throw new TypeError(
      "Card sync evidence amount must be positive integer cents.",
    );
  }
  const maskedCardNumber = nullableMaskedCardNumber(input.maskedCardNumber);
  const cardBin =
    input.cardBin === null
      ? null
      : boundedInteger(input.cardBin, 0, 99_999_999, "card BIN");
  const bankDateTimeIso = nullableBankDateTime(input.bankDateTimeIso);
  return Object.freeze({
    version: 1 as const,
    provider: input.provider,
    operation: input.operation,
    processor: input.processor,
    txnRef: nullableProtectedText(input.txnRef, "transaction reference", 128),
    authCode: nullableProtectedText(input.authCode, "authorization code", 32),
    cardType: nullableProtectedText(input.cardType, "card type", 32),
    cardBin,
    maskedCardNumber,
    merchantId: nullableProtectedText(input.merchantId, "merchant id", 64),
    responseCode: nullableProtectedText(input.responseCode, "response code", 32),
    responseText: nullableProtectedText(input.responseText, "response text", 256),
    stan: nullableProtectedText(input.stan, "STAN", 32),
    bankDateTimeIso,
    amountCents: amountCents as number,
    refundReference: nullableProtectedText(
      input.refundReference,
      "refund reference",
      128,
    ),
  });
}

function nullableMaskedCardNumber(value: unknown): string | null {
  const normalized = nullableProtectedText(value, "masked card number", 40);
  if (normalized === null) return null;
  const digits = normalized.replace(/\D/gu, "");
  if (
    digits.length > 10 ||
    (digits.length > 4 && !/[*xX•]/u.test(normalized))
  ) {
    throw new TypeError("Card sync evidence masked card number is invalid.");
  }
  return normalized;
}

function nullableBankDateTime(value: unknown): string | null {
  const normalized = nullableProtectedText(value, "bank date time", 64);
  if (normalized === null) return null;
  if (!/(?:Z|[+-]\d{2}:\d{2})$/u.test(normalized)) {
    throw new TypeError("Card sync evidence bank date time needs a timezone.");
  }
  const milliseconds = Date.parse(normalized);
  if (!Number.isFinite(milliseconds)) {
    throw new TypeError("Card sync evidence bank date time is invalid.");
  }
  return new Date(milliseconds).toISOString();
}

function nullableProtectedText(
  value: unknown,
  label: string,
  maxLength: number,
): string | null {
  if (value === null) return null;
  if (typeof value !== "string") {
    throw new TypeError(`Card sync evidence ${label} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`Card sync evidence ${label} is invalid.`);
  }
  return normalized;
}

function boundedInteger(
  value: unknown,
  minimum: number,
  maximum: number,
  label: string,
): number {
  if (
    !Number.isSafeInteger(value) ||
    (value as number) < minimum ||
    (value as number) > maximum
  ) {
    throw new TypeError(`Card sync evidence ${label} is invalid.`);
  }
  return value as number;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
