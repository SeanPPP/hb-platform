import {
  redactAuditText,
  type OperationAuditRawItem,
  type OperationAuditRawRecord,
  type OperationAuditReadPort,
  type OperationAuditUploadState,
} from "@hb/pos-domain/features/attendance-audit/operation-audit-presenter";

import { HbposApiError, type HbposTransport } from "../../transport";
import type { components } from "../../openapi";

type GeneratedAuditList =
  components["schemas"]["OperationAuditReadListDto"];
type GeneratedAuditRecord =
  components["schemas"]["OperationAuditReadRecordDto"];

const REMOTE_AUDIT_LIMIT = 100;

/**
 * Hbpos.Api 远程操作审计读取适配器。
 *
 * 门店和终端只用于本地 scope 校验，绝不进入 query；服务端必须以认证 claims
 * 决定可见范围。响应再做一次白名单投影，避免未来 DTO 扩字段后意外暴露敏感材料。
 */
export class HbposOperationAuditReadApi
  implements OperationAuditReadPort
{
  private readonly trustedStoreCode: string;
  private readonly trustedDeviceCode: string;

  public constructor(
    private readonly transport: HbposTransport,
    trustedStoreCode: string,
    trustedDeviceCode: string,
  ) {
    this.trustedStoreCode = trustedScopeCode(
      trustedStoreCode,
      "storeCode",
      50,
    );
    this.trustedDeviceCode = trustedScopeCode(
      trustedDeviceCode,
      "deviceCode",
      128,
    );
  }

  public async list(
    input: Parameters<OperationAuditReadPort["list"]>[0],
  ): Promise<readonly OperationAuditRawRecord[]> {
    const query = this.validateListInput(input);
    const response =
      await this.transport.request<GeneratedAuditList>({
        method: "GET",
        url: "/api/v1/operation-audits",
        params: {
          keyword: query.keyword ?? undefined,
          limit: REMOTE_AUDIT_LIMIT,
        },
      });
    assertSuccessfulStatus(response.status);

    const body = requiredObject(
      response.data,
      "Remote audit list",
    );
    const values = requiredArray(
      body.items,
      "Remote audit list items",
      REMOTE_AUDIT_LIMIT,
    );
    const eventIds = new Set<string>();
    const rows = values.map((value) => {
      const mapped = mapRecord(
        value,
        this.trustedStoreCode,
        this.trustedDeviceCode,
      );
      if (eventIds.has(mapped.eventId)) {
        throw new TypeError("Remote audit list contains duplicate EventId.");
      }
      eventIds.add(mapped.eventId);
      return mapped;
    });

    if (
      input.uploadState !== null &&
      input.uploadState !== "uploaded"
    ) {
      return Object.freeze([]);
    }
    return Object.freeze(rows);
  }

  public async get(
    input: Parameters<OperationAuditReadPort["get"]>[0],
  ): Promise<OperationAuditRawRecord | null> {
    this.validateRemoteScope(input);
    const requestedEventId = requiredUuid(
      input.eventId,
      "Remote audit requested EventId",
    );
    try {
      const response =
        await this.transport.request<GeneratedAuditRecord>({
          acceptedStatuses: [404],
          method: "GET",
          url: `/api/v1/operation-audits/${encodeURIComponent(requestedEventId)}`,
        });
      if (response.status === 404) return null;
      assertSuccessfulStatus(response.status);

      const mapped = mapRecord(
        response.data,
        this.trustedStoreCode,
        this.trustedDeviceCode,
      );
      if (mapped.eventId !== requestedEventId) {
        throw new TypeError(
          "Remote audit detail EventId does not match request.",
        );
      }
      return mapped;
    } catch (error: unknown) {
      // 仅将受支持的“未找到”语义折叠为 null；401/403 必须继续由传输层
      // 的全局会话锁定逻辑处理，并将原异常交回调用方。
      if (error instanceof HbposApiError && error.status === 404) {
        return null;
      }
      throw error;
    }
  }

  private validateListInput(
    input: Parameters<OperationAuditReadPort["list"]>[0],
  ): Readonly<{ keyword: string | null }> {
    this.validateRemoteScope(input);
    if (input.limit !== REMOTE_AUDIT_LIMIT) {
      throw new TypeError("Remote audit limit must be 100.");
    }
    requiredUploadFilter(input.uploadState);
    return Object.freeze({
      keyword: optionalKeyword(input.keyword),
    });
  }

  private validateRemoteScope(
    input: Readonly<{
      deviceCode: string;
      source: string;
      storeCode: string;
    }>,
  ): void {
    if (input.source !== "remote") {
      throw new TypeError(
        "Hbpos operation audit adapter only supports remote reads.",
      );
    }
    if (
      input.storeCode !== this.trustedStoreCode ||
      input.deviceCode !== this.trustedDeviceCode
    ) {
      throw new TypeError(
        "Remote audit request is outside the trusted scope.",
      );
    }
  }
}

function mapRecord(
  value: unknown,
  trustedStoreCode: string,
  trustedDeviceCode: string,
): OperationAuditRawRecord {
  const record = requiredObject(value, "Remote audit record");
  const eventId = requiredUuid(record.eventId, "Remote audit EventId");
  const responseStoreCode = requiredScopeText(
    record.storeCode,
    "Remote audit storeCode",
    50,
  );
  const responseDeviceCode = requiredScopeText(
    record.deviceCode,
    "Remote audit deviceCode",
    128,
  );
  if (
    responseStoreCode !== trustedStoreCode ||
    responseDeviceCode !== trustedDeviceCode
  ) {
    throw new TypeError("Remote audit response scope mismatch.");
  }

  const rawItems = requiredArray(
    record.items,
    "Remote audit record items",
    500,
  );
  const lineIndexes = new Set<number>();
  const items = rawItems.map((itemValue) => {
    const item = mapItem(itemValue);
    if (lineIndexes.has(item.lineIndex)) {
      throw new TypeError(
        "Remote audit record contains duplicate lineIndex.",
      );
    }
    lineIndexes.add(item.lineIndex);
    return item;
  });

  return Object.freeze({
    cashierName: optionalSafeText(
      record.cashierName,
      "Remote audit cashierName",
      256,
    ),
    correlationId: optionalSafeText(
      record.correlationId,
      "Remote audit correlationId",
      256,
    ),
    deviceCode: trustedDeviceCode,
    eventId,
    items: Object.freeze(items),
    occurredAtIso: requiredUtcIso(
      record.occurredAtIso,
      "Remote audit occurredAtIso",
    ),
    operationType: requiredCode(
      record.operationType,
      "Remote audit operationType",
      100,
    ),
    orderGuid: optionalUuid(
      record.orderGuid,
      "Remote audit orderGuid",
    ),
    outcome: requiredCode(
      record.outcome,
      "Remote audit outcome",
      64,
    ),
    paymentAmountCents: optionalSafeInteger(
      record.paymentAmountCents,
      "Remote audit paymentAmountCents",
    ),
    primaryProduct: optionalSafeText(
      record.primaryProduct,
      "Remote audit primaryProduct",
      512,
    ),
    productCount: requiredNonNegativeInteger(
      record.productCount,
      "Remote audit productCount",
    ),
    receiptNumber: optionalSafeText(
      record.receiptNumber,
      "Remote audit receiptNumber",
      128,
    ),
    safeMessage: optionalSafeText(
      record.safeMessage,
      "Remote audit safeMessage",
      1_000,
    ),
    storeCode: trustedStoreCode,
    uploadState: requiredUploadedState(record.uploadState),
  });
}

function mapItem(value: unknown): OperationAuditRawItem {
  const item = requiredObject(value, "Remote audit item");
  return Object.freeze({
    actualAmountDeltaCents: optionalSafeInteger(
      item.actualAmountDeltaCents,
      "Remote audit item actualAmountDeltaCents",
    ),
    displayName: optionalSafeText(
      item.displayName,
      "Remote audit item displayName",
      512,
    ),
    lineIndex: requiredNonNegativeInteger(
      item.lineIndex,
      "Remote audit item lineIndex",
    ),
    productCode: optionalSafeText(
      item.productCode,
      "Remote audit item productCode",
      128,
    ),
    quantityDelta: optionalDecimal(
      item.quantityDelta,
      "Remote audit item quantityDelta",
    ),
  });
}

function requiredObject(
  value: unknown,
  field: string,
): Readonly<Record<string, unknown>> {
  if (
    typeof value !== "object" ||
    value === null ||
    Array.isArray(value)
  ) {
    throw new TypeError(`${field} must be an object.`);
  }
  return value as Readonly<Record<string, unknown>>;
}

function requiredArray(
  value: unknown,
  field: string,
  maximumLength: number,
): readonly unknown[] {
  if (!Array.isArray(value) || value.length > maximumLength) {
    throw new TypeError(`${field} must be a bounded array.`);
  }
  return value;
}

function trustedScopeCode(
  value: unknown,
  field: string,
  maximumLength: number,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`Invalid trusted ${field}.`);
  }
  return requiredScopeText(
    value.trim(),
    `Trusted ${field}`,
    maximumLength,
  );
}

function requiredScopeText(
  value: unknown,
  field: string,
  maximumLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maximumLength ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value;
}

function optionalKeyword(value: unknown): string | null {
  if (value === null) return null;
  if (typeof value !== "string") {
    throw new TypeError("Invalid remote audit keyword.");
  }
  const normalized = value.trim();
  if (
    normalized.length > 120 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError("Invalid remote audit keyword.");
  }
  return normalized.length === 0 ? null : normalized;
}

function requiredUploadFilter(
  value: unknown,
): asserts value is OperationAuditUploadState | null {
  if (
    value !== null &&
    value !== "pending" &&
    value !== "uploaded" &&
    value !== "rejected"
  ) {
    throw new TypeError("Invalid remote audit upload filter.");
  }
}

function requiredUuid(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      value,
    )
  ) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value.toLowerCase();
}

function optionalUuid(value: unknown, field: string): string | null {
  return value === null ? null : requiredUuid(value, field);
}

function requiredUtcIso(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/u.test(
      value,
    ) ||
    !Number.isSafeInteger(Date.parse(value))
  ) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value;
}

function requiredCode(
  value: unknown,
  field: string,
  maximumLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maximumLength ||
    !/^[A-Za-z0-9_.-]+$/u.test(value)
  ) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value;
}

function optionalSafeText(
  value: unknown,
  field: string,
  maximumLength: number,
): string | null {
  if (value === null) return null;
  if (typeof value !== "string" || value.length > maximumLength) {
    throw new TypeError(`Invalid ${field}.`);
  }
  const normalized = value
    .replace(/[\u0000-\u001f\u007f]/gu, " ")
    .trim();
  return normalized.length === 0
    ? null
    : redactAuditText(normalized);
}

function optionalSafeInteger(
  value: unknown,
  field: string,
): number | null {
  if (value === null) return null;
  if (!Number.isSafeInteger(value)) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value as number;
}

function requiredNonNegativeInteger(
  value: unknown,
  field: string,
): number {
  if (!Number.isSafeInteger(value) || (value as number) < 0) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value as number;
}

function optionalDecimal(
  value: unknown,
  field: string,
): string | null {
  if (value === null) return null;
  if (
    typeof value !== "string" ||
    !/^-?(?:0|[1-9]\d*)(?:\.\d{1,4})?$/u.test(value)
  ) {
    throw new TypeError(`Invalid ${field}.`);
  }
  return value;
}

function requiredUploadedState(value: unknown): "uploaded" {
  if (value !== "uploaded") {
    throw new TypeError(
      "Remote audit response uploadState must be uploaded.",
    );
  }
  return value;
}

function assertSuccessfulStatus(status: number): void {
  if (!Number.isSafeInteger(status) || status < 200 || status >= 300) {
    throw new HbposApiError("Remote operation audit HTTP failure.", {
      kind: "http",
      ...(Number.isSafeInteger(status) ? { status } : {}),
    });
  }
}
