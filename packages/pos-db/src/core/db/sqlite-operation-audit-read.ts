import type { SqliteConnectionPort } from "./types";

import type {
  OperationAuditRawItem,
  OperationAuditRawRecord,
  OperationAuditReadPort,
  OperationAuditUploadState,
} from "@hb/pos-domain/features/attendance-audit/operation-audit-presenter";

export type {
  OperationAuditRawItem,
  OperationAuditRawRecord,
  OperationAuditReadPort,
};

export const OPERATION_AUDIT_CANDIDATE_LIMIT = 500;
export const OPERATION_AUDIT_RESULT_LIMIT = 100;

export type OperationAuditLocalScope = Readonly<{
  storeCode: string;
  deviceCode: string;
}>;

type OperationAuditRow = Readonly<{
  event_id: unknown;
  event_type: unknown;
  occurred_at_iso: unknown;
  order_guid: unknown;
  external_order_guid: unknown;
  correlation_id: unknown;
  payload_json: unknown;
  uploaded_at_iso: unknown;
  order_store_code: unknown;
  order_device_code: unknown;
  order_cashier_name: unknown;
}>;

/**
 * 本机审计只读适配器。原始 payload_json 永不越过此边界；旧 shape 或损坏 JSON
 * 仅降级白名单展示字段，不能把未识别内容带进 presenter。
 */
export class SqliteOperationAuditRead implements OperationAuditReadPort {
  private readonly scope: OperationAuditLocalScope;

  public constructor(
    private readonly connection: SqliteConnectionPort,
    scope: OperationAuditLocalScope,
  ) {
    this.scope = normalizeScope(scope);
  }

  public async list(
    input: Parameters<OperationAuditReadPort["list"]>[0],
  ): Promise<readonly OperationAuditRawRecord[]> {
    const request = validateListRequest(input, this.scope);
    if (request.uploadState === "rejected") {
      return Object.freeze([]);
    }
    const uploadClause =
      request.uploadState === "pending"
        ? "AND audit.uploaded_at_iso IS NULL"
        : request.uploadState === "uploaded"
          ? "AND audit.uploaded_at_iso IS NOT NULL"
          : "";
    const rows = await this.connection.getAll<OperationAuditRow>(
      `${selectAuditRows()}
       WHERE (
         (audit.order_guid IS NULL AND audit.external_order_guid IS NULL)
         OR (
           audit.external_order_guid IS NOT NULL
           AND audit.scope_store_code = ?
           AND audit.scope_device_code = ?
         )
         OR (
           orders.order_guid IS NOT NULL
           AND orders.store_code = ?
           AND orders.device_code = ?
         )
       )
       ${uploadClause}
       ORDER BY audit.occurred_at_iso DESC, audit.event_id ASC
       LIMIT ?`,
      [
        this.scope.storeCode,
        this.scope.deviceCode,
        this.scope.storeCode,
        this.scope.deviceCode,
        OPERATION_AUDIT_CANDIDATE_LIMIT,
      ],
    );

    const records: OperationAuditRawRecord[] = [];
    for (const row of rows) {
      const record = mapAuditRow(row, this.scope);
      if (
        record !== null &&
        (request.keyword === null ||
          matchesKeyword(record, request.keyword))
      ) {
        records.push(record);
        if (records.length === OPERATION_AUDIT_RESULT_LIMIT) break;
      }
    }
    return Object.freeze(records);
  }

  public async get(
    input: Parameters<OperationAuditReadPort["get"]>[0],
  ): Promise<OperationAuditRawRecord | null> {
    validateCommonRequest(input, this.scope);
    const eventId = strictUuid(input.eventId);
    if (eventId === null) {
      throw new TypeError("Operation audit event ID is invalid.");
    }
    const row = await this.connection.getFirst<OperationAuditRow>(
      `${selectAuditRows()}
       WHERE audit.event_id = ?
         AND (
           (audit.order_guid IS NULL AND audit.external_order_guid IS NULL)
           OR (
             audit.external_order_guid IS NOT NULL
             AND audit.scope_store_code = ?
             AND audit.scope_device_code = ?
           )
           OR (
             orders.order_guid IS NOT NULL
             AND orders.store_code = ?
             AND orders.device_code = ?
           )
         )
       LIMIT 1`,
      [
        eventId,
        this.scope.storeCode,
        this.scope.deviceCode,
        this.scope.storeCode,
        this.scope.deviceCode,
      ],
    );
    return row === null ? null : mapAuditRow(row, this.scope);
  }
}

function selectAuditRows(): string {
  return `SELECT
    audit.event_id,
    audit.event_type,
    audit.occurred_at_iso,
    audit.order_guid,
    audit.external_order_guid,
    audit.correlation_id,
    audit.payload_json,
    audit.uploaded_at_iso,
    orders.store_code AS order_store_code,
    orders.device_code AS order_device_code,
    orders.cashier_name AS order_cashier_name
  FROM audit_events AS audit
  LEFT JOIN local_orders AS orders
    ON orders.order_guid = audit.order_guid`;
}

function validateListRequest(
  input: Parameters<OperationAuditReadPort["list"]>[0],
  expectedScope: OperationAuditLocalScope,
): Readonly<{
  keyword: string | null;
  uploadState: OperationAuditUploadState | null;
}> {
  validateCommonRequest(input, expectedScope);
  if (input.limit !== OPERATION_AUDIT_RESULT_LIMIT) {
    throw new TypeError("Operation audit list limit must be 100.");
  }
  const keyword = normalizeKeyword(input.keyword);
  const uploadState = normalizeUploadState(input.uploadState);
  return Object.freeze({ keyword, uploadState });
}

function validateCommonRequest(
  input: Readonly<{
    source: unknown;
    storeCode: unknown;
    deviceCode: unknown;
  }>,
  expectedScope: OperationAuditLocalScope,
): void {
  if (input.source !== "local") {
    throw new TypeError("Operation audit source must be local.");
  }
  if (
    input.storeCode !== expectedScope.storeCode ||
    input.deviceCode !== expectedScope.deviceCode
  ) {
    throw new Error("Operation audit request scope is invalid.");
  }
}

function normalizeScope(value: OperationAuditLocalScope): OperationAuditLocalScope {
  if (
    typeof value !== "object" ||
    value === null ||
    Array.isArray(value)
  ) {
    throw new TypeError("Operation audit local scope is invalid.");
  }
  return Object.freeze({
    storeCode: strictScopeText(value.storeCode, "store", 50),
    deviceCode: strictScopeText(value.deviceCode, "device", 128),
  });
}

function strictScopeText(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maxLength ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`Operation audit ${label} scope is invalid.`);
  }
  return value;
}

function normalizeKeyword(value: unknown): string | null {
  if (value === null) return null;
  if (typeof value !== "string") {
    throw new TypeError("Operation audit keyword is invalid.");
  }
  const keyword = value.trim();
  if (
    keyword.length > 256 ||
    /[\u0000-\u001f\u007f]/u.test(keyword)
  ) {
    throw new TypeError("Operation audit keyword is invalid.");
  }
  return keyword.length === 0 ? null : keyword.toLocaleLowerCase();
}

function normalizeUploadState(
  value: unknown,
): OperationAuditUploadState | null {
  if (
    value !== null &&
    value !== "pending" &&
    value !== "uploaded" &&
    value !== "rejected"
  ) {
    throw new TypeError("Operation audit upload state is invalid.");
  }
  return value;
}

function mapAuditRow(
  row: OperationAuditRow,
  scope: OperationAuditLocalScope,
): OperationAuditRawRecord | null {
  const eventId = strictUuid(row.event_id);
  const occurredAtIso = safeIso(row.occurred_at_iso);
  const operationType = safeCode(row.event_type, 100);
  if (
    eventId === null ||
    occurredAtIso === null ||
    operationType === null
  ) {
    return null;
  }

  const orderLinked = row.order_guid !== null;
  const externalOrderLinked = row.external_order_guid !== null;
  const orderGuid = orderLinked
    ? strictUuid(row.order_guid)
    : externalOrderLinked
      ? strictUuid(row.external_order_guid)
      : null;
  if (
    (orderLinked || externalOrderLinked) &&
    (orderGuid === null ||
      (orderLinked &&
        (row.order_store_code !== scope.storeCode ||
          row.order_device_code !== scope.deviceCode)))
  ) {
    return null;
  }
  const uploadState = mapUploadState(row.uploaded_at_iso);
  if (uploadState === null) return null;

  const payload = safePayload(row.payload_json);
  const items = mapPayloadItems(payload.items);
  const primaryProduct =
    safePayloadText(payload.primaryProduct, 512) ??
    items[0]?.displayName ??
    items[0]?.productCode ??
    null;
  const payloadProductCount = safeNonnegativeInteger(
    payload.productCount,
  );
  const cashierName = orderLinked
    ? safeDisplayText(row.order_cashier_name, 256)
    : safePayloadText(payload.cashierName, 256);

  return Object.freeze({
    cashierName,
    correlationId: safeDisplayText(row.correlation_id, 256),
    deviceCode: scope.deviceCode,
    eventId,
    items,
    occurredAtIso,
    operationType,
    orderGuid,
    outcome:
      safeCode(payload.outcome, 64) ??
      safeCode(payload.status, 64) ??
      "Unknown",
    paymentAmountCents: safeInteger(payload.paymentAmountCents),
    primaryProduct,
    productCount: payloadProductCount ?? items.length,
    receiptNumber: safePayloadText(payload.receiptNumber, 128),
    safeMessage:
      safePayloadText(payload.safeMessage, 1_000) ??
      safePayloadText(payload.reason, 1_000),
    storeCode: scope.storeCode,
    uploadState,
  });
}

function safePayload(value: unknown): Record<string, unknown> {
  if (typeof value !== "string") return Object.create(null);
  try {
    const parsed = JSON.parse(value) as unknown;
    return isRecord(parsed) ? parsed : Object.create(null);
  } catch {
    return Object.create(null);
  }
}

function mapPayloadItems(value: unknown): readonly OperationAuditRawItem[] {
  if (!Array.isArray(value) || value.length > 500) {
    return Object.freeze([]);
  }
  const items: OperationAuditRawItem[] = [];
  for (const candidate of value) {
    if (!isRecord(candidate)) continue;
    const lineIndex = safeNonnegativeInteger(candidate.lineIndex);
    if (lineIndex === null) continue;
    items.push(
      Object.freeze({
        actualAmountDeltaCents: safeInteger(
          candidate.actualAmountDeltaCents,
        ),
        displayName: safePayloadText(candidate.displayName, 512),
        lineIndex,
        productCode: safePayloadText(candidate.productCode, 128),
        quantityDelta: safeDecimal(candidate.quantityDelta),
      }),
    );
  }
  return Object.freeze(items);
}

function safePayloadText(
  value: unknown,
  maxLength: number,
): string | null {
  return safeDisplayText(value, maxLength);
}

function safeDisplayText(
  value: unknown,
  maxLength: number,
): string | null {
  if (typeof value !== "string" || value.length > maxLength) {
    return null;
  }
  const normalized = value
    .replace(/[\u0000-\u001f\u007f]/gu, " ")
    .trim();
  return normalized.length === 0 ? null : redactAuditText(normalized);
}

function redactAuditText(value: string): string {
  return value
    .replace(
      /\bBearer\s+[^\s,;"']+/giu,
      "Bearer [REDACTED_TOKEN]",
    )
    .replace(
      /\bHBPOSE[12]-[A-Za-z0-9._~+/=-]+/gu,
      "[REDACTED_EMERGENCY_TOKEN]",
    )
    .replace(
      /\bHBATE1(?:\.[A-Za-z0-9_-]+){1,4}/gu,
      "[REDACTED_ATTENDANCE_TOKEN]",
    )
    .replace(
      /\b(authorization(?:Token|Code)?|reservationToken|keyMaterial|authCode|cardNumber|voucherCode)\s*[:=]\s*["']?[^,\s;"']+/giu,
      "$1=[REDACTED_SECRET]",
    )
    .replace(
      /(?<!\d)(?:\d[ -]?){12,19}(?!\d)/gu,
      "[REDACTED_CARD]",
    )
    .replace(
      /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/giu,
      "[REDACTED_CONTACT]",
    )
    .replace(
      /(?<!\d)(?:\+?61[ -]?4|04)(?:[ -]?\d){8}(?!\d)/gu,
      "[REDACTED_CONTACT]",
    );
}

function strictUuid(value: unknown): string | null {
  if (
    typeof value !== "string" ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      value,
    )
  ) {
    return null;
  }
  return value.toLowerCase();
}

function safeIso(value: unknown): string | null {
  if (
    typeof value !== "string" ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/u.test(
      value,
    ) ||
    !Number.isSafeInteger(Date.parse(value))
  ) {
    return null;
  }
  return value;
}

function safeCode(value: unknown, maxLength: number): string | null {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maxLength ||
    !/^[A-Za-z0-9_.-]+$/u.test(value)
  ) {
    return null;
  }
  return value;
}

function safeInteger(value: unknown): number | null {
  return typeof value === "number" && Number.isSafeInteger(value)
    ? value
    : null;
}

function safeNonnegativeInteger(value: unknown): number | null {
  const integer = safeInteger(value);
  return integer !== null && integer >= 0 ? integer : null;
}

function safeDecimal(value: unknown): string | null {
  return typeof value === "string" &&
    /^-?(?:0|[1-9]\d*)(?:\.\d{1,4})?$/u.test(value)
    ? value
    : null;
}

function mapUploadState(
  uploadedAtIso: unknown,
): OperationAuditUploadState | null {
  if (uploadedAtIso === null) return "pending";
  return safeIso(uploadedAtIso) === null ? null : "uploaded";
}

function matchesKeyword(
  record: OperationAuditRawRecord,
  keyword: string,
): boolean {
  const values: (string | number | null)[] = [
    record.cashierName,
    record.correlationId,
    record.eventId,
    record.occurredAtIso,
    record.operationType,
    record.orderGuid,
    record.outcome,
    record.paymentAmountCents,
    record.primaryProduct,
    record.productCount,
    record.receiptNumber,
    record.safeMessage,
    record.uploadState,
  ];
  for (const item of record.items) {
    values.push(
      item.actualAmountDeltaCents,
      item.displayName,
      item.lineIndex,
      item.productCode,
      item.quantityDelta,
    );
  }
  return values.some(
    (value) =>
      value !== null &&
      String(value).toLocaleLowerCase().includes(keyword),
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return (
    typeof value === "object" &&
    value !== null &&
    !Array.isArray(value)
  );
}
