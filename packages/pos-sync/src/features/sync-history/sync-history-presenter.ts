import {
  resolveSyncHistoryAccess,
  type SyncHistoryAccess,
} from "./sync-history-authorization";
import {
  buildSyncHistorySupportExport,
  retransmitGate,
  safeSyncHistoryErrorCode,
  serializeSyncHistorySupportExport,
  type LocalSyncHistoryFilters,
  type LocalSyncHistoryOrder,
  type LocalSyncHistoryOrderState,
  type LocalSyncHistoryPageQuery,
  type LocalSyncHistoryPort,
  type SyncHistoryRetransmitGate,
  type SyncHistorySupportExport,
} from "./sync-history-domain";

export type SyncHistoryRow = Readonly<{
  orderGuid: string;
  localSequence: number;
  storeCode: string;
  deviceCode: string;
  soldAtIso: string;
  state: LocalSyncHistoryOrderState;
  tenderSummary: string;
  totalCents: number;
  discountCents: number;
  actualAmountCents: number;
  outbox: LocalSyncHistoryOrder["outbox"];
  retransmit: SyncHistoryRetransmitGate;
  isSelected: boolean;
}>;

export type SyncHistoryRetransmitResult = Readonly<{
  kind: "requested" | "nothing-eligible" | "failed";
  requestedCount: number;
  skippedCount: number;
  reauthenticationRequiredCount: number;
  supervisorRequiredCount: number;
  errorCode: string | null;
}>;

type SyncHistoryStateBase = Readonly<{
  access: SyncHistoryAccess;
  filters: LocalSyncHistoryFilters;
  rows: readonly SyncHistoryRow[];
  pendingCount: number;
  nextBeforeLocalSequence: number | null;
  selectedOrderGuids: readonly string[];
  lastRetransmit: SyncHistoryRetransmitResult | null;
}>;

export type SyncHistoryPresenterState =
  | (SyncHistoryStateBase & Readonly<{ kind: "loading" }>)
  | (SyncHistoryStateBase & Readonly<{ kind: "ready" }>)
  | (SyncHistoryStateBase & Readonly<{ kind: "empty" }>)
  | (SyncHistoryStateBase & Readonly<{ kind: "failed"; errorCode: string }>);

export type SyncHistoryPresenterOptions = Readonly<{
  /** 必须由组合根提供当前可信收银员会话的冻结权限摘要。 */
  permissionCodes: readonly string[];
  port: LocalSyncHistoryPort;
  pageSize?: number;
  supportExportMaxOrders?: number;
  nowIso?: () => string;
}>;

const noFilters: LocalSyncHistoryFilters = {
  dateFromIso: null,
  dateToIso: null,
  states: [],
};

/**
 * 路由无关的本地同步历史呈现器。所有写操作都仅委托耐久 Port，绝不触碰订单内容或裸 SQLite。
 */
export class SyncHistoryPresenter {
  public readonly refundActionAvailable = false;
  public state: SyncHistoryPresenterState;

  private readonly pageSize: number;
  private readonly supportExportMaxOrders: number;
  private readonly nowIso: () => string;
  private readonly access: SyncHistoryAccess;
  private readonly listeners = new Set<() => void>();
  private filters: LocalSyncHistoryFilters = noFilters;
  private orders: LocalSyncHistoryOrder[] = [];
  private pendingCount = 0;
  private nextBeforeLocalSequence: number | null = null;
  private readonly selectedOrderGuids = new Set<string>();
  private lastRetransmit: SyncHistoryRetransmitResult | null = null;
  private queryGeneration = 0;
  private loadInFlight: Readonly<{ generation: number; promise: Promise<void> }> | null = null;
  private retransmitInFlight: Readonly<{
    generation: number;
    promise: Promise<SyncHistoryRetransmitResult>;
  }> | null = null;
  private destroyed = false;

  public constructor(private readonly options: SyncHistoryPresenterOptions) {
    this.access = resolveSyncHistoryAccess(options.permissionCodes);
    this.pageSize = options.pageSize ?? 50;
    this.supportExportMaxOrders = options.supportExportMaxOrders ?? 10_000;
    if (
      !Number.isSafeInteger(this.supportExportMaxOrders) ||
      this.supportExportMaxOrders < 1 ||
      this.supportExportMaxOrders > 10_000
    ) {
      throw new TypeError("Support export order limit must be between 1 and 10000.");
    }
    this.nowIso = options.nowIso ?? (() => new Date().toISOString());
    this.state = this.buildState("empty");
  }

  public readonly getState = (): SyncHistoryPresenterState => this.state;

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

  /**
   * 页面卸载后只清理订阅并使尚未完成的 generation 失效；不删除本地订单或 outbox。
   */
  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.queryGeneration += 1;
    this.loadInFlight = null;
    this.retransmitInFlight = null;
    this.listeners.clear();
  }

  public setFilters(filters: LocalSyncHistoryFilters): void {
    if (this.destroyed) return;
    this.filters = normalizeFilters(filters);
    this.queryGeneration += 1;
    this.orders = [];
    this.pendingCount = 0;
    this.nextBeforeLocalSequence = null;
    this.selectedOrderGuids.clear();
    // 中文注释：筛选切换立即清掉旧快照；调用方可随后显式 refresh，新筛选绝不配旧 rows。
    this.publish("empty");
  }

  public refresh(): Promise<void> {
    if (this.destroyed || !this.access.canView) return Promise.resolve();
    return this.load(true);
  }

  public loadNextPage(): Promise<void> {
    if (this.destroyed || !this.access.canView) return Promise.resolve();
    return this.nextBeforeLocalSequence === null ? Promise.resolve() : this.load(false);
  }

  public setSelected(orderGuid: string, selected: boolean): void {
    if (this.destroyed || !this.access.canView || !this.access.canManualRetransmit) return;
    const order = this.orders.find((candidate) => candidate.orderGuid === orderGuid);
    if (!order) return;
    if (selected && retransmitGate(order).kind !== "allowed") return;
    if (this.selectedOrderGuids.has(orderGuid) === selected) return;
    if (selected) this.selectedOrderGuids.add(orderGuid);
    else this.selectedOrderGuids.delete(orderGuid);
    this.publish(this.orders.length ? "ready" : "empty");
  }

  public requestRetransmitSelected(): Promise<SyncHistoryRetransmitResult> {
    if (this.destroyed) return Promise.resolve(failedResult("presenter-destroyed"));
    if (!this.canManuallyRetransmit()) return this.rejectRetransmit();
    const selected = this.orders.filter((order) => this.selectedOrderGuids.has(order.orderGuid));
    return this.requestRetransmit(selected);
  }

  public requestRetransmitDateRange(): Promise<SyncHistoryRetransmitResult> {
    if (this.destroyed) return Promise.resolve(failedResult("presenter-destroyed"));
    if (!this.canManuallyRetransmit()) return this.rejectRetransmit();
    if (!this.filters.dateFromIso || !this.filters.dateToIso) {
      const result = failedResult("date-range-required");
      this.lastRetransmit = result;
      this.publish(this.orders.length ? "ready" : "empty");
      return Promise.resolve(result);
    }
    const dateError = dateFilterError(this.filters);
    if (dateError) {
      const result = failedResult(dateError);
      this.lastRetransmit = result;
      this.publish(this.orders.length ? "ready" : "empty");
      return Promise.resolve(result);
    }
    const generation = this.queryGeneration;
    const filters = this.filters;
    return this.startRetransmit(generation, async () => {
      const candidates = await this.collectDateRangeCandidates(filters, generation);
      this.assertCurrentGeneration(generation);
      return this.restoreEligible(candidates);
    });
  }

  public async createSupportExport(): Promise<SyncHistorySupportExport> {
    if (!this.access.canExport) throw new Error("permission-required");
    const generation = this.queryGeneration;
    const filters = normalizeFilters(this.filters);
    const collected =
      await this.options.port.getLocalSyncHistorySupportSnapshot({
        filters,
        limit: this.supportExportMaxOrders,
      });
    this.assertCurrentGeneration(generation);
    validateSupportSnapshot(
      collected,
      this.supportExportMaxOrders,
    );
    const createdAtIso = this.nowIso();
    if (!isValidIsoDateTime(createdAtIso)) {
      throw new Error("Support export clock is invalid.");
    }
    const context = await this.options.port.getSupportContext();
    this.assertCurrentGeneration(generation);
    return buildSyncHistorySupportExport(
      context,
      collected.orders,
      {
        createdAtIso,
        filters,
        exportedCount: collected.orders.length,
        totalMatchingCount: collected.totalMatchingCount,
        truncated: collected.totalMatchingCount > collected.orders.length,
      },
    );
  }

  public async serializeSupportExport(): Promise<string> {
    return serializeSyncHistorySupportExport(await this.createSupportExport());
  }

  private load(replace: boolean): Promise<void> {
    if (!this.access.canView) return Promise.resolve();
    const generation = this.queryGeneration;
    if (this.loadInFlight?.generation === generation) return this.loadInFlight.promise;
    const dateError = dateFilterError(this.filters);
    if (dateError) {
      this.publish("failed", dateError);
      return Promise.resolve();
    }
    this.publish("loading");
    const filters = this.filters;
    const beforeLocalSequence = replace ? null : this.nextBeforeLocalSequence;
    const promise = this.loadPage(replace, generation, filters, beforeLocalSequence).finally(() => {
      if (this.loadInFlight?.promise === promise) this.loadInFlight = null;
    });
    this.loadInFlight = { generation, promise };
    return promise;
  }

  private async loadPage(
    replace: boolean,
    generation: number,
    filters: LocalSyncHistoryFilters,
    beforeLocalSequence: number | null,
  ): Promise<void> {
    try {
      const query: LocalSyncHistoryPageQuery = {
        limit: this.pageSize,
        beforeLocalSequence,
        filters,
      };
      const page = await this.options.port.listLocalSyncHistory(query);
      if (generation !== this.queryGeneration) return;
      validatePage(page, query.beforeLocalSequence);
      this.orders = replace
        ? [...page.orders]
        : mergeStablePages(this.orders, page.orders);
      this.pendingCount = page.pendingCount;
      this.nextBeforeLocalSequence = page.nextBeforeLocalSequence;
      this.removeMissingSelections();
      this.publish(this.orders.length ? "ready" : "empty");
    } catch {
      if (generation !== this.queryGeneration) return;
      this.publish("failed", "history-load-failed");
    }
  }

  private requestRetransmit(candidates: readonly LocalSyncHistoryOrder[]): Promise<SyncHistoryRetransmitResult> {
    const generation = this.queryGeneration;
    return this.startRetransmit(generation, () => {
      this.assertCurrentGeneration(generation);
      return this.restoreEligible(candidates);
    });
  }

  private canManuallyRetransmit(): boolean {
    return this.access.canView && this.access.canManualRetransmit;
  }

  private rejectRetransmit(): Promise<SyncHistoryRetransmitResult> {
    const result = failedResult("permission-required");
    this.lastRetransmit = result;
    this.publish(this.orders.length ? "ready" : "empty");
    return Promise.resolve(result);
  }

  private startRetransmit(
    generation: number,
    work: () => Promise<SyncHistoryRetransmitResult>,
  ): Promise<SyncHistoryRetransmitResult> {
    if (this.retransmitInFlight?.generation === generation) return this.retransmitInFlight.promise;
    if (this.retransmitInFlight) return Promise.resolve(failedResult("retransmit-in-progress"));
    const promise = work()
      .then(async (result) => {
        if (generation !== this.queryGeneration) return failedResult("query-superseded");
        await this.refresh();
        if (generation !== this.queryGeneration) return failedResult("query-superseded");
        this.lastRetransmit = result;
        this.publish(this.orders.length ? "ready" : "empty");
        return result;
      })
      .catch(() => {
        if (generation !== this.queryGeneration) return failedResult("query-superseded");
        const result = failedResult("retransmit-failed");
        this.lastRetransmit = result;
        this.publish(this.orders.length ? "ready" : "empty");
        return result;
      })
      .finally(() => {
        if (this.retransmitInFlight?.promise === promise) this.retransmitInFlight = null;
      });
    this.retransmitInFlight = { generation, promise };
    return promise;
  }

  private async restoreEligible(candidates: readonly LocalSyncHistoryOrder[]): Promise<SyncHistoryRetransmitResult> {
    const gate = countGates(candidates);
    if (!gate.orderGuids.length) {
      return {
        kind: "nothing-eligible",
        requestedCount: 0,
        skippedCount: gate.skippedCount,
        reauthenticationRequiredCount: gate.reauthenticationRequiredCount,
        supervisorRequiredCount: gate.supervisorRequiredCount,
        errorCode: null,
      };
    }
    // 仓储在单个排他事务内逐笔 CAS，不拼接 IN 参数；整批原子提交可避免
    // 后续分批失败时把前批已生效误报成“0 笔成功”。
    const restored =
      await this.options.port.restoreExistingOrderOutboxToPending(
        gate.orderGuids,
      );
    return {
      kind: "requested",
      requestedCount: restored.restoredOrderGuids.length,
      skippedCount:
        gate.skippedCount + restored.skippedOrderGuids.length,
      reauthenticationRequiredCount: gate.reauthenticationRequiredCount,
      supervisorRequiredCount: gate.supervisorRequiredCount,
      errorCode: null,
    };
  }

  private async collectDateRangeCandidates(
    filters: LocalSyncHistoryFilters,
    generation: number,
  ): Promise<readonly LocalSyncHistoryOrder[]> {
    const candidates: LocalSyncHistoryOrder[] = [];
    let beforeLocalSequence: number | null = null;
    do {
      const query: LocalSyncHistoryPageQuery = {
        limit: this.pageSize,
        beforeLocalSequence,
        filters: {
          dateFromIso: filters.dateFromIso,
          dateToIso: filters.dateToIso,
          states: [],
        },
      };
      const page = await this.options.port.listLocalSyncHistory(query);
      this.assertCurrentGeneration(generation);
      validatePage(page, beforeLocalSequence);
      candidates.push(...page.orders);
      beforeLocalSequence = page.nextBeforeLocalSequence;
    } while (beforeLocalSequence !== null);
    return candidates;
  }

  private assertCurrentGeneration(generation: number): void {
    if (generation !== this.queryGeneration) throw new Error("Sync history query was superseded.");
  }

  private removeMissingSelections(): void {
    const current = new Set(
      this.orders
        .filter((order) => retransmitGate(order).kind === "allowed")
        .map((order) => order.orderGuid),
    );
    for (const orderGuid of this.selectedOrderGuids) {
      if (!current.has(orderGuid)) this.selectedOrderGuids.delete(orderGuid);
    }
  }

  private publish(kind: SyncHistoryPresenterState["kind"], errorCode?: string): void {
    if (this.destroyed) return;
    this.state = this.buildState(kind, errorCode);
    for (const listener of [...this.listeners]) listener();
  }

  private buildState(kind: SyncHistoryPresenterState["kind"], errorCode?: string): SyncHistoryPresenterState {
    const base: SyncHistoryStateBase = {
      access: this.access,
      filters: this.filters,
      rows: this.orders.map((order) => toRow(order, this.selectedOrderGuids.has(order.orderGuid))),
      pendingCount: this.pendingCount,
      nextBeforeLocalSequence: this.nextBeforeLocalSequence,
      selectedOrderGuids: [...this.selectedOrderGuids],
      lastRetransmit: this.lastRetransmit,
    };
    return kind === "failed"
      ? { ...base, kind, errorCode: errorCode ?? "history-failed" }
      : { ...base, kind };
  }
}

function normalizeFilters(value: LocalSyncHistoryFilters): LocalSyncHistoryFilters {
  return {
    dateFromIso: value.dateFromIso,
    dateToIso: value.dateToIso,
    states: [...new Set(value.states)],
  };
}

function validateSupportSnapshot(
  value: Readonly<{
    orders: readonly LocalSyncHistoryOrder[];
    totalMatchingCount: number;
  }>,
  limit: number,
): void {
  if (
    !Array.isArray(value.orders) ||
    value.orders.length > limit ||
    !Number.isSafeInteger(value.totalMatchingCount) ||
    value.totalMatchingCount < value.orders.length
  ) {
    throw new Error("Sync history support snapshot is invalid.");
  }
}

function dateFilterError(filters: LocalSyncHistoryFilters): string | null {
  const from = filters.dateFromIso;
  const to = filters.dateToIso;
  if ((from !== null && !isValidIsoDateTime(from)) || (to !== null && !isValidIsoDateTime(to))) {
    return "invalid-date-range";
  }
  if (from !== null && to !== null && Date.parse(from) > Date.parse(to)) return "invalid-date-range";
  return null;
}

function isValidIsoDateTime(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,3}))?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  const offset = match[8] ?? "";
  const candidate = new Date(0);
  candidate.setUTCFullYear(year, month - 1, day);
  candidate.setUTCHours(hour, minute, second, 0);
  const calendarIsValid = candidate.getUTCFullYear() === year
    && candidate.getUTCMonth() === month - 1
    && candidate.getUTCDate() === day
    && candidate.getUTCHours() === hour
    && candidate.getUTCMinutes() === minute
    && candidate.getUTCSeconds() === second;
  if (!calendarIsValid || !Number.isFinite(Date.parse(value))) return false;
  if (offset === "Z") return true;
  const offsetHour = Number(offset.slice(1, 3));
  const offsetMinute = Number(offset.slice(4, 6));
  return offsetHour <= 23 && offsetMinute <= 59;
}

function toRow(order: LocalSyncHistoryOrder, isSelected: boolean): SyncHistoryRow {
  return {
    orderGuid: order.orderGuid,
    localSequence: order.localSequence,
    storeCode: order.storeCode,
    deviceCode: order.deviceCode,
    soldAtIso: order.soldAtIso,
    state: order.state,
    tenderSummary: order.tenders.map((tender) => `${tender.method.toUpperCase()} ${formatCents(tender.amountCents)}`).join(", "),
    totalCents: order.totalCents,
    discountCents: order.discountCents,
    actualAmountCents: order.actualAmountCents,
    outbox: order.outbox
      ? { ...order.outbox, lastErrorCode: safeSyncHistoryErrorCode(order.outbox.lastErrorCode) }
      : null,
    retransmit: retransmitGate(order),
    isSelected,
  };
}

function formatCents(value: number): string {
  const sign = value < 0 ? "-" : "";
  const cents = Math.abs(value);
  return `${sign}$${Math.floor(cents / 100)}.${String(cents % 100).padStart(2, "0")}`;
}

function countGates(candidates: readonly LocalSyncHistoryOrder[]): Readonly<{
  orderGuids: readonly string[];
  skippedCount: number;
  reauthenticationRequiredCount: number;
  supervisorRequiredCount: number;
}> {
  const orderGuids: string[] = [];
  let skippedCount = 0;
  let reauthenticationRequiredCount = 0;
  let supervisorRequiredCount = 0;
  for (const candidate of candidates) {
    const gate = retransmitGate(candidate);
    if (gate.kind === "allowed") {
      orderGuids.push(candidate.orderGuid);
      continue;
    }
    skippedCount += 1;
    if (gate.reason === "reauthentication-required") reauthenticationRequiredCount += 1;
    if (gate.reason === "supervisor-required") supervisorRequiredCount += 1;
  }
  return { orderGuids, skippedCount, reauthenticationRequiredCount, supervisorRequiredCount };
}

function failedResult(errorCode: string): SyncHistoryRetransmitResult {
  return {
    kind: "failed",
    requestedCount: 0,
    skippedCount: 0,
    reauthenticationRequiredCount: 0,
    supervisorRequiredCount: 0,
    errorCode,
  };
}

function validatePage(
  page: Readonly<{ orders: readonly LocalSyncHistoryOrder[]; nextBeforeLocalSequence: number | null; pendingCount: number }>,
  beforeLocalSequence: number | null,
): void {
  if (!Number.isSafeInteger(page.pendingCount) || page.pendingCount < 0) throw new Error("Invalid pending count.");
  if (!page.orders.length) {
    if (page.nextBeforeLocalSequence !== null) throw new Error("Empty sync history page cannot continue.");
    return;
  }
  let previous = beforeLocalSequence;
  for (const order of page.orders) {
    if (!Number.isSafeInteger(order.localSequence) || order.localSequence <= 0 || (previous !== null && order.localSequence >= previous)) {
      throw new Error("Sync history page is not stable by local sequence.");
    }
    previous = order.localSequence;
  }
  const cursor = page.nextBeforeLocalSequence;
  if (cursor !== null) {
    const lastLocalSequence = page.orders.at(-1)?.localSequence;
    if (!Number.isSafeInteger(cursor) || cursor <= 0 || cursor !== lastLocalSequence) {
      throw new Error("Next local sequence cursor must equal the final row.");
    }
    if (beforeLocalSequence !== null && cursor >= beforeLocalSequence) {
      throw new Error("Next local sequence cursor must strictly decrease.");
    }
  }
}

function mergeStablePages(
  current: readonly LocalSyncHistoryOrder[],
  next: readonly LocalSyncHistoryOrder[],
): LocalSyncHistoryOrder[] {
  const known = new Set(current.map((order) => order.orderGuid));
  const merged = [...current, ...next.filter((order) => !known.has(order.orderGuid))];
  return merged.sort((left, right) => right.localSequence - left.localSequence);
}
