import {
  normalizeRemoteHistoryQuery,
  type RemoteOrderHistoryDetails,
  type RemoteOrderHistoryPort,
  type RemoteOrderHistoryQuery,
  type RemoteOrderHistorySummary,
} from "@/core/contracts/remote-history";

export const REMOTE_HISTORY_VIEW_PERMISSION =
  "Permissions.PosTerminal.History.View";
export const REMOTE_HISTORY_REPRINT_PERMISSION =
  "Permissions.PosTerminal.History.Reprint";

export const REMOTE_HISTORY_READ_ONLY_CAPABILITIES = Object.freeze({
  refund: false,
  recall: false,
  reprint: false,
});

/**
 * 重打边界只允许传递已验证订单号。门店、设备、打印机和渲染内容必须由组合根绑定，
 * 不能让远程历史 UI 传入 receipt bytes、printer id 或支付 provider 引用。
 */
export type RemoteHistoryReprintPort = Readonly<{
  canReprint(details: RemoteOrderHistoryDetails): boolean;
  reprintExistingOrder(orderGuid: string): Promise<void>;
}>;

export type RemoteHistoryFilters = Readonly<{
  deviceCode: string | null;
  soldFromIso: string;
  soldToIso: string;
  keyword: string | null;
}>;

export type RemoteHistoryDetailsState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{ kind: "loading"; orderGuid: string }>
  | Readonly<{
      kind: "ready";
      orderGuid: string;
      value: RemoteOrderHistoryDetails;
    }>
  | Readonly<{ kind: "not-found"; orderGuid: string }>
  | Readonly<{
      kind: "failed";
      orderGuid: string;
      errorCode: "remote-history-details-failed";
    }>;

export type RemoteHistoryReprintState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{ kind: "unavailable" }>
  | Readonly<{ kind: "submitting"; orderGuid: string }>
  | Readonly<{ kind: "succeeded"; orderGuid: string }>
  | Readonly<{
      kind: "failed";
      orderGuid: string;
      errorCode: "remote-history-reprint-failed";
    }>;

export type RemoteHistoryPresenterState = Readonly<{
  kind:
    | "idle"
    | "loading"
    | "ready"
    | "empty"
    | "failed"
    | "unauthorized"
    | "offline"
    | "unavailable";
  filters: RemoteHistoryFilters;
  rows: readonly RemoteOrderHistorySummary[];
  selectedOrderGuid: string | null;
  details: RemoteHistoryDetailsState;
  reprint: RemoteHistoryReprintState;
  errorCode: "remote-history-load-failed" | null;
}>;

export type RemoteHistoryPresenterOptions = Readonly<{
  port: RemoteOrderHistoryPort | null;
  trustedStoreCode: string;
  currentDeviceCode: string;
  permissionCodes: readonly string[];
  reprintPort?: RemoteHistoryReprintPort | null;
  online: boolean;
  now?: () => Date;
}>;

/** React 无关的只读 presenter；每次筛选和选择都会使旧 generation 失效。 */
export class RemoteHistoryPresenter {
  public state: RemoteHistoryPresenterState;

  private readonly listeners = new Set<() => void>();
  private readonly trustedStoreCode: string;
  private readonly allowed: boolean;
  private readonly canReprint: boolean;
  private online: boolean;
  /** 进入离线前记录的门禁状态，恢复在线后原样还原（数据不被清空）。 */
  private kindBeforeOffline: RemoteHistoryPresenterState["kind"] | null = null;
  private listGeneration = 0;
  private detailsGeneration = 0;
  private reprintGeneration = 0;
  private reprintInFlight: Readonly<{
    orderGuid: string;
    promise: Promise<void>;
  }> | null = null;
  private destroyed = false;

  public constructor(private readonly options: RemoteHistoryPresenterOptions) {
    this.trustedStoreCode = requiredText(
      options.trustedStoreCode,
      "Remote history trusted store",
    );
    requiredText(
      options.currentDeviceCode,
      "Remote history current device",
    );
    this.allowed = hasRemoteHistoryViewPermission(options.permissionCodes);
    this.canReprint = hasRemoteHistoryReprintPermission(options.permissionCodes);
    this.online = options.online;
    const day = localDayRange((options.now ?? (() => new Date()))());
    const filters = toFilters(
      {
        ...day,
        // 默认查看同分店全部终端；可信门店仍由 presenter 和 API 适配器固定。
        deviceCode: null,
        keyword: null,
      },
      this.trustedStoreCode,
    );
    this.state = {
      kind: this.gateKind(),
      filters,
      rows: [],
      selectedOrderGuid: null,
      details: { kind: "idle" },
      reprint: { kind: "idle" },
      errorCode: null,
    };
  }

  public get capabilities() {
    return Object.freeze({
      refund: false,
      recall: false,
      reprint: this.canReprintSelected(),
    });
  }

  public readonly getState = (): RemoteHistoryPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.listGeneration += 1;
    this.detailsGeneration += 1;
    this.reprintGeneration += 1;
    this.listeners.clear();
  }

  public setFilters(filters: RemoteHistoryFilters): void {
    if (this.destroyed) return;
    const normalized = toFilters(filters, this.trustedStoreCode);
    this.listGeneration += 1;
    this.detailsGeneration += 1;
    this.reprintGeneration += 1;
    this.publish({
      kind: this.gateKind(),
      filters: normalized,
      rows: [],
      selectedOrderGuid: null,
      details: { kind: "idle" },
      reprint: { kind: "idle" },
      errorCode: null,
    });
  }

  /**
   * 网络恢复后由路由调用：就地翻转在线状态并重新计算门禁，避免重建 presenter
   * 导致已加载列表丢失与页面闪屏；恢复在线后查询/刷新自动可用。
   * 离线时保留已加载数据并记住离线前状态，恢复后原样还原。
   */
  public setOnline(online: boolean): void {
    if (this.destroyed || this.online === online) return;
    this.online = online;
    if (online) {
      // 恢复在线：还原离线前状态（如 ready/empty），不覆盖已加载列表。
      const restored = this.kindBeforeOffline;
      this.kindBeforeOffline = null;
      if (this.state.kind === "offline") {
        this.publish({
          ...this.state,
          kind: restored ?? this.gateKind(),
        });
      }
    } else if (this.state.kind !== "offline") {
      // 进入离线：记住离线前状态，统一翻转为 offline 门禁。
      this.kindBeforeOffline = this.state.kind;
      this.publish({ ...this.state, kind: "offline" });
    }
  }

  public async refresh(): Promise<void> {
    if (this.destroyed) return;
    const gated = this.gateKind();
    if (gated !== "idle") {
      this.reprintGeneration += 1;
      this.publish({
        ...this.state,
        kind: gated,
        rows: [],
        selectedOrderGuid: null,
        details: { kind: "idle" },
        reprint: { kind: "idle" },
        errorCode: null,
      });
      return;
    }

    const port = this.options.port;
    if (!port) return;
    const generation = ++this.listGeneration;
    ++this.detailsGeneration;
    ++this.reprintGeneration;
    const query = toQuery(this.state.filters, this.trustedStoreCode);
    this.publish({
      ...this.state,
      kind: "loading",
      rows: [],
      selectedOrderGuid: null,
      details: { kind: "idle" },
      reprint: { kind: "idle" },
      errorCode: null,
    });

    try {
      const rows = await port.list(query);
      if (!this.isCurrentList(generation)) return;
      const frozenRows = Object.freeze([...rows]);
      const selectedOrderGuid = frozenRows[0]?.orderGuid ?? null;
      this.publish({
        ...this.state,
        kind: frozenRows.length === 0 ? "empty" : "ready",
        rows: frozenRows,
        selectedOrderGuid,
        details: { kind: "idle" },
        reprint: { kind: "idle" },
        errorCode: null,
      });
      if (selectedOrderGuid !== null) {
        await this.loadDetails(selectedOrderGuid);
      }
    } catch {
      if (!this.isCurrentList(generation)) return;
      this.publish({
        ...this.state,
        kind: "failed",
        rows: [],
        selectedOrderGuid: null,
        details: { kind: "idle" },
        reprint: { kind: "idle" },
        errorCode: "remote-history-load-failed",
      });
    }
  }

  public selectOrder(orderGuid: string): Promise<void> {
    if (
      this.destroyed ||
      !this.state.rows.some((row) => row.orderGuid === orderGuid)
    ) {
      return Promise.resolve();
    }
    if (this.state.selectedOrderGuid !== orderGuid) {
      this.reprintGeneration += 1;
      this.publish({
        ...this.state,
        selectedOrderGuid: orderGuid,
        details: { kind: "idle" },
        reprint: { kind: "idle" },
      });
    }
    return this.loadDetails(orderGuid);
  }

  public reprintSelected(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.reprintInFlight) return this.reprintInFlight.promise;
    const details = this.reprintableDetails();
    const port = this.options.reprintPort;
    if (!details || !port) {
      this.publish({ ...this.state, reprint: { kind: "unavailable" } });
      return Promise.resolve();
    }
    const generation = ++this.reprintGeneration;
    const orderGuid = details.orderGuid;
    this.publish({
      ...this.state,
      reprint: { kind: "submitting", orderGuid },
    });
    const promise = port.reprintExistingOrder(orderGuid)
      .then(() => {
        if (!this.isCurrentReprint(generation, orderGuid)) return;
        // 中文注释：重打是外设副作用，绝不回写、刷新或改造历史订单快照。
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
            errorCode: "remote-history-reprint-failed",
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

  private async loadDetails(orderGuid: string): Promise<void> {
    const port = this.options.port;
    if (
      this.destroyed ||
      !this.allowed ||
      !this.online ||
      !port ||
      this.state.selectedOrderGuid !== orderGuid
    ) {
      return;
    }
    const generation = ++this.detailsGeneration;
    this.publish({
      ...this.state,
      details: { kind: "loading", orderGuid },
    });
    try {
      const details = await port.getDetails(orderGuid);
      if (!this.isCurrentDetails(generation, orderGuid)) return;
      this.publish({
        ...this.state,
        details:
          details === null
            ? { kind: "not-found", orderGuid }
            : { kind: "ready", orderGuid, value: details },
      });
    } catch {
      if (!this.isCurrentDetails(generation, orderGuid)) return;
      this.publish({
        ...this.state,
        details: {
          kind: "failed",
          orderGuid,
          errorCode: "remote-history-details-failed",
        },
      });
    }
  }

  private gateKind(): "idle" | "unauthorized" | "offline" | "unavailable" {
    if (!this.allowed) return "unauthorized";
    if (!this.online) return "offline";
    return this.options.port ? "idle" : "unavailable";
  }

  private isCurrentList(generation: number): boolean {
    return !this.destroyed && generation === this.listGeneration;
  }

  private isCurrentDetails(generation: number, orderGuid: string): boolean {
    return (
      !this.destroyed &&
      generation === this.detailsGeneration &&
      this.state.selectedOrderGuid === orderGuid
    );
  }

  private isCurrentReprint(generation: number, orderGuid: string): boolean {
    return (
      !this.destroyed &&
      generation === this.reprintGeneration &&
      this.reprintableDetails()?.orderGuid === orderGuid
    );
  }

  private canReprintSelected(): boolean {
    return this.reprintableDetails() !== null;
  }

  private reprintableDetails(): RemoteOrderHistoryDetails | null {
    const details = this.state.details;
    if (
      !this.allowed ||
      !this.canReprint ||
      !this.online ||
      !this.options.reprintPort ||
      this.state.kind !== "ready" ||
      details.kind !== "ready" ||
      details.orderGuid !== this.state.selectedOrderGuid ||
      details.value.storeCode.toUpperCase() !== this.trustedStoreCode.toUpperCase() ||
      !this.options.reprintPort.canReprint(details.value)
    ) {
      return null;
    }
    return details.value;
  }

  private publish(state: RemoteHistoryPresenterState): void {
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

export function hasRemoteHistoryViewPermission(
  permissionCodes: readonly string[],
): boolean {
  return permissionCodes.includes(REMOTE_HISTORY_VIEW_PERMISSION);
}

export function hasRemoteHistoryReprintPermission(
  permissionCodes: readonly string[],
): boolean {
  return permissionCodes.includes(REMOTE_HISTORY_REPRINT_PERMISSION);
}

function toFilters(
  value: RemoteHistoryFilters,
  trustedStoreCode: string,
): RemoteHistoryFilters {
  const query = normalizeRemoteHistoryQuery(
    {
      storeCode: trustedStoreCode,
      deviceCode: value.deviceCode,
      soldFromIso: value.soldFromIso,
      soldToIso: value.soldToIso,
      keyword: value.keyword,
      take: 100,
    },
    trustedStoreCode,
  );
  return Object.freeze({
    deviceCode: query.deviceCode,
    soldFromIso: query.soldFromIso,
    soldToIso: query.soldToIso,
    keyword: query.keyword,
  });
}

function toQuery(
  filters: RemoteHistoryFilters,
  trustedStoreCode: string,
): RemoteOrderHistoryQuery {
  return normalizeRemoteHistoryQuery(
    {
      storeCode: trustedStoreCode,
      ...filters,
      take: 100,
    },
    trustedStoreCode,
  );
}

function localDayRange(now: Date): Readonly<{
  soldFromIso: string;
  soldToIso: string;
}> {
  if (!Number.isFinite(now.getTime())) {
    throw new TypeError("Remote history current date is invalid.");
  }
  const start = new Date(
    now.getFullYear(),
    now.getMonth(),
    now.getDate(),
    0,
    0,
    0,
    0,
  );
  const end = new Date(
    now.getFullYear(),
    now.getMonth(),
    now.getDate() + 1,
    0,
    0,
    0,
    0,
  );
  end.setMilliseconds(-1);
  return {
    soldFromIso: start.toISOString(),
    soldToIso: end.toISOString(),
  };
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new TypeError(`${label} is required.`);
  return normalized;
}
