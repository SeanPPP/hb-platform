import {
  normalizeLocalHistoryQuery,
  type LocalHistoryDetails,
  type LocalHistoryFilters,
  type LocalHistoryPage,
  type LocalHistoryPort,
  type LocalHistoryReceiptPreviewPort,
  type LocalHistoryReprintPort,
  type LocalHistorySummary,
} from "./local-history-domain";

import type { EscPosDocument } from "@hb/pos-receipt-core/features/receipts/receipt-document";
import {
  businessDayUtcRange,
  resolveBusinessTimeZone,
} from "@hb/pos-sync/features/sync-history/business-day-range";

export const LOCAL_HISTORY_VIEW_PERMISSION =
  "Permissions.PosTerminal.History.View";
export const LOCAL_HISTORY_REPRINT_PERMISSION =
  "Permissions.PosTerminal.History.Reprint";

export type LocalHistoryDetailsState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{ kind: "loading"; orderGuid: string }>
  | Readonly<{
      kind: "ready";
      orderGuid: string;
      value: LocalHistoryDetails;
    }>
  | Readonly<{ kind: "not-found"; orderGuid: string }>
  | Readonly<{
      kind: "failed";
      orderGuid: string;
      errorCode: "local-history-details-failed";
    }>;

export type LocalHistoryReprintState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{ kind: "unavailable" }>
  | Readonly<{ kind: "submitting"; orderGuid: string }>
  | Readonly<{ kind: "succeeded"; orderGuid: string }>
  | Readonly<{
      kind: "failed";
      orderGuid: string;
      errorCode: "local-history-reprint-failed";
    }>;

export type LocalHistoryReceiptPreviewState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{ kind: "loading"; orderGuid: string }>
  | Readonly<{
      kind: "ready";
      orderGuid: string;
      document: EscPosDocument;
    }>
  | Readonly<{ kind: "not-found"; orderGuid: string }>
  | Readonly<{
      kind: "failed";
      orderGuid: string;
      errorCode: "local-history-receipt-preview-failed";
    }>;

export type LocalHistoryPresenterState = Readonly<{
  kind:
    | "idle"
    | "loading"
    | "ready"
    | "empty"
    | "failed"
    | "unauthorized";
  filters: LocalHistoryFilters;
  businessTimeZone: string;
  rows: readonly LocalHistorySummary[];
  selectedOrderGuid: string | null;
  details: LocalHistoryDetailsState;
  receiptPreview: LocalHistoryReceiptPreviewState;
  reprint: LocalHistoryReprintState;
  loadingMore: boolean;
  hasMore: boolean;
  nextCursor: number | null;
  errorCode: "local-history-load-failed" | null;
}>;

export type LocalHistoryPresenterOptions = Readonly<{
  port: LocalHistoryPort;
  receiptPreviewPort?: LocalHistoryReceiptPreviewPort | null;
  reprintPort?: LocalHistoryReprintPort | null;
  permissionCodes: readonly string[];
  businessTimeZone?: string;
  now?: () => Date;
  pageSize?: number;
}>;

export class LocalHistoryPresenter {
  public state: LocalHistoryPresenterState;

  private readonly listeners = new Set<() => void>();
  private readonly pageSize: number;
  private readonly businessTimeZone: string;
  private readonly canView: boolean;
  private readonly canReprint: boolean;
  private listGeneration = 0;
  private detailsGeneration = 0;
  private receiptPreviewGeneration = 0;
  private reprintGeneration = 0;
  private listInFlight: Readonly<{
    kind: "refresh" | "more";
    generation: number;
    promise: Promise<void>;
  }> | null = null;
  private detailsInFlight: Readonly<{
    orderGuid: string;
    promise: Promise<void>;
  }> | null = null;
  private receiptPreviewInFlight: Readonly<{
    orderGuid: string;
    promise: Promise<void>;
  }> | null = null;
  private reprintInFlight: Readonly<{
    orderGuid: string;
    promise: Promise<void>;
  }> | null = null;
  private destroyed = false;

  public constructor(private readonly options: LocalHistoryPresenterOptions) {
    this.pageSize = normalizePageSize(options.pageSize ?? 50);
    const timeZone = resolveBusinessTimeZone(options.businessTimeZone);
    if (!timeZone) {
      throw new TypeError("Local history business time zone is invalid.");
    }
    this.businessTimeZone = timeZone;
    this.canView = hasLocalHistoryViewPermission(options.permissionCodes);
    this.canReprint =
      this.canView &&
      hasLocalHistoryReprintPermission(options.permissionCodes);
    const now = (options.now ?? (() => new Date()))();
    const businessDate = dateInTimeZone(now, this.businessTimeZone);
    const filters = localHistoryBusinessDayRange(
      businessDate,
      businessDate,
      this.businessTimeZone,
    );
    if (!filters) {
      throw new TypeError("Local history current business day is invalid.");
    }
    this.state = {
      kind: this.canView ? "idle" : "unauthorized",
      filters,
      businessTimeZone: this.businessTimeZone,
      rows: [],
      selectedOrderGuid: null,
      details: { kind: "idle" },
      receiptPreview: { kind: "idle" },
      reprint: { kind: "idle" },
      loadingMore: false,
      hasMore: false,
      nextCursor: null,
      errorCode: null,
    };
  }

  public get capabilities(): Readonly<{
    refund: false;
    recall: false;
    reprint: boolean;
  }> {
    return Object.freeze({
      refund: false,
      recall: false,
      reprint: this.reprintableDetails() !== null,
    });
  }

  public readonly getState = (): LocalHistoryPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    let subscribed = true;
    return () => {
      if (!subscribed) return;
      subscribed = false;
      this.listeners.delete(listener);
    };
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.listGeneration += 1;
    this.detailsGeneration += 1;
    this.receiptPreviewGeneration += 1;
    this.reprintGeneration += 1;
    this.listInFlight = null;
    this.detailsInFlight = null;
    this.receiptPreviewInFlight = null;
    this.reprintInFlight = null;
    this.listeners.clear();
  }

  public setFilters(filters: LocalHistoryFilters): void {
    if (this.destroyed) return;
    const normalized = normalizeLocalHistoryQuery({
      ...filters,
      cursor: null,
      limit: this.pageSize,
    });
    this.listGeneration += 1;
    this.detailsGeneration += 1;
    this.receiptPreviewGeneration += 1;
    this.reprintGeneration += 1;
    this.publish({
      ...this.state,
      kind: this.canView ? "idle" : "unauthorized",
      filters: {
        soldFromIso: normalized.soldFromIso,
        soldToIso: normalized.soldToIso,
        keyword: normalized.keyword,
      },
      rows: [],
      selectedOrderGuid: null,
      details: { kind: "idle" },
      receiptPreview: { kind: "idle" },
      reprint: { kind: "idle" },
      loadingMore: false,
      hasMore: false,
      nextCursor: null,
      errorCode: null,
    });
  }

  public refresh(): Promise<void> {
    if (this.destroyed || !this.canView) return Promise.resolve();
    if (
      this.listInFlight?.kind === "refresh" &&
      this.listInFlight.generation === this.listGeneration
    ) {
      return this.listInFlight.promise;
    }
    const generation = ++this.listGeneration;
    this.detailsGeneration += 1;
    this.receiptPreviewGeneration += 1;
    this.reprintGeneration += 1;
    this.publish({
      ...this.state,
      kind: "loading",
      rows: [],
      selectedOrderGuid: null,
      details: { kind: "idle" },
      receiptPreview: { kind: "idle" },
      reprint: { kind: "idle" },
      loadingMore: false,
      hasMore: false,
      nextCursor: null,
      errorCode: null,
    });
    const promise = this.loadFirstPage(generation).finally(() => {
      if (this.listInFlight?.promise === promise) {
        this.listInFlight = null;
      }
    });
    this.listInFlight = { kind: "refresh", generation, promise };
    return promise;
  }

  public loadMore(): Promise<void> {
    if (
      this.destroyed ||
      !this.canView ||
      this.state.nextCursor === null ||
      this.state.loadingMore
    ) {
      return this.listInFlight?.kind === "more"
        ? this.listInFlight.promise
        : Promise.resolve();
    }
    const cursor = this.state.nextCursor;
    const generation = ++this.listGeneration;
    this.publish({
      ...this.state,
      loadingMore: true,
      errorCode: null,
    });
    const promise = this.loadNextPage(generation, cursor).finally(() => {
      if (this.listInFlight?.promise === promise) {
        this.listInFlight = null;
      }
    });
    this.listInFlight = { kind: "more", generation, promise };
    return promise;
  }

  public selectOrder(orderGuid: string): Promise<void> {
    if (
      this.destroyed ||
      !this.canView ||
      !this.state.rows.some((row) => row.orderGuid === orderGuid)
    ) {
      return Promise.resolve();
    }
    if (
      this.state.selectedOrderGuid === orderGuid &&
      this.state.details.kind === "ready" &&
      (!this.options.receiptPreviewPort ||
        this.state.receiptPreview.kind === "ready")
    ) {
      return Promise.resolve();
    }
    if (
      this.state.selectedOrderGuid === orderGuid &&
      (this.detailsInFlight?.orderGuid === orderGuid ||
        this.receiptPreviewInFlight?.orderGuid === orderGuid)
    ) {
      return Promise.all([
        this.detailsInFlight?.promise ?? Promise.resolve(),
        this.receiptPreviewInFlight?.promise ?? Promise.resolve(),
      ]).then(() => undefined);
    }
    this.detailsGeneration += 1;
    this.receiptPreviewGeneration += 1;
    this.reprintGeneration += 1;
    this.publish({
      ...this.state,
      selectedOrderGuid: orderGuid,
      details: { kind: "idle" },
      receiptPreview: { kind: "idle" },
      reprint: { kind: "idle" },
    });
    return this.loadSelectedOrder(orderGuid);
  }

  public reprintSelected(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.reprintInFlight) return this.reprintInFlight.promise;
    const details = this.reprintableDetails();
    const reprintPort = this.options.reprintPort;
    if (!details || !reprintPort) {
      this.publish({ ...this.state, reprint: { kind: "unavailable" } });
      return Promise.resolve();
    }
    const generation = ++this.reprintGeneration;
    const orderGuid = details.orderGuid;
    this.publish({
      ...this.state,
      reprint: { kind: "submitting", orderGuid },
    });
    const promise = reprintPort.reprintExistingOrder(orderGuid)
      .then(() => {
        if (!this.isCurrentReprint(generation, orderGuid)) return;
        // 中文注释：重打只产生外设副作用，不回写或刷新本地订单账本。
        this.publish({
          ...this.state,
          reprint: { kind: "succeeded", orderGuid },
        });
      })
      .catch(() => {
        if (!this.isCurrentReprint(generation, orderGuid)) return;
        this.publish({
          ...this.state,
          reprint: {
            kind: "failed",
            orderGuid,
            errorCode: "local-history-reprint-failed",
          },
        });
      })
      .finally(() => {
        if (this.reprintInFlight?.promise === promise) {
          this.reprintInFlight = null;
        }
      });
    this.reprintInFlight = { orderGuid, promise };
    return promise;
  }

  private async loadFirstPage(generation: number): Promise<void> {
    try {
      const query = normalizeLocalHistoryQuery({
        ...this.state.filters,
        cursor: null,
        limit: this.pageSize,
      });
      const page = await this.options.port.list(query);
      if (!this.isCurrentList(generation)) return;
      validateLocalHistoryPage(page, null, this.pageSize);
      const rows = Object.freeze([...page.orders]);
      const selectedOrderGuid = rows[0]?.orderGuid ?? null;
      this.publish({
        ...this.state,
        kind: rows.length ? "ready" : "empty",
        rows,
        selectedOrderGuid,
        details: { kind: "idle" },
        receiptPreview: { kind: "idle" },
        reprint: { kind: "idle" },
        loadingMore: false,
        hasMore: page.nextCursor !== null,
        nextCursor: page.nextCursor,
        errorCode: null,
      });
      if (selectedOrderGuid !== null) {
        await this.loadSelectedOrder(selectedOrderGuid);
      }
    } catch {
      if (!this.isCurrentList(generation)) return;
      this.publish({
        ...this.state,
        kind: "failed",
        rows: [],
        selectedOrderGuid: null,
        details: { kind: "idle" },
        receiptPreview: { kind: "idle" },
        reprint: { kind: "idle" },
        loadingMore: false,
        hasMore: false,
        nextCursor: null,
        errorCode: "local-history-load-failed",
      });
    }
  }

  private async loadNextPage(
    generation: number,
    cursor: number,
  ): Promise<void> {
    try {
      const query = normalizeLocalHistoryQuery({
        ...this.state.filters,
        cursor,
        limit: this.pageSize,
      });
      const page = await this.options.port.list(query);
      if (!this.isCurrentList(generation)) return;
      validateLocalHistoryPage(page, cursor, this.pageSize);
      const rows = mergeStablePages(this.state.rows, page.orders);
      this.publish({
        ...this.state,
        kind: rows.length ? "ready" : "empty",
        rows,
        loadingMore: false,
        hasMore: page.nextCursor !== null,
        nextCursor: page.nextCursor,
        errorCode: null,
      });
    } catch {
      if (!this.isCurrentList(generation)) return;
      this.publish({
        ...this.state,
        loadingMore: false,
        errorCode: "local-history-load-failed",
      });
    }
  }

  private loadDetails(orderGuid: string): Promise<void> {
    if (
      this.destroyed ||
      !this.canView ||
      this.state.selectedOrderGuid !== orderGuid
    ) {
      return Promise.resolve();
    }
    const row = this.state.rows.find(
      (candidate) => candidate.orderGuid === orderGuid,
    );
    if (!row) return Promise.resolve();
    const generation = ++this.detailsGeneration;
    this.publish({
      ...this.state,
      details: { kind: "loading", orderGuid },
    });
    const promise = this.options.port.getDetails(orderGuid)
      .then((details) => {
        if (!this.isCurrentDetails(generation, orderGuid)) return;
        if (details === null) {
          this.publish({
            ...this.state,
            details: { kind: "not-found", orderGuid },
          });
          return;
        }
        validateDetails(details, row);
        this.publish({
          ...this.state,
          details: { kind: "ready", orderGuid, value: details },
        });
      })
      .catch(() => {
        if (!this.isCurrentDetails(generation, orderGuid)) return;
        this.publish({
          ...this.state,
          details: {
            kind: "failed",
            orderGuid,
            errorCode: "local-history-details-failed",
          },
        });
      })
      .finally(() => {
        if (this.detailsInFlight?.promise === promise) {
          this.detailsInFlight = null;
        }
      });
    this.detailsInFlight = { orderGuid, promise };
    return promise;
  }

  private loadSelectedOrder(orderGuid: string): Promise<void> {
    return Promise.all([
      this.loadDetails(orderGuid),
      this.loadReceiptPreview(orderGuid),
    ]).then(() => undefined);
  }

  private loadReceiptPreview(orderGuid: string): Promise<void> {
    const port = this.options.receiptPreviewPort;
    if (
      this.destroyed ||
      !this.canView ||
      !port ||
      this.state.selectedOrderGuid !== orderGuid
    ) {
      return Promise.resolve();
    }
    const generation = ++this.receiptPreviewGeneration;
    this.publish({
      ...this.state,
      receiptPreview: { kind: "loading", orderGuid },
    });
    const promise = port.getPreview(orderGuid)
      .then((document) => {
        if (!this.isCurrentReceiptPreview(generation, orderGuid)) return;
        this.publish({
          ...this.state,
          receiptPreview: document
            ? { kind: "ready", orderGuid, document }
            : { kind: "not-found", orderGuid },
        });
      })
      .catch(() => {
        if (!this.isCurrentReceiptPreview(generation, orderGuid)) return;
        this.publish({
          ...this.state,
          receiptPreview: {
            kind: "failed",
            orderGuid,
            errorCode: "local-history-receipt-preview-failed",
          },
        });
      })
      .finally(() => {
        if (this.receiptPreviewInFlight?.promise === promise) {
          this.receiptPreviewInFlight = null;
        }
      });
    this.receiptPreviewInFlight = { orderGuid, promise };
    return promise;
  }

  private isCurrentList(generation: number): boolean {
    return !this.destroyed && generation === this.listGeneration;
  }

  private isCurrentDetails(
    generation: number,
    orderGuid: string,
  ): boolean {
    return (
      !this.destroyed &&
      generation === this.detailsGeneration &&
      this.state.selectedOrderGuid === orderGuid
    );
  }

  private isCurrentReprint(
    generation: number,
    orderGuid: string,
  ): boolean {
    return (
      !this.destroyed &&
      generation === this.reprintGeneration &&
      this.state.selectedOrderGuid === orderGuid
    );
  }

  private isCurrentReceiptPreview(
    generation: number,
    orderGuid: string,
  ): boolean {
    return (
      !this.destroyed &&
      generation === this.receiptPreviewGeneration &&
      this.state.selectedOrderGuid === orderGuid
    );
  }

  private reprintableDetails(): LocalHistoryDetails | null {
    const details = this.state.details;
    if (
      !this.canView ||
      !this.canReprint ||
      !this.options.reprintPort ||
      this.state.kind !== "ready" ||
      this.state.reprint.kind === "submitting" ||
      details.kind !== "ready" ||
      details.orderGuid !== this.state.selectedOrderGuid ||
      details.value.orderGuid !== this.state.selectedOrderGuid
    ) {
      return null;
    }
    return details.value;
  }

  private publish(state: LocalHistoryPresenterState): void {
    if (this.destroyed) return;
    this.state = state;
    for (const listener of [...this.listeners]) {
      try {
        listener();
      } catch {
        // 一个已卸载视图不能阻止其他订阅者接收只读历史状态。
      }
    }
  }
}

export function hasLocalHistoryViewPermission(
  permissionCodes: readonly string[],
): boolean {
  return permissionSet(permissionCodes).has(LOCAL_HISTORY_VIEW_PERMISSION);
}

export function hasLocalHistoryReprintPermission(
  permissionCodes: readonly string[],
): boolean {
  return permissionSet(permissionCodes).has(
    LOCAL_HISTORY_REPRINT_PERMISSION,
  );
}

export function localHistoryBusinessDayRange(
  dateFrom: string,
  dateTo: string,
  businessTimeZone: string,
): LocalHistoryFilters | null {
  const range = businessDayUtcRange(
    dateFrom,
    dateTo,
    businessTimeZone,
  );
  return range?.dateFromIso && range.dateToIso
    ? Object.freeze({
        soldFromIso: range.dateFromIso,
        soldToIso: range.dateToIso,
        keyword: null,
      })
    : null;
}

export function validateLocalHistoryPage(
  page: LocalHistoryPage,
  queryCursor: number | null,
  limit = 50,
): void {
  if (!Array.isArray(page.orders) || page.orders.length > limit) {
    throw new Error("Invalid local history page size.");
  }
  const orderGuids = new Set<string>();
  const sequences = new Set<number>();
  let previous = Number.POSITIVE_INFINITY;
  for (const order of page.orders) {
    validateSummary(order);
    if (
      orderGuids.has(order.orderGuid) ||
      sequences.has(order.localSequence) ||
      order.localSequence >= previous ||
      (queryCursor !== null && order.localSequence >= queryCursor)
    ) {
      throw new Error("Invalid local history page ordering.");
    }
    orderGuids.add(order.orderGuid);
    sequences.add(order.localSequence);
    previous = order.localSequence;
  }
  if (
    page.nextCursor !== null &&
    (!Number.isSafeInteger(page.nextCursor) ||
      page.nextCursor <= 0 ||
      page.nextCursor !== page.orders.at(-1)?.localSequence ||
      (queryCursor !== null && page.nextCursor >= queryCursor))
  ) {
    throw new Error("Invalid local history next cursor.");
  }
}

function normalizePageSize(value: number): number {
  if (!Number.isSafeInteger(value) || value < 1 || value > 50) {
    throw new TypeError("Local history page size must be between 1 and 50.");
  }
  return value;
}

function permissionSet(permissionCodes: readonly string[]): ReadonlySet<string> {
  return new Set(
    permissionCodes
      .filter((permission): permission is string =>
        typeof permission === "string",
      )
      .map((permission) => permission.trim()),
  );
}

function dateInTimeZone(now: Date, timeZone: string): string {
  if (!Number.isFinite(now.getTime())) {
    throw new TypeError("Local history clock is invalid.");
  }
  const parts = new Map(
    new Intl.DateTimeFormat("en-CA", {
      calendar: "gregory",
      day: "2-digit",
      month: "2-digit",
      numberingSystem: "latn",
      timeZone,
      year: "numeric",
    })
      .formatToParts(now)
      .filter((part) =>
        part.type === "year" ||
        part.type === "month" ||
        part.type === "day",
      )
      .map((part) => [part.type, part.value]),
  );
  const value =
    `${parts.get("year") ?? ""}-` +
    `${parts.get("month") ?? ""}-` +
    `${parts.get("day") ?? ""}`;
  if (!/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
    throw new TypeError("Local history business date is invalid.");
  }
  return value;
}

function validateSummary(summary: LocalHistorySummary): void {
  if (
    !summary.orderGuid.trim() ||
    !Number.isSafeInteger(summary.localSequence) ||
    summary.localSequence <= 0 ||
    !Number.isFinite(Date.parse(summary.soldAtIso)) ||
    !summary.cashierName.trim() ||
    !Number.isSafeInteger(summary.totalCents) ||
    !Number.isSafeInteger(summary.discountCents) ||
    !Number.isSafeInteger(summary.actualAmountCents) ||
    !Number.isSafeInteger(summary.lineCount) ||
    summary.lineCount < 0 ||
    typeof summary.paymentSummary !== "string" ||
    !isVisibleState(summary.state)
  ) {
    throw new Error("Invalid local history summary.");
  }
}

function validateDetails(
  details: LocalHistoryDetails,
  summary: LocalHistorySummary,
): void {
  if (
    details.orderGuid !== summary.orderGuid ||
    details.localSequence !== summary.localSequence ||
    details.soldAtIso !== summary.soldAtIso ||
    details.cashierName !== summary.cashierName ||
    details.state !== summary.state ||
    details.totalCents !== summary.totalCents ||
    details.discountCents !== summary.discountCents ||
    details.actualAmountCents !== summary.actualAmountCents ||
    !Array.isArray(details.lines) ||
    !Array.isArray(details.tenders)
  ) {
    throw new Error("Invalid local history details.");
  }
}

function isVisibleState(value: string): boolean {
  return (
    value === "CompletedLocal" ||
    value === "PendingSync" ||
    value === "Syncing" ||
    value === "Synced" ||
    value === "Blocked403" ||
    value === "Rejected"
  );
}

function mergeStablePages(
  existing: readonly LocalHistorySummary[],
  incoming: readonly LocalHistorySummary[],
): readonly LocalHistorySummary[] {
  const seenOrderGuids = new Set(
    existing.map((summary) => summary.orderGuid),
  );
  const seenSequences = new Set(
    existing.map((summary) => summary.localSequence),
  );
  return Object.freeze([
    ...existing,
    ...incoming.filter(
      (summary) =>
        !seenOrderGuids.has(summary.orderGuid) &&
        !seenSequences.has(summary.localSequence),
    ),
  ]);
}
