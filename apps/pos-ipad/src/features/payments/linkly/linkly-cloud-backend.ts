import { paymentProviderAmountCents } from "@hb/pos-payments-core/features/payments/payment-amount";

import {
  HbposApiError,
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import {
  normalizeCardSyncEvidence,
  type CardSyncEvidenceV1,
  type OnlinePaymentPort,
  type PaymentAttempt,
  type PaymentProviderResult,
} from "@hb/pos-domain/core/contracts/payment";
import type { components } from "@hb/pos-api-client/openapi";

type LinklySessionDto = components["schemas"]["LinklyCloudBackendSessionResponse"];
type LinklyCardTransactionDto = components["schemas"]["LinklyCloudBackendCardTransactionDto"];

export type LinklyCloudBackendSession = Readonly<{
  environment: string;
  storeCode: string;
  deviceCode: string;
  sessionId: string;
  status: string;
  txnRef: string | null;
  responseCode: string | null;
  responseText: string | null;
  recoveryAction: string | null;
  displayText: string | null;
  cancelKeyFlag: boolean;
  okKeyFlag: boolean;
  acceptYesKeyFlag: boolean;
  declineNoKeyFlag: boolean;
  authoriseKeyFlag: boolean;
  inputType: string | null;
  graphicCode: string | null;
  displayLines: readonly string[];
  receiptText: string | null;
  recoveryCount: number;
  receiptPrintedAt: string | null;
  clientAcknowledgedAt: string | null;
  lastHttpStatus: number | null;
  notifications: readonly Readonly<{ type: string; payloadJson: string; receivedAt: string }>[];
  transactionSuccess: boolean | null;
  cardTransaction?: LinklyCardTransactionDto | null;
}>;

export type LinklyCloudBackendProviderOptions = Readonly<{ environment: string }>;

/** iPad 仅调用 Hbpos.Api；Linkly terminal secret 和 POS ID 永不下发到客户端。 */
export class LinklyCloudBackendApi {
  public constructor(private readonly transport: HbposTransport) {}

  public create(input: components["schemas"]["LinklyCloudBackendTransactionRequest"]): Promise<LinklyCloudBackendSession> {
    return this.requestSession({ method: "POST", url: "/api/v1/linkly/cloud-backend/transactions", data: input });
  }

  public active(environment: string): Promise<LinklyCloudBackendSession | null> {
    return this.requestOptionalSession({ method: "GET", url: "/api/v1/linkly/cloud-backend/transactions/active", params: { environment } });
  }

  public resumable(environment: string): Promise<LinklyCloudBackendSession | null> {
    return this.requestOptionalSession({ method: "GET", url: "/api/v1/linkly/cloud-backend/transactions/resumable", params: { environment } });
  }

  public status(environment: string, sessionId: string): Promise<LinklyCloudBackendSession> {
    return this.requestSession({ method: "GET", url: sessionUrl(sessionId, "status"), params: { environment } });
  }

  public recover(environment: string, sessionId: string): Promise<LinklyCloudBackendSession> {
    return this.requestSession({ method: "POST", url: sessionUrl(sessionId, "recover"), data: { environment } });
  }

  public sendKey(environment: string, sessionId: string, key: string, data: string | null): Promise<LinklyCloudBackendSession> {
    return this.requestSession({ method: "POST", url: sessionUrl(sessionId, "sendkey"), data: { environment, key, data } });
  }

  public markReceiptPrinted(environment: string, sessionId: string): Promise<LinklyCloudBackendSession> {
    return this.requestSession({ method: "POST", url: sessionUrl(sessionId, "receipt/printed"), data: { environment } });
  }

  public acknowledge(environment: string, sessionId: string): Promise<LinklyCloudBackendSession> {
    return this.requestSession({ method: "POST", url: sessionUrl(sessionId, "acknowledge"), params: { environment }, data: { environment } });
  }

  private async requestSession(request: Parameters<HbposTransport["request"]>[0]): Promise<LinklyCloudBackendSession> {
    const response = await this.transport.request<HbposEnvelope<LinklySessionDto>>(request);
    if (response.status === 404) throw sessionNotFound();
    return normalizeSession(unwrapHbposEnvelope(response.data));
  }

  private async requestOptionalSession(request: Parameters<HbposTransport["request"]>[0]): Promise<LinklyCloudBackendSession | null> {
    try {
      const response = await this.transport.request<HbposEnvelope<LinklySessionDto>>(request);
      if (response.status === 404) return null;
      return normalizeSession(unwrapHbposEnvelope(response.data));
    } catch (error) {
      if (isNotFound(error)) return null;
      throw error;
    }
  }
}

/**
 * Linkly Backend Async 的支付 Provider。create 一旦进入传输歧义，绝不重发 POST；
 * 没有已持久 SessionId 时只允许通过已持久 UID 强匹配 active/resumable，绝不凭同额认领。
 */
export class LinklyCloudBackendProvider implements OnlinePaymentPort {
  public readonly provider = "linkly-cloud" as const;

  public constructor(
    private readonly api: LinklyCloudBackendApi,
    private readonly options: LinklyCloudBackendProviderOptions,
  ) {}

  public async submit(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    linklyProviderAmountCents(attempt);
    if (attempt.operation === "refund") return this.refund(attempt);
    if (attempt.state === "Unknown" || attempt.references.sessionId) return this.recover(attempt);

    const active = await this.api.active(this.options.environment);
    // 这是另一笔未完成交易，不能把它的 SessionId/TxnRef 绑定到当前新订单。
    if (active) return activeSessionConflict(attempt);
    try {
      const created = await this.api.create(transactionRequest(attempt, this.options.environment));
      return toPaymentResult(created, attempt);
    } catch (error) {
      if (isActiveSessionConflict(error)) return activeSessionConflict(attempt);
      if (!isCreateAmbiguous(error)) throw error;
      return this.recoverAmbiguousCreate(attempt);
    }
  }

  public async recover(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    linklyProviderAmountCents(attempt);
    if (attempt.references.sessionId) {
      const recovered = await this.api.recover(this.options.environment, attempt.references.sessionId);
      return toPaymentResult(recovered, attempt);
    }
    return this.recoverAmbiguousCreate(attempt);
  }

  public async cancel(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    linklyProviderAmountCents(attempt);
    // Unknown 只能恢复，绝不能在不知道终端是否已扣款时自动发送取消键。
    if (attempt.state === "Unknown" || !attempt.references.sessionId) return unknownResult(attempt);
    const session = await this.api.sendKey(this.options.environment, attempt.references.sessionId, "CANCEL", null);
    return toPaymentResult(session, attempt);
  }

  public async refund(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    linklyProviderAmountCents(attempt);
    if (attempt.references.rfn === null) return { state: "Declined", references: attempt.references, receiptText: null, responseCode: "LINKLY_RFN_REQUIRED" };
    if (attempt.state === "Unknown" || attempt.references.sessionId) return this.recover(attempt);

    const active = await this.api.active(this.options.environment);
    if (active) return activeSessionConflict(attempt);
    try {
      return toPaymentResult(await this.api.create(transactionRequest(attempt, this.options.environment)), attempt);
    } catch (error) {
      if (isActiveSessionConflict(error)) return activeSessionConflict(attempt);
      if (!isCreateAmbiguous(error)) throw error;
      return this.recoverAmbiguousCreate(attempt);
    }
  }

  private async recoverAmbiguousCreate(attempt: PaymentAttempt): Promise<PaymentProviderResult> {
    const recoveryUid = normalizeRecoveryUid(attempt.idempotencyKey);
    if (recoveryUid === null) return unknownResult(attempt);

    const active = await this.api.active(this.options.environment);
    const activeScope = active === null
      ? null
      : matchingRecoveryScope(active, attempt, this.options.environment, recoveryUid);
    if (active !== null && activeScope !== null) {
      return this.recoverMatchedSession(attempt, active, activeScope, recoveryUid);
    }

    const resumable = await this.api.resumable(this.options.environment);
    const resumableScope = resumable === null
      ? null
      : matchingRecoveryScope(resumable, attempt, this.options.environment, recoveryUid);
    if (resumable === null || resumableScope === null) return unknownResult(attempt);
    return this.recoverMatchedSession(attempt, resumable, resumableScope, recoveryUid);
  }

  private async recoverMatchedSession(
    attempt: PaymentAttempt,
    candidate: LinklyCloudBackendSession,
    expectedScope: LinklyRecoveryScope,
    recoveryUid: string,
  ): Promise<PaymentProviderResult> {
    const status = await this.api.status(this.options.environment, candidate.sessionId);
    if (!matchesRecoveryScope(status, expectedScope) ||
      matchingRecoveryScope(status, attempt, this.options.environment, recoveryUid) === null) {
      return unknownResult(attempt);
    }

    const statusResult = toPaymentResult(status, attempt);
    if (isFinalPaymentState(statusResult.state)) return statusResult;

    const recovered = await this.api.recover(this.options.environment, candidate.sessionId);
    if (!matchesRecoveryScope(recovered, expectedScope) ||
      matchingRecoveryScope(recovered, attempt, this.options.environment, recoveryUid) === null) {
      return unknownResult(attempt);
    }
    return toPaymentResult(recovered, attempt);
  }
}

type LinklyRecoveryScope = Readonly<{
  environment: string;
  storeCode: string;
  deviceCode: string;
  sessionId: string;
  txnRef: string;
}>;

type LinklyRecoveryIdentity = Readonly<{
  uid: string;
  txnType: "P" | "R";
  amountCents: number;
  txnRef: string;
}>;

function matchingRecoveryScope(
  session: LinklyCloudBackendSession,
  attempt: PaymentAttempt,
  environment: string,
  recoveryUid: string,
): LinklyRecoveryScope | null {
  // active/resumable 已由 Hbpos.Api 按当前门店/设备 claim 隔离；客户端仍要求后续响应保持同一作用域。
  if (!sameCaseInsensitiveIdentity(session.environment, environment) ||
    session.storeCode.trim().length === 0 ||
    session.deviceCode.trim().length === 0 ||
    session.sessionId.trim().length === 0 ||
    session.txnRef === null ||
    session.txnRef.trim().length === 0) {
    return null;
  }

  const identities: LinklyRecoveryIdentity[] = [];
  for (const notification of session.notifications) {
    if (!sameCaseInsensitiveIdentity(notification.type, "transaction")) continue;
    const identity = parseRecoveryIdentity(notification.payloadJson);
    // 任意 transaction 通知无法验证时都失败关闭，避免忽略冲突证据后误绑定。
    if (identity === null) return null;
    identities.push(identity);
  }
  if (identities.length === 0) return null;

  const first = identities[0]!;
  if (identities.some((identity) => !sameRecoveryIdentity(identity, first)) ||
    first.uid !== recoveryUid ||
    first.txnType !== (attempt.operation === "refund" ? "R" : "P") ||
    first.amountCents !== linklyProviderAmountCents(attempt) ||
    !sameIdentity(first.txnRef, session.txnRef)) {
    return null;
  }

  return {
    environment: session.environment.trim(),
    storeCode: session.storeCode.trim(),
    deviceCode: session.deviceCode.trim(),
    sessionId: session.sessionId.trim(),
    txnRef: session.txnRef.trim(),
  };
}

function parseRecoveryIdentity(payloadJson: string): LinklyRecoveryIdentity | null {
  try {
    const parsed: unknown = JSON.parse(payloadJson);
    if (!isRecord(parsed)) return null;
    const responses = recordValues(parsed, "Response");
    if (responses.length > 1) return null;
    let source = parsed;
    if (responses.length === 1) {
      const response = responses[0];
      if (!isRecord(response)) return null;
      source = response;
    }
    const txnTypeValue = recordValue(source, "TxnType");
    const amountValue = recordValue(source, "AmtPurchase");
    const txnRefValue = recordValue(source, "TxnRef");
    const purchaseAnalysisData = recordValue(source, "PurchaseAnalysisData");
    if ((txnTypeValue !== "P" && txnTypeValue !== "R") ||
      typeof amountValue !== "number" ||
      !Number.isSafeInteger(amountValue) ||
      amountValue === 0 ||
      typeof txnRefValue !== "string" ||
      !txnRefValue.trim() ||
      !isRecord(purchaseAnalysisData)) {
      return null;
    }
    const uid = normalizeRecoveryUid(recordValue(purchaseAnalysisData, "UID"));
    if (uid === null) return null;
    return {
      uid,
      txnType: txnTypeValue,
      amountCents: Math.abs(amountValue),
      txnRef: txnRefValue.trim(),
    };
  } catch {
    return null;
  }
}

function recordValue(
  value: Readonly<Record<string, unknown>>,
  field: string,
): unknown {
  const values = recordValues(value, field);
  return values.length === 1 ? values[0] : undefined;
}

function recordValues(
  value: Readonly<Record<string, unknown>>,
  field: string,
): readonly unknown[] {
  return Object.entries(value)
    .filter(([key]) => key.toLowerCase() === field.toLowerCase())
    .map(([, fieldValue]) => fieldValue);
}

function normalizeRecoveryUid(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u
    .test(normalized)
    ? normalized
    : null;
}

function sameCaseInsensitiveIdentity(left: string, right: string): boolean {
  return left.trim().toLowerCase() === right.trim().toLowerCase();
}

function sameRecoveryIdentity(
  left: LinklyRecoveryIdentity,
  right: LinklyRecoveryIdentity,
): boolean {
  return left.uid === right.uid &&
    left.txnType === right.txnType &&
    left.amountCents === right.amountCents &&
    left.txnRef === right.txnRef;
}

function matchesRecoveryScope(
  session: LinklyCloudBackendSession,
  expected: LinklyRecoveryScope,
): boolean {
  return sameCaseInsensitiveIdentity(session.environment, expected.environment) &&
    session.storeCode.trim() === expected.storeCode &&
    session.deviceCode.trim() === expected.deviceCode &&
    session.sessionId.trim() === expected.sessionId &&
    session.txnRef?.trim() === expected.txnRef;
}

function isFinalPaymentState(
  state: PaymentProviderResult["state"],
): boolean {
  return state === "Approved" || state === "Declined" || state === "Cancelled";
}

function transactionRequest(attempt: PaymentAttempt, environment: string): components["schemas"]["LinklyCloudBackendTransactionRequest"] {
  const request: components["schemas"]["LinklyCloudBackendTransactionRequest"] = {
    environment,
    txnType: attempt.operation === "refund" ? "R" : "P",
    amtPurchase: linklyProviderAmountCents(attempt),
  };
  const purchaseAnalysisData: Record<string, string> = {};
  const recoveryUid = normalizeRecoveryUid(attempt.idempotencyKey);
  // Linkly PAD UID 是会在结果中回显的 UUID v4 关联值；它只用于认领恢复，绝不授权重发 create。
  if (recoveryUid !== null) purchaseAnalysisData.UID = recoveryUid;
  if (attempt.operation === "refund" && attempt.references.rfn) {
    purchaseAnalysisData.RFN = attempt.references.rfn;
  }
  if (Object.keys(purchaseAnalysisData).length > 0) {
    request.purchaseAnalysisData = purchaseAnalysisData;
  }
  return request;
}

function toPaymentResult(session: LinklyCloudBackendSession, attempt: PaymentAttempt): PaymentProviderResult {
  const state = sessionState(session);
  const sessionReferences = {
    checkoutId: null,
    paymentId: null,
    sessionId: session.sessionId,
    txnRef: session.txnRef,
    // 非终态仍保留旧兼容逻辑；Approved 会使用已验证的结构化 RFN 覆盖。
    rfn: attempt.operation === "purchase" ? session.txnRef : attempt.references.rfn,
    voucherReservationToken: null,
  };
  // 恢复中的异常响应不得把另一笔会话身份写回本地；新建会话仍保留服务端签发的 SessionId。
  const references = state === "Unknown" && attempt.references.sessionId !== null
    ? attempt.references
    : sessionReferences;
  const result: PaymentProviderResult = {
    state,
    references,
    receiptText: session.receiptText,
    responseCode: session.responseCode,
  };
  if (state !== "Approved") return result;

  const evidence = buildApprovedCardSyncEvidence(session, attempt);
  if (!evidence.ok) {
    return {
      state: "Unknown",
      references: attempt.references.sessionId === null
        ? references
        : attempt.references,
      receiptText: session.receiptText,
      responseCode: evidence.code,
    };
  }

  return {
    ...result,
    references: {
      ...references,
      rfn: evidence.value.refundReference,
    },
    protectedSyncEvidence: evidence.value,
  };
}

function unknownResult(attempt: PaymentAttempt): PaymentProviderResult {
  return { state: "Unknown", references: attempt.references, receiptText: null, responseCode: "LINKLY_SESSION_UNRESOLVED" };
}

function activeSessionConflict(attempt: PaymentAttempt): PaymentProviderResult {
  return {
    state: "Declined",
    references: {
      checkoutId: null,
      paymentId: null,
      sessionId: null,
      txnRef: null,
      rfn: attempt.operation === "refund" ? attempt.references.rfn : null,
      voucherReservationToken: null,
    },
    receiptText: null,
    responseCode: "LINKLY_ACTIVE_SESSION_CONFLICT",
  };
}

const LINKLY_PENDING_STATUSES = new Set([
  "pending",
  "tokenrefreshrequired",
]);

const LINKLY_DECLINED_STATUSES = new Set([
  "completed",
  "failed",
  "declined",
  "notsubmitted",
]);

function sessionState(session: LinklyCloudBackendSession): PaymentProviderResult["state"] {
  const status = session.status.trim().toLowerCase();
  if (!status) return "Unknown";

  if (status === "cancelled" || status === "canceled") {
    return session.transactionSuccess === false ? "Cancelled" : "Unknown";
  }

  if (LINKLY_PENDING_STATUSES.has(status)) {
    return session.transactionSuccess === null ? "Pending" : "Unknown";
  }

  if (status === "completed" && session.transactionSuccess === true) {
    return "Approved";
  }

  if (LINKLY_DECLINED_STATUSES.has(status) && session.transactionSuccess === false) {
    return "Declined";
  }

  // 新状态、缺失最终结果或互相矛盾的字段都必须等待同一 SessionId 恢复。
  return "Unknown";
}

function normalizeSession(value: LinklySessionDto): LinklyCloudBackendSession {
  return {
    environment: requiredText(value.environment, "environment"), storeCode: requiredText(value.storeCode, "storeCode"), deviceCode: requiredText(value.deviceCode, "deviceCode"),
    sessionId: requiredText(value.sessionId, "sessionId"), status: typeof value.status === "string" ? value.status : "", txnRef: optionalText(value.txnRef), responseCode: optionalText(value.responseCode),
    responseText: optionalText(value.responseText), recoveryAction: optionalText(value.recoveryAction), displayText: optionalText(value.displayText), cancelKeyFlag: Boolean(value.cancelKeyFlag),
    okKeyFlag: Boolean(value.okKeyFlag), acceptYesKeyFlag: Boolean(value.acceptYesKeyFlag), declineNoKeyFlag: Boolean(value.declineNoKeyFlag), authoriseKeyFlag: Boolean(value.authoriseKeyFlag),
    inputType: optionalText(value.inputType), graphicCode: optionalText(value.graphicCode), displayLines: (value.displayLines ?? []).map((line) => requiredText(line, "displayLines")), receiptText: optionalText(value.receiptText),
    recoveryCount: integer(value.recoveryCount ?? 0, "recoveryCount"), receiptPrintedAt: optionalText(value.receiptPrintedAt), clientAcknowledgedAt: optionalText(value.clientAcknowledgedAt),
    lastHttpStatus: value.lastHttpStatus === null || value.lastHttpStatus === undefined ? null : integer(value.lastHttpStatus, "lastHttpStatus"),
    notifications: (value.notifications ?? []).map((notification) => ({ type: requiredText(notification.type, "notification.type"), payloadJson: requiredText(notification.payloadJson, "notification.payloadJson"), receivedAt: requiredText(notification.receivedAt, "notification.receivedAt") })),
    transactionSuccess: value.transactionSuccess ?? null,
    cardTransaction: value.cardTransaction ?? null,
  };
}

const LINKLY_CARD_TRANSACTION_KEYS = new Set([
  "txnRef",
  "rfn",
  "authCode",
  "cardType",
  "maskedCardNumber",
  "merchantId",
  "responseCode",
  "responseText",
  "stan",
  "bankDateTime",
  "amountCents",
]);

type ApprovedEvidenceResult =
  | Readonly<{ ok: true; value: CardSyncEvidenceV1 }>
  | Readonly<{
      ok: false;
      code:
        | "LINKLY_CARD_EVIDENCE_REQUIRED"
        | "LINKLY_CARD_EVIDENCE_INVALID"
        | "LINKLY_CARD_EVIDENCE_MISMATCH";
    }>;

function buildApprovedCardSyncEvidence(
  session: LinklyCloudBackendSession,
  attempt: PaymentAttempt,
): ApprovedEvidenceResult {
  const raw = session.cardTransaction as unknown;
  if (raw === null || raw === undefined) {
    return { ok: false, code: "LINKLY_CARD_EVIDENCE_REQUIRED" };
  }
  if (
    !isRecord(raw) ||
    Object.keys(raw).some((key) => !LINKLY_CARD_TRANSACTION_KEYS.has(key))
  ) {
    return { ok: false, code: "LINKLY_CARD_EVIDENCE_INVALID" };
  }

  let evidence: CardSyncEvidenceV1;
  try {
    // 只逐字段映射后端脱敏 DTO；notifications、receipt 和任何额外 payload 永不进入证据。
    evidence = normalizeCardSyncEvidence({
      version: 1,
      provider: "linkly-cloud",
      operation: attempt.operation,
      processor: "ANZ",
      txnRef: nullableDtoField(raw, "txnRef"),
      authCode: nullableDtoField(raw, "authCode"),
      cardType: nullableDtoField(raw, "cardType"),
      cardBin: null,
      maskedCardNumber: nullableDtoField(raw, "maskedCardNumber"),
      merchantId: nullableDtoField(raw, "merchantId"),
      responseCode: nullableDtoField(raw, "responseCode"),
      responseText: nullableDtoField(raw, "responseText"),
      stan: nullableDtoField(raw, "stan"),
      bankDateTimeIso: nullableDtoField(raw, "bankDateTime"),
      amountCents: raw.amountCents,
      refundReference: nullableDtoField(raw, "rfn"),
    });
  } catch {
    return { ok: false, code: "LINKLY_CARD_EVIDENCE_INVALID" };
  }

  const expectedAmountCents = linklyProviderAmountCents(attempt);
  if (
    evidence.amountCents !== expectedAmountCents ||
    evidence.txnRef === null ||
    evidence.refundReference === null ||
    !sameIdentity(evidence.txnRef, session.txnRef) ||
    (attempt.references.sessionId !== null &&
      !sameIdentity(attempt.references.sessionId, session.sessionId)) ||
    (attempt.references.sessionId !== null &&
      attempt.references.txnRef !== null &&
      !sameIdentity(attempt.references.txnRef, evidence.txnRef)) ||
    (attempt.operation === "refund" &&
      !sameIdentity(attempt.references.rfn, evidence.refundReference))
  ) {
    return { ok: false, code: "LINKLY_CARD_EVIDENCE_MISMATCH" };
  }

  return { ok: true, value: evidence };
}

function nullableDtoField(
  value: Readonly<Record<string, unknown>>,
  field: string,
): unknown {
  return value[field] === undefined ? null : value[field];
}

function sameIdentity(left: unknown, right: unknown): boolean {
  return typeof left === "string" &&
    typeof right === "string" &&
    left.trim().length > 0 &&
    left.trim() === right.trim();
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function sessionUrl(sessionId: string, suffix: string): string { return `/api/v1/linkly/cloud-backend/transactions/${encodeURIComponent(sessionId)}/${suffix}`; }
function requiredText(value: unknown, field: string): string { if (typeof value !== "string" || !value.trim()) throw new Error(`Invalid Linkly ${field}.`); return value; }
function optionalText(value: unknown): string | null { return typeof value === "string" && value ? value : null; }
function integer(value: unknown, field: string): number { if (typeof value !== "number" || !Number.isSafeInteger(value)) throw new Error(`Invalid Linkly ${field}.`); return value; }
function isNotFound(error: unknown): boolean { return error instanceof HbposApiError && (error.status === 404 || error.code === "LINKLY_CLOUD_BACKEND_SESSION_NOT_FOUND"); }
function sessionNotFound(): HbposApiError { return new HbposApiError("Linkly session was not found.", { kind: "http", status: 404, code: "LINKLY_CLOUD_BACKEND_SESSION_NOT_FOUND" }); }
function isCreateAmbiguous(error: unknown): boolean { return error instanceof HbposApiError && (error.kind === "transport" || error.status === 408 || (error.status !== undefined && error.status >= 500)); }
function isActiveSessionConflict(error: unknown): boolean { return error instanceof HbposApiError && error.status === 409; }
function linklyProviderAmountCents(attempt: PaymentAttempt): number {
  const amount = paymentProviderAmountCents(attempt.operation, attempt.amount);
  if (amount === null) throw new Error("LINKLY_AMOUNT_INVALID");
  return amount;
}
