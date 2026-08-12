import type {
  HeldOrderActionResult,
  SharedHeldOrderLocalShareRow,
  SharedHeldOrderRemoteRow,
  SharedHeldOrderShareRequestOutcome,
  SharedHeldOrdersViewPort,
  SharedHeldOrderTakeViewResult,
} from "./held-orders-domain";
import { HeldOrdersOrchestrator } from "./held-orders-orchestrator";

import type { HeldOrderSummary } from "@/core/contracts";
import {
  businessDayUtcRange,
  resolveBusinessTimeZone,
} from "@/features/sync-history/business-day-range";

export type HeldOrdersDateFilter = "today" | "all";
export type HeldOrdersSourceTab = "local" | "other";

export type HeldOrdersPresenterOptions = Readonly<{
  businessTimeZone?: string;
  currentDeviceCode?: string;
  now?(): Date;
}>;

export type HeldOrderViewStatus =
  | "local-pending"
  | "claiming-here"
  | "local-pending-publish"
  | "published-shareable"
  | "remote-pending"
  | "blocked";

/** 本地与远端挂单合并后的行；本地副本存在时优先保留（可离线取回）。 */
export type HeldOrderViewRow = Readonly<{
  holdId: string;
  local: HeldOrderSummary | null;
  remote: SharedHeldOrderRemoteRow | null;
  status: HeldOrderViewStatus;
  blockReason: string | null;
  shareState?: SharedHeldOrderLocalShareRow["shareState"] | undefined;
  shareRequestedAtIso?: string | null | undefined;
  isSyntheticSharedClaim?: boolean | undefined;
}>;

export type HeldOrderShareActionResult = Readonly<{
  ok: boolean;
  outcome: SharedHeldOrderShareRequestOutcome;
  holdId: string;
}>;

type RemoteHeldOrdersRefreshResult =
  | Readonly<{ status: "fulfilled"; value: readonly SharedHeldOrderRemoteRow[] }>
  | Readonly<{ status: "rejected"; reason: unknown }>;

export type HeldOrdersPresenterState = Readonly<{
  kind: "loading" | "ready" | "unauthorized" | "failed";
  rows: readonly HeldOrderViewRow[];
  dateFilter: HeldOrdersDateFilter;
  sourceTab: HeldOrdersSourceTab;
  busy: boolean;
  shareBusyHoldIds: readonly string[];
  lastAction: HeldOrderActionResult | null;
  /** 非阻塞共享同步错误（本地行仍然保留），机器码由屏幕映射文案。 */
  refreshError: string | null;
  /** 远端挂单独立后台刷新；本机 SQLite ready 不依赖它。 */
  remoteRefreshing: boolean;
  sharedEnabled: boolean;
}>;

/** React 无关 presenter：只叠加共享数据源/动作，绝不改变旧 hold/recall 语义。 */
export class HeldOrdersPresenter {
  public state: HeldOrdersPresenterState = {
    kind: "loading",
    rows: [],
    dateFilter: "today",
    sourceTab: "local",
    busy: false,
    shareBusyHoldIds: [],
    lastAction: null,
    refreshError: null,
    remoteRefreshing: false,
    sharedEnabled: false,
  };

  private readonly listeners = new Set<() => void>();
  private refreshInFlight: Promise<void> | null = null;
  private remoteRefreshInFlight: Promise<RemoteHeldOrdersRefreshResult> | null = null;
  private actionInFlight: Promise<HeldOrderActionResult> | null = null;
  private readonly shareRequestsInFlight = new Map<
    string,
    Promise<HeldOrderShareActionResult>
  >();
  private destroyed = false;
  private sharedOrders: SharedHeldOrdersViewPort | null = null;
  private autoRefreshTimer: ReturnType<typeof setInterval> | null = null;
  private allRows: readonly HeldOrderViewRow[] = [];
  private localRowsCache: readonly HeldOrderSummary[] = [];
  private remoteRowsCache: readonly SharedHeldOrderRemoteRow[] = [];
  private shareRowsCache: readonly SharedHeldOrderLocalShareRow[] = [];
  private localCacheReady = false;
  private refreshGeneration = 0;
  private readonly businessTimeZone: string;
  private readonly currentDeviceCode: string | null;
  private readonly now: () => Date;

  public constructor(
    private readonly orchestrator: HeldOrdersOrchestrator,
    options: HeldOrdersPresenterOptions = {},
  ) {
    const businessTimeZone = resolveBusinessTimeZone(options.businessTimeZone);
    if (!businessTimeZone) {
      throw new TypeError("Held orders business time zone is invalid.");
    }
    this.businessTimeZone = businessTimeZone;
    this.currentDeviceCode = options.currentDeviceCode?.trim() || null;
    this.now = options.now ?? (() => new Date());
  }

  public readonly getState = (): HeldOrdersPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** 组合根路由在 createPresenter 后注入共享视图端口；重复注入以后者为准。 */
  public attachSharedOrders(sharedOrders: SharedHeldOrdersViewPort): void {
    this.sharedOrders = sharedOrders;
    this.patch({ sharedEnabled: true });
  }

  public supportsForceRelease(): boolean {
    return this.sharedOrders?.forceRelease != null;
  }

  public setDateFilter(dateFilter: HeldOrdersDateFilter): void {
    if (this.destroyed || this.state.dateFilter === dateFilter) return;
    this.patch({
      dateFilter,
      rows: this.visibleRows(this.allRows, dateFilter, this.state.sourceTab),
    });
  }

  public setSourceTab(sourceTab: HeldOrdersSourceTab): void {
    if (this.destroyed || this.state.sourceTab === sourceTab) return;
    this.patch({
      sourceTab,
      rows: this.visibleRows(this.allRows, this.state.dateFilter, sourceTab),
    });
  }

  public startAutoRefresh(intervalMs = 10_000): void {
    if (this.destroyed || this.autoRefreshTimer) return;
    this.autoRefreshTimer = setInterval(() => {
      void this.refresh();
    }, intervalMs);
  }

  public stopAutoRefresh(): void {
    if (this.autoRefreshTimer === null) return;
    clearInterval(this.autoRefreshTimer);
    this.autoRefreshTimer = null;
  }

  public destroy(): void {
    this.destroyed = true;
    this.refreshGeneration += 1;
    this.stopAutoRefresh();
    this.shareRequestsInFlight.clear();
    this.sharedOrders = null;
    this.listeners.clear();
  }

  public refresh(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.refreshInFlight) return this.refreshInFlight;
    const generation = ++this.refreshGeneration;
    this.patch({
      // 本地缓存已就绪时，后台远端刷新不能把可操作页面退回 loading。
      kind: this.localCacheReady ? "ready" : "loading",
      lastAction: null,
    });
    const operation = (async () => {
      if (!this.sharedOrders) {
        await this.refreshLocalOnly(generation);
        return;
      }
      await this.refreshWithShared(this.sharedOrders, generation);
    })().finally(() => {
      if (this.refreshInFlight === operation) this.refreshInFlight = null;
    });
    this.refreshInFlight = operation;
    return operation;
  }

  public hold(): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.hold());
  }

  public recall(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.recall(holdId));
  }

  public recover(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.recover(holdId));
  }

  public release(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() =>
      this.orchestrator.release(
        holdId,
        this.sharedOrders?.releaseOwnedClaim,
      ),
    );
  }

  public delete(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() =>
      this.orchestrator.delete(
        holdId,
        this.sharedOrders?.cancelOwnedHold,
      ),
    );
  }

  /** 共享按钮只写一次性意图；busy 只绑定当前行，不锁住本机 recall。 */
  public requestShare(holdId: string): Promise<HeldOrderShareActionResult> {
    if (this.destroyed) {
      return Promise.resolve({ ok: false, outcome: "ineligible", holdId });
    }
    const requestShare = this.sharedOrders?.requestShare;
    if (!requestShare) {
      return Promise.resolve({ ok: false, outcome: "ineligible", holdId });
    }
    const existing = this.shareRequestsInFlight.get(holdId);
    if (existing) return existing;

    this.patch({
      shareBusyHoldIds: [...this.state.shareBusyHoldIds, holdId],
    });
    const operation = this.requestShareOnce(holdId, requestShare).finally(() => {
      if (this.shareRequestsInFlight.get(holdId) !== operation) return;
      this.shareRequestsInFlight.delete(holdId);
      if (!this.destroyed) {
        this.patch({
          shareBusyHoldIds: this.state.shareBusyHoldIds.filter(
            (busyHoldId) => busyHoldId !== holdId,
          ),
        });
      }
    });
    this.shareRequestsInFlight.set(holdId, operation);
    return operation;
  }

  private async requestShareOnce(
    holdId: string,
    requestShare: NonNullable<SharedHeldOrdersViewPort["requestShare"]>,
  ): Promise<HeldOrderShareActionResult> {
    try {
      const outcome = await requestShare(holdId);
      if (this.destroyed) return { ok: false, outcome, holdId };
      if (outcome === "requested" || outcome === "already-requested") {
        await this.refresh();
      }
      return {
        ok: outcome === "requested" || outcome === "already-requested",
        outcome,
        holdId,
      };
    } catch {
      return { ok: false, outcome: "ineligible", holdId };
    }
  }

  /** 在线取单：组合根把 shared coordinator 的 prepare→durable→activate→restore 适配进来。 */
  public takeRemote(holdGuid: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.takeRemoteOnce(holdGuid));
  }

  /** 原设备离线本地取回：只读取本地已发布副本，不触碰服务端。 */
  public recallLocalShared(holdGuid: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.recallLocalSharedOnce(holdGuid));
  }

  /**
   * 强制释放：只在组合根已提供授权包装的 forceRelease 时可用；原因必须非空。
   * 当前运行时尚无授权接口时返回 force-release-unavailable，绝不伪造调用。
   */
  public forceRelease(holdGuid: string, reason: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.forceReleaseOnce(holdGuid, reason));
  }

  private async refreshLocalOnly(generation: number): Promise<void> {
    try {
      const rows = await this.orchestrator.list();
      if (this.destroyed || generation !== this.refreshGeneration) return;
      this.localRowsCache = rows;
      this.localCacheReady = true;
      this.remoteRowsCache = [];
      this.shareRowsCache = [];
      this.replaceRows(toLocalViewRows(rows));
      this.patch({ kind: "ready", refreshError: null, remoteRefreshing: false });
    } catch (error: unknown) {
      if (this.destroyed || generation !== this.refreshGeneration) return;
      this.allRows = [];
      this.patch({
        kind:
          error instanceof Error && error.message === "HELD_ORDER_LIST_UNAUTHORIZED"
            ? "unauthorized"
            : "failed",
        rows: [],
        remoteRefreshing: false,
      });
    }
  }

  private async refreshWithShared(
    shared: SharedHeldOrdersViewPort,
    generation: number,
  ): Promise<void> {
    this.patch({ remoteRefreshing: true });
    const localPromise = this.orchestrator.list();
    // 远端请求一启动就把拒绝转成普通结果；即使本地账本先失败并提前返回，
    // 远端失败也不会成为未处理 rejection。
    const remoteResultPromise = this.getOrStartRemoteRefresh(shared);
    const sharePromise = shared.listLocalShareState
      ? callAsPromise(() => shared.listLocalShareState!())
      : Promise.resolve([] as readonly SharedHeldOrderLocalShareRow[]);

    // 本地 SQLite 与 share state 是进入页面的最小 ready 条件；远端只在后台
    // 收敛，且每个结果都带 generation，防止旧页面/旧刷新回写新状态。
    const localAndShare = await Promise.allSettled([localPromise, sharePromise]);
    if (this.destroyed || generation !== this.refreshGeneration) return;
    const localResult = localAndShare[0];
    const shareResult = localAndShare[1];
    if (localResult.status === "rejected") {
      this.allRows = [];
      this.patch({
        kind:
          localResult.reason instanceof Error &&
          localResult.reason.message === "HELD_ORDER_LIST_UNAUTHORIZED"
            ? "unauthorized"
            : "failed",
        rows: [],
        refreshError: null,
        remoteRefreshing: false,
      });
      return;
    }

    this.localRowsCache = localResult.value;
    this.localCacheReady = true;
    if (shareResult.status === "fulfilled") {
      this.shareRowsCache = shareResult.value;
    }
    this.replaceRows(
      mergeHeldOrderRows(
        this.localRowsCache,
        this.remoteRowsCache,
        this.shareRowsCache,
        // 缓存只用于本地首屏补充展示；必须等本轮远端成功后才能权威移除
        // Published 本地副本，避免旧缓存让刚共享的行短暂消失。
        false,
        this.currentDeviceCode,
      ),
    );
    const shareError = shareResult.status === "rejected";
    this.patch({
      kind: "ready",
      refreshError: shareError ? "SHARED_HELD_ORDERS_SYNC_FAILED" : null,
    });

    // 让已经同步完成的 remote promise 在本次 refresh 返回前收敛；未完成的
    // promise 不会阻塞本地 ready，也不会在失败时清空本地缓存。
    await Promise.resolve();
    void remoteResultPromise.then((remoteResult) => {
      if (remoteResult.status === "fulfilled") {
        if (this.destroyed || generation !== this.refreshGeneration) return;
        const remoteRows = remoteResult.value;
        this.remoteRowsCache = remoteRows;
        this.replaceRows(
          mergeHeldOrderRows(
            this.localRowsCache,
            this.remoteRowsCache,
            this.shareRowsCache,
            true,
            this.currentDeviceCode,
          ),
        );
        this.patch({
          kind: "ready",
          refreshError: shareError ? "SHARED_HELD_ORDERS_SYNC_FAILED" : null,
          remoteRefreshing: false,
        });
      } else {
        if (this.destroyed || generation !== this.refreshGeneration) return;
        this.replaceRows(
          mergeHeldOrderRows(
            this.localRowsCache,
            this.remoteRowsCache,
            this.shareRowsCache,
            false,
            this.currentDeviceCode,
          ),
        );
        this.patch({
          kind: "ready",
          refreshError: "SHARED_HELD_ORDERS_SYNC_FAILED",
          remoteRefreshing: false,
        });
      }
    });
  }

  private getOrStartRemoteRefresh(
    shared: SharedHeldOrdersViewPort,
  ): Promise<RemoteHeldOrdersRefreshResult> {
    if (this.remoteRefreshInFlight) return this.remoteRefreshInFlight;
    const operation = callAsPromise(() => shared.listRemotePending())
      .then<RemoteHeldOrdersRefreshResult, RemoteHeldOrdersRefreshResult>(
        (value) => ({ status: "fulfilled", value }),
        (reason: unknown) => ({ status: "rejected", reason }),
      );
    this.remoteRefreshInFlight = operation;
    void operation.then(() => {
      if (this.remoteRefreshInFlight === operation) {
        this.remoteRefreshInFlight = null;
      }
    });
    return operation;
  }

  private async takeRemoteOnce(holdGuid: string): Promise<HeldOrderActionResult> {
    const shared = this.sharedOrders;
    if (!shared) return { ok: false, code: "shared-not-available" };
    try {
      return mapSharedTake(await shared.takeRemoteHold(holdGuid));
    } catch (error: unknown) {
      return mapSharedTakeError(error, holdGuid);
    }
  }

  private async recallLocalSharedOnce(holdGuid: string): Promise<HeldOrderActionResult> {
    const shared = this.sharedOrders;
    if (!shared) return { ok: false, code: "shared-not-available" };
    try {
      return mapSharedTake(await shared.recallLocalPublication(holdGuid));
    } catch {
      return { ok: false, code: "shared-conflict", holdId: holdGuid };
    }
  }

  private async forceReleaseOnce(
    holdGuid: string,
    reason: string,
  ): Promise<HeldOrderActionResult> {
    const forceRelease = this.sharedOrders?.forceRelease;
    if (!forceRelease) return { ok: false, code: "force-release-unavailable" };
    if (!reason.trim()) return { ok: false, code: "force-release-reason-required" };
    try {
      const result = await forceRelease({ holdGuid, reason: reason.trim() });
      return result.ok
        ? { ok: true, code: "force-released", holdId: holdGuid }
        : { ...result, holdId: holdGuid };
    } catch {
      return { ok: false, code: "force-release-failed", holdId: holdGuid };
    }
  }

  private async runAction(
    action: () => Promise<HeldOrderActionResult>,
  ): Promise<HeldOrderActionResult> {
    if (this.destroyed) return { ok: false, code: "operation-in-progress" };
    if (this.actionInFlight) {
      return { ok: false, code: "operation-in-progress" };
    }
    this.patch({ busy: true, lastAction: null });
    const operation = (async () => {
      let result: HeldOrderActionResult;
      try {
        result = await action();
      } catch {
        result = { ok: false, code: "load-failed" };
      }
      if (this.destroyed) return result;
      this.patch({ busy: false, lastAction: result });
      if (shouldRefreshAfterAction(result)) {
        await this.refresh();
      }
      return result;
    })().finally(() => {
      if (this.actionInFlight === operation) this.actionInFlight = null;
    });
    this.actionInFlight = operation;
    return operation;
  }

  private patch(patch: Partial<HeldOrdersPresenterState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of [...this.listeners]) {
      try {
        listener();
      } catch {
        // 一个已卸载页面不能阻止其他订阅者看到最新耐久状态。
      }
    }
  }

  private replaceRows(rows: readonly HeldOrderViewRow[]): void {
    this.allRows = rows;
    this.patch({
      rows: this.visibleRows(rows, this.state.dateFilter, this.state.sourceTab),
    });
  }

  private visibleRows(
    rows: readonly HeldOrderViewRow[],
    dateFilter: HeldOrdersDateFilter,
    sourceTab: HeldOrdersSourceTab,
  ): readonly HeldOrderViewRow[] {
    const sourceRows = rows.filter((row) =>
      rowBelongsToSource(row, sourceTab, this.currentDeviceCode),
    );
    if (dateFilter === "all") return sourceRows;
    return filterRowsForBusinessToday(sourceRows, this.now(), this.businessTimeZone);
  }
}

function shouldRefreshAfterAction(result: HeldOrderActionResult): boolean {
  if (
    result.ok &&
    (result.code === "recalled" || result.code === "recovered")
  ) {
    // 购物车已经恢复；页面会立即返回收银，下次进入挂单页再刷新即可。
    return false;
  }
  return (
    result.ok ||
    result.code === "hold-committed-cart-not-cleared" ||
    result.code === "hold-fence-not-cleared" ||
    result.code === "restore-failed" ||
    result.code === "rollback-failed" ||
    result.code === "release-failed" ||
    result.code === "shared-prepared-awaiting-activation" ||
    result.code === "shared-fence-held" ||
    result.code === "shared-restore-failed" ||
    result.code === "shared-conflict" ||
    result.code === "delete-failed" ||
    result.code === "delete-shared-failed" ||
    result.code === "force-released" ||
    result.code === "force-release-failed"
  );
}

function toLocalViewRows(rows: readonly HeldOrderSummary[]): HeldOrderViewRow[] {
  return rows.map((local) => ({
    holdId: local.holdId,
    local,
    remote: null,
    status: local.status === "Recalling" ? "claiming-here" : "local-pending",
    blockReason: null,
    isSyntheticSharedClaim: local.isSyntheticSharedClaim === true,
  }));
}

/**
 * 按 HoldGuid 去重合并：本地副本优先保留（离线取回能力），远端项补充
 * 来源设备/收银员/时间/件数/金额；服务端 Active claim 已被 API 隐藏。
 */
function mergeHeldOrderRows(
  localRows: readonly HeldOrderSummary[],
  remoteRows: readonly SharedHeldOrderRemoteRow[],
  shareRows: readonly SharedHeldOrderLocalShareRow[],
  remoteListAuthoritative: boolean,
  currentDeviceCode: string | null,
): HeldOrderViewRow[] {
  const remoteByHoldGuid = new Map(
    remoteRows.map((remote) => [remote.holdGuid, remote]),
  );
  const shareByHoldId = new Map(
    shareRows.map((share) => [share.holdId, share]),
  );
  const rows = new Map<string, HeldOrderViewRow>();
  for (const local of localRows) {
    const remote = remoteByHoldGuid.get(local.holdId) ?? null;
    const share = shareByHoldId.get(local.holdId) ?? null;
    if (
      remoteListAuthoritative &&
      local.status !== "Recalling" &&
      share?.shareState === "Published" &&
      remote === null
    ) {
      // 待取列表成功返回时，Published 但已不在列表说明服务端已进入
      // Claimed/Completed/Cancelled；在线界面必须隐藏，失败刷新仍保留离线副本。
      continue;
    }
    const status = local.status === "Recalling"
      ? "claiming-here"
      : share?.shareState === "Blocked"
        ? "blocked"
        : share?.shareState === "Published" || remote
          ? "published-shareable"
          : share?.shareState === "PendingPublish" ||
              (share?.shareState === "NeedsEvaluation" &&
                share.requestedAtIso !== null)
            ? "local-pending-publish"
            : "local-pending";
    rows.set(local.holdId, {
      holdId: local.holdId,
      local,
      remote,
      status,
      blockReason: status === "blocked" ? (share?.blockReason ?? null) : null,
      shareState: share?.shareState,
      shareRequestedAtIso: share?.requestedAtIso ?? null,
      isSyntheticSharedClaim: local.isSyntheticSharedClaim === true,
    });
  }
  for (const remote of remoteRows) {
    if (!rows.has(remote.holdGuid)) {
      rows.set(remote.holdGuid, {
        holdId: remote.holdGuid,
        local: null,
        remote,
        status: "remote-pending",
        blockReason: null,
        isSyntheticSharedClaim: false,
      });
    }
  }
  return [...rows.values()].sort((left, right) => {
    const leftMs = rowHeldAtMs(left);
    const rightMs = rowHeldAtMs(right);
    return rightMs - leftMs || left.holdId.localeCompare(right.holdId);
  });
}

function rowBelongsToSource(
  row: HeldOrderViewRow,
  sourceTab: HeldOrdersSourceTab,
  currentDeviceCode: string | null,
): boolean {
  // 没有可信设备身份的旧测试/嵌入调用保持兼容；生产组合根总会传入当前设备。
  if (currentDeviceCode === null) return true;
  if (row.local) {
    const isSynthetic =
      row.isSyntheticSharedClaim === true ||
      row.local.isSyntheticSharedClaim === true;
    const isCurrentDevice = sameDeviceCode(
      row.local.scope.deviceCode,
      currentDeviceCode,
    );
    const belongsToLocal = !isSynthetic && isCurrentDevice;
    return sourceTab === "local" ? belongsToLocal : !belongsToLocal;
  }
  if (row.remote) {
    const isCurrentDevice = sameDeviceCode(
      row.remote.deviceCode,
      currentDeviceCode,
    );
    return sourceTab === "local" ? isCurrentDevice : !isCurrentDevice;
  }
  return false;
}

function sameDeviceCode(left: string, right: string): boolean {
  return left.trim().toLocaleUpperCase("en-US") ===
    right.trim().toLocaleUpperCase("en-US");
}

function rowHeldAtMs(row: HeldOrderViewRow): number {
  const iso = row.local?.heldAtIso ?? row.remote?.heldAtIso ?? "";
  const parsed = Date.parse(iso);
  return Number.isFinite(parsed) ? parsed : 0;
}

function filterRowsForBusinessToday(
  rows: readonly HeldOrderViewRow[],
  now: Date,
  businessTimeZone: string,
): HeldOrderViewRow[] {
  const businessDate = dateInTimeZone(now, businessTimeZone);
  if (!businessDate) return [];
  const range = businessDayUtcRange(
    businessDate,
    businessDate,
    businessTimeZone,
  );
  if (!range?.dateFromIso || !range.dateToIso) return [];
  const from = Date.parse(range.dateFromIso);
  const to = Date.parse(range.dateToIso);
  return rows.filter((row) => {
    const heldAt = rowHeldAtMs(row);
    return heldAt >= from && heldAt <= to;
  });
}

function dateInTimeZone(now: Date, businessTimeZone: string): string | null {
  if (!Number.isFinite(now.getTime())) return null;
  try {
    const parts = new Map(
      new Intl.DateTimeFormat("en-CA", {
        calendar: "gregory",
        day: "2-digit",
        month: "2-digit",
        numberingSystem: "latn",
        timeZone: businessTimeZone,
        year: "numeric",
      })
        .formatToParts(now)
        .filter((part) => ["year", "month", "day"].includes(part.type))
        .map((part) => [part.type, part.value]),
    );
    const year = parts.get("year");
    const month = parts.get("month");
    const day = parts.get("day");
    return year && month && day ? `${year}-${month}-${day}` : null;
  } catch {
    return null;
  }
}

/** 把端口的同步异常转成 rejected Promise，交由 allSettled 做非阻塞降级。 */
function callAsPromise<T>(operation: () => T | Promise<T>): Promise<T> {
  return Promise.resolve().then(operation);
}

function mapSharedTake(result: SharedHeldOrderTakeViewResult): HeldOrderActionResult {
  switch (result.outcome) {
    case "restored":
      return { ok: true, code: "recalled", holdId: result.holdGuid };
    case "prepared-awaiting-activation":
      return {
        ok: false,
        code: "shared-prepared-awaiting-activation",
        holdId: result.holdGuid,
      };
    case "fence-held":
      return { ok: false, code: "shared-fence-held", holdId: result.holdGuid };
    case "conflict":
      return { ok: false, code: "shared-conflict", holdId: result.holdGuid };
  }
}

function mapSharedTakeError(
  error: unknown,
  holdGuid: string,
): HeldOrderActionResult {
  const code = sharedCoordinatorErrorCode(error);
  switch (code) {
    case "FENCE_CONFLICT":
      return { ok: false, code: "shared-fence-held", holdId: holdGuid };
    case "CART_NOT_EMPTY":
      return { ok: false, code: "cart-not-empty", holdId: holdGuid };
    case "SALE_MODE_REQUIRED":
      return { ok: false, code: "sale-mode-required", holdId: holdGuid };
    case "RESTORE_FAILED":
      return { ok: false, code: "shared-restore-failed", holdId: holdGuid };
    default:
      return { ok: false, code: "shared-conflict", holdId: holdGuid };
  }
}

function sharedCoordinatorErrorCode(error: unknown): string | null {
  if (
    !(error instanceof Error) ||
    error.name !== "SharedHeldOrderCoordinatorError" ||
    !("code" in error)
  ) {
    return null;
  }
  return typeof error.code === "string" ? error.code : null;
}
