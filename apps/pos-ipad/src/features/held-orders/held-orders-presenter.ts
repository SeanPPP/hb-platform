import type {
  HeldOrderActionResult,
  SharedHeldOrderLocalShareRow,
  SharedHeldOrderRemoteRow,
  SharedHeldOrdersViewPort,
  SharedHeldOrderTakeViewResult,
} from "./held-orders-domain";
import { HeldOrdersOrchestrator } from "./held-orders-orchestrator";

import type { HeldOrderSummary } from "@/core/contracts";

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
}>;

export type HeldOrdersPresenterState = Readonly<{
  kind: "loading" | "ready" | "unauthorized" | "failed";
  rows: readonly HeldOrderViewRow[];
  busy: boolean;
  lastAction: HeldOrderActionResult | null;
  /** 非阻塞共享同步错误（本地行仍然保留），机器码由屏幕映射文案。 */
  refreshError: string | null;
  sharedEnabled: boolean;
}>;

/** React 无关 presenter：只叠加共享数据源/动作，绝不改变旧 hold/recall 语义。 */
export class HeldOrdersPresenter {
  public state: HeldOrdersPresenterState = {
    kind: "loading",
    rows: [],
    busy: false,
    lastAction: null,
    refreshError: null,
    sharedEnabled: false,
  };

  private readonly listeners = new Set<() => void>();
  private refreshInFlight: Promise<void> | null = null;
  private actionInFlight: Promise<HeldOrderActionResult> | null = null;
  private destroyed = false;
  private sharedOrders: SharedHeldOrdersViewPort | null = null;
  private autoRefreshTimer: ReturnType<typeof setInterval> | null = null;

  public constructor(private readonly orchestrator: HeldOrdersOrchestrator) {}

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
    this.stopAutoRefresh();
    this.sharedOrders = null;
    this.listeners.clear();
  }

  public refresh(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.refreshInFlight) return this.refreshInFlight;
    this.patch({ kind: "loading", lastAction: null });
    const operation = (async () => {
      if (!this.sharedOrders) {
        await this.refreshLocalOnly();
        return;
      }
      await this.refreshWithShared(this.sharedOrders);
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
    return this.runAction(() => this.orchestrator.release(holdId));
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

  private async refreshLocalOnly(): Promise<void> {
    try {
      const rows = await this.orchestrator.list();
      if (this.destroyed) return;
      this.patch({ kind: "ready", rows: toLocalViewRows(rows) });
    } catch (error: unknown) {
      if (this.destroyed) return;
      this.patch({
        kind:
          error instanceof Error && error.message === "HELD_ORDER_LIST_UNAUTHORIZED"
            ? "unauthorized"
            : "failed",
        rows: [],
      });
    }
  }

  private async refreshWithShared(
    shared: SharedHeldOrdersViewPort,
  ): Promise<void> {
    const localPromise = this.orchestrator.list();
    let remoteRows: readonly SharedHeldOrderRemoteRow[] = [];
    let shareRows: readonly SharedHeldOrderLocalShareRow[] = [];
    let refreshError: string | null = null;
    const [remoteResult, shareResult] = await Promise.allSettled([
      shared.listRemotePending(),
      shared.listLocalShareState
        ? shared.listLocalShareState()
        : Promise.resolve([] as readonly SharedHeldOrderLocalShareRow[]),
    ]);
    if (remoteResult.status === "fulfilled") {
      remoteRows = remoteResult.value;
    } else {
      refreshError = "SHARED_HELD_ORDERS_SYNC_FAILED";
    }
    if (shareResult.status === "fulfilled") {
      shareRows = shareResult.value;
    } else {
      refreshError = "SHARED_HELD_ORDERS_SYNC_FAILED";
    }
    try {
      const localRows = await localPromise;
      if (this.destroyed) return;
      this.patch({
        kind: "ready",
        rows: mergeHeldOrderRows(localRows, remoteRows, shareRows),
        refreshError,
      });
    } catch (error: unknown) {
      if (this.destroyed) return;
      // 本地加密账本失败保持旧 fail-closed 语义，共享失败不覆盖本地事实。
      this.patch({
        kind:
          error instanceof Error && error.message === "HELD_ORDER_LIST_UNAUTHORIZED"
            ? "unauthorized"
            : "failed",
        rows: [],
        refreshError: null,
      });
    }
  }

  private async takeRemoteOnce(holdGuid: string): Promise<HeldOrderActionResult> {
    const shared = this.sharedOrders;
    if (!shared) return { ok: false, code: "shared-not-available" };
    try {
      return mapSharedTake(await shared.takeRemoteHold(holdGuid));
    } catch {
      return { ok: false, code: "shared-conflict", holdId: holdGuid };
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
}

function shouldRefreshAfterAction(result: HeldOrderActionResult): boolean {
  return (
    result.ok ||
    result.code === "hold-committed-cart-not-cleared" ||
    result.code === "hold-fence-not-cleared" ||
    result.code === "restore-failed" ||
    result.code === "rollback-failed" ||
    result.code === "release-failed" ||
    result.code === "shared-prepared-awaiting-activation" ||
    result.code === "shared-fence-held" ||
    result.code === "shared-conflict" ||
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
    const status = local.status === "Recalling"
      ? "claiming-here"
      : share?.shareState === "Blocked"
        ? "blocked"
        : share?.shareState === "Published" || remote
          ? "published-shareable"
          : share?.shareState === "NeedsEvaluation" ||
              share?.shareState === "PendingPublish"
            ? "local-pending-publish"
            : "local-pending";
    rows.set(local.holdId, {
      holdId: local.holdId,
      local,
      remote,
      status,
      blockReason: status === "blocked" ? (share?.blockReason ?? null) : null,
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
      });
    }
  }
  return [...rows.values()].sort((left, right) => {
    const leftMs = rowHeldAtMs(left);
    const rightMs = rowHeldAtMs(right);
    return rightMs - leftMs || left.holdId.localeCompare(right.holdId);
  });
}

function rowHeldAtMs(row: HeldOrderViewRow): number {
  const iso = row.local?.heldAtIso ?? row.remote?.heldAtIso ?? "";
  const parsed = Date.parse(iso);
  return Number.isFinite(parsed) ? parsed : 0;
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
