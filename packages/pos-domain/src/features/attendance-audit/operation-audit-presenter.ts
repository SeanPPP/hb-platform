export const AUDIT_VIEW_PERMISSION =
  "Permissions.PosTerminal.Audit.View";

export type OperationAuditSource = "local" | "remote";
export type OperationAuditUploadState =
  | "pending"
  | "uploaded"
  | "rejected";

export type OperationAuditRawItem = Readonly<{
  actualAmountDeltaCents: number | null;
  displayName: string | null;
  lineIndex: number;
  productCode: string | null;
  quantityDelta: string | null;
}>;

export type OperationAuditRawRecord = Readonly<{
  cashierName: string | null;
  correlationId: string | null;
  deviceCode: string;
  eventId: string;
  items: readonly OperationAuditRawItem[];
  occurredAtIso: string;
  operationType: string;
  orderGuid: string | null;
  outcome: string;
  paymentAmountCents: number | null;
  primaryProduct: string | null;
  productCount: number;
  receiptNumber: string | null;
  safeMessage: string | null;
  storeCode: string;
  uploadState: OperationAuditUploadState;
}>;

export type OperationAuditItem = OperationAuditRawItem;
export type OperationAuditRecord = OperationAuditRawRecord;

export interface OperationAuditReadPort {
  list(input: Readonly<{
    deviceCode: string;
    keyword: string | null;
    limit: 100;
    source: OperationAuditSource;
    storeCode: string;
    uploadState: OperationAuditUploadState | null;
  }>): Promise<readonly OperationAuditRawRecord[]>;
  get(input: Readonly<{
    deviceCode: string;
    eventId: string;
    source: OperationAuditSource;
    storeCode: string;
  }>): Promise<OperationAuditRawRecord | null>;
}

export type OperationAuditPresenterState = Readonly<{
  access: Readonly<{ canView: boolean }>;
  detail: OperationAuditRecord | null;
  detailLoading: boolean;
  kind: "idle" | "loading" | "ready" | "unauthorized" | "failed";
  online: boolean;
  query: string;
  rows: readonly OperationAuditRecord[];
  selectedEventId: string | null;
  source: OperationAuditSource;
  statusCode:
    | "details-failed"
    | "details-unavailable"
    | "list-failed"
    | "online-required"
    | "permission-required"
    | null;
  uploadState: OperationAuditUploadState | null;
}>;

export class OperationAuditPresenter {
  private readonly listeners = new Set<() => void>();
  private readonly trustedStoreCode: string;
  private readonly trustedDeviceCode: string;
  private state: OperationAuditPresenterState;
  private loadGeneration = 0;
  private detailGeneration = 0;
  private destroyed = false;

  public constructor(
    private readonly options: Readonly<{
      initialOnline: boolean;
      permissions: readonly string[];
      read: OperationAuditReadPort;
      trustedDeviceCode: string;
      trustedStoreCode: string;
    }>,
  ) {
    this.trustedStoreCode = trustedCode(
      options.trustedStoreCode,
      "storeCode",
      50,
    );
    this.trustedDeviceCode = trustedCode(
      options.trustedDeviceCode,
      "deviceCode",
      128,
    );
    const permissions = new Set(
      options.permissions
        .filter((value): value is string => typeof value === "string")
        .map((value) => value.trim())
        .filter(Boolean),
    );
    this.state = Object.freeze({
      access: Object.freeze({
        canView: permissions.has(AUDIT_VIEW_PERMISSION),
      }),
      detail: null,
      detailLoading: false,
      kind: "idle",
      online: options.initialOnline,
      query: "",
      rows: Object.freeze([]),
      selectedEventId: null,
      source: "local",
      statusCode: null,
      uploadState: null,
    });
  }

  public readonly getState = (): OperationAuditPresenterState =>
    this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.loadGeneration += 1;
    this.detailGeneration += 1;
    this.listeners.clear();
  }

  public setOnline(online: boolean): void {
    if (this.destroyed || this.state.online === online) return;
    this.patch({ online });
  }

  public setQuery(value: string): void {
    if (this.destroyed) return;
    this.patch({
      query: value.slice(0, 120),
      statusCode: null,
    });
  }

  public setSource(source: OperationAuditSource): void {
    if (this.destroyed || this.state.source === source) return;
    this.detailGeneration += 1;
    this.patch({
      detail: null,
      detailLoading: false,
      selectedEventId: null,
      source,
      statusCode: null,
    });
  }

  public setUploadState(
    uploadState: OperationAuditUploadState | null,
  ): void {
    if (this.destroyed) return;
    this.patch({ statusCode: null, uploadState });
  }

  public async load(): Promise<void> {
    if (this.destroyed) return;
    if (!this.state.access.canView) {
      this.patch({
        detail: null,
        kind: "unauthorized",
        rows: Object.freeze([]),
        selectedEventId: null,
        statusCode: "permission-required",
      });
      return;
    }
    if (this.state.source === "remote" && !this.state.online) {
      this.patch({ statusCode: "online-required" });
      return;
    }
    const generation = ++this.loadGeneration;
    const source = this.state.source;
    this.patch({ kind: "loading", statusCode: null });
    try {
      const raw = await this.options.read.list({
        deviceCode: this.trustedDeviceCode,
        keyword: optionalQuery(this.state.query),
        limit: 100,
        source,
        storeCode: this.trustedStoreCode,
        uploadState: this.state.uploadState,
      });
      if (!this.isCurrentLoad(generation, source)) return;
      const rows = mapAuditPage(
        raw,
        this.trustedStoreCode,
        this.trustedDeviceCode,
      );
      const selectedStillPresent =
        this.state.selectedEventId !== null &&
        rows.some(
          (row) => row.eventId === this.state.selectedEventId,
        );
      this.patch({
        detail: selectedStillPresent ? this.state.detail : null,
        kind: "ready",
        rows,
        selectedEventId: selectedStillPresent
          ? this.state.selectedEventId
          : null,
      });
    } catch {
      if (!this.isCurrentLoad(generation, source)) return;
      this.patch({
        detail: null,
        kind: "failed",
        rows: Object.freeze([]),
        selectedEventId: null,
        statusCode: "list-failed",
      });
    }
  }

  public async select(eventId: string): Promise<void> {
    if (
      this.destroyed ||
      !this.state.access.canView ||
      !this.state.rows.some((row) => row.eventId === eventId)
    ) {
      return;
    }
    if (this.state.source === "remote" && !this.state.online) {
      this.patch({ statusCode: "online-required" });
      return;
    }
    const generation = ++this.detailGeneration;
    const source = this.state.source;
    this.patch({
      detail: null,
      detailLoading: true,
      selectedEventId: eventId,
      statusCode: null,
    });
    try {
      const raw = await this.options.read.get({
        deviceCode: this.trustedDeviceCode,
        eventId,
        source,
        storeCode: this.trustedStoreCode,
      });
      if (!this.isCurrentDetail(generation, source, eventId)) return;
      const detail = raw
        ? mapAuditRecord(
            raw,
            this.trustedStoreCode,
            this.trustedDeviceCode,
          )
        : null;
      if (detail && detail.eventId !== eventId) {
        throw new Error("Audit detail EventId mismatch.");
      }
      this.patch({
        detail,
        detailLoading: false,
        statusCode: detail ? null : "details-unavailable",
      });
    } catch {
      if (!this.isCurrentDetail(generation, source, eventId)) return;
      this.patch({
        detail: null,
        detailLoading: false,
        statusCode: "details-failed",
      });
    }
  }

  private isCurrentLoad(
    generation: number,
    source: OperationAuditSource,
  ): boolean {
    return (
      !this.destroyed &&
      generation === this.loadGeneration &&
      source === this.state.source
    );
  }

  private isCurrentDetail(
    generation: number,
    source: OperationAuditSource,
    eventId: string,
  ): boolean {
    return (
      !this.destroyed &&
      generation === this.detailGeneration &&
      source === this.state.source &&
      eventId === this.state.selectedEventId
    );
  }

  private patch(
    patch: Partial<OperationAuditPresenterState>,
  ): void {
    if (this.destroyed) return;
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of this.listeners) listener();
  }
}

function mapAuditPage(
  value: readonly OperationAuditRawRecord[],
  storeCode: string,
  deviceCode: string,
): readonly OperationAuditRecord[] {
  if (!Array.isArray(value) || value.length > 100) {
    throw new Error("Invalid audit page.");
  }
  const ids = new Set<string>();
  const rows = value.map((row) => {
    const mapped = mapAuditRecord(row, storeCode, deviceCode);
    if (ids.has(mapped.eventId)) {
      throw new Error("Duplicate audit EventId.");
    }
    ids.add(mapped.eventId);
    return mapped;
  });
  return Object.freeze(rows);
}

function mapAuditRecord(
  value: OperationAuditRawRecord,
  trustedStoreCode: string,
  trustedDeviceCode: string,
): OperationAuditRecord {
  if (
    value.storeCode !== trustedStoreCode ||
    value.deviceCode !== trustedDeviceCode
  ) {
    throw new Error("Audit scope mismatch.");
  }
  const eventId = requiredUuid(value.eventId, "eventId");
  const items = requiredItems(value.items).map((item) =>
    Object.freeze({
      actualAmountDeltaCents: optionalCents(
        item.actualAmountDeltaCents,
      ),
      displayName: optionalSafeText(item.displayName, 512),
      lineIndex: nonNegativeInteger(item.lineIndex, "lineIndex"),
      productCode: optionalSafeText(item.productCode, 128),
      quantityDelta: optionalDecimal(
        item.quantityDelta,
        "quantityDelta",
      ),
    }),
  );
  return Object.freeze({
    cashierName: optionalSafeText(value.cashierName, 256),
    correlationId: optionalSafeText(value.correlationId, 256),
    deviceCode: trustedDeviceCode,
    eventId,
    items: Object.freeze(items),
    occurredAtIso: requiredIso(value.occurredAtIso, "occurredAtIso"),
    operationType: requiredCode(
      value.operationType,
      "operationType",
      100,
    ),
    orderGuid:
      value.orderGuid === null
        ? null
        : requiredUuid(value.orderGuid, "orderGuid"),
    outcome: requiredCode(value.outcome, "outcome", 64),
    paymentAmountCents: optionalCents(value.paymentAmountCents),
    primaryProduct: optionalSafeText(value.primaryProduct, 512),
    productCount: nonNegativeInteger(
      value.productCount,
      "productCount",
    ),
    receiptNumber: optionalSafeText(value.receiptNumber, 128),
    safeMessage: optionalSafeText(value.safeMessage, 1_000),
    storeCode: trustedStoreCode,
    uploadState: requiredUploadState(value.uploadState),
  });
}

function requiredItems(
  value: readonly OperationAuditRawItem[],
): readonly OperationAuditRawItem[] {
  if (!Array.isArray(value) || value.length > 500) {
    throw new Error("Invalid audit items.");
  }
  return value;
}

function optionalSafeText(
  value: string | null,
  maxLength: number,
): string | null {
  if (value === null) return null;
  if (typeof value !== "string" || value.length > maxLength) {
    throw new Error("Invalid audit display text.");
  }
  const normalized = value
    .replace(/[\u0000-\u001f\u007f]/gu, " ")
    .trim();
  if (normalized.length === 0) return null;
  return redactAuditText(normalized);
}

export function redactAuditText(value: string): string {
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

function requiredUuid(value: string, field: string): string {
  if (
    typeof value !== "string" ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      value,
    )
  ) {
    throw new Error(`Invalid ${field}.`);
  }
  return value.toLowerCase();
}

function requiredIso(value: string, field: string): string {
  if (
    typeof value !== "string" ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/u.test(
      value,
    ) ||
    !Number.isSafeInteger(Date.parse(value))
  ) {
    throw new Error(`Invalid ${field}.`);
  }
  return value;
}

function requiredCode(
  value: string,
  field: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maxLength ||
    !/^[A-Za-z0-9_.-]+$/u.test(value)
  ) {
    throw new Error(`Invalid ${field}.`);
  }
  return value;
}

function optionalCents(value: number | null): number | null {
  if (value === null) return null;
  if (!Number.isSafeInteger(value)) {
    throw new Error("Invalid audit cents.");
  }
  return value;
}

function nonNegativeInteger(value: number, field: string): number {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error(`Invalid ${field}.`);
  }
  return value;
}

function optionalDecimal(
  value: string | null,
  field: string,
): string | null {
  if (value === null) return null;
  if (
    typeof value !== "string" ||
    !/^-?(?:0|[1-9]\d*)(?:\.\d{1,4})?$/u.test(value)
  ) {
    throw new Error(`Invalid ${field}.`);
  }
  return value;
}

function requiredUploadState(
  value: OperationAuditUploadState,
): OperationAuditUploadState {
  if (
    value !== "pending" &&
    value !== "uploaded" &&
    value !== "rejected"
  ) {
    throw new Error("Invalid uploadState.");
  }
  return value;
}

function optionalQuery(value: string): string | null {
  const normalized = value.trim();
  return normalized.length === 0 ? null : normalized;
}

function trustedCode(
  value: string,
  field: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.trim() !== value ||
    value.length === 0 ||
    value.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error(`Invalid trusted ${field}.`);
  }
  return value;
}
