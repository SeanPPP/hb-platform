/** Wave 2.11 的本地订单同步状态；历史页不展示 Draft/Completing 等未完成订单。 */
export type LocalSyncHistoryOrderState =
  | "CompletedLocal"
  | "PendingSync"
  | "Syncing"
  | "Synced"
  | "Blocked403"
  | "Rejected";

export type LocalSyncHistoryOutboxState =
  | "pending"
  | "leased"
  | "succeeded"
  | "blocked403"
  | "rejected";

export type SyncHistoryTenderSummary = Readonly<{
  method: "cash" | "card" | "voucher";
  amountCents: number;
}>;

export type LocalSyncHistoryOutbox = Readonly<{
  state: LocalSyncHistoryOutboxState;
  attemptCount: number;
  lastErrorCode: string | null;
  nextAttemptAtIso: string | null;
}>;

/**
 * 端口只返回历史页确实需要的白名单字段，支付 reference、凭据密文和 receipt bytes 均不跨 feature 边界。
 */
export type LocalSyncHistoryOrder = Readonly<{
  orderGuid: string;
  localSequence: number;
  storeCode: string;
  deviceCode: string;
  soldAtIso: string;
  state: LocalSyncHistoryOrderState;
  totalCents: number;
  discountCents: number;
  actualAmountCents: number;
  tenders: readonly SyncHistoryTenderSummary[];
  outbox: LocalSyncHistoryOutbox | null;
}>;

export type LocalSyncHistoryFilters = Readonly<{
  dateFromIso: string | null;
  dateToIso: string | null;
  states: readonly LocalSyncHistoryOrderState[];
}>;

export type LocalSyncHistoryPageQuery = Readonly<{
  limit: number;
  /** 只接受小于该序号的记录，以 local_sequence 形成稳定的降序翻页。 */
  beforeLocalSequence: number | null;
  filters: LocalSyncHistoryFilters;
}>;

export type LocalSyncHistoryPage = Readonly<{
  orders: readonly LocalSyncHistoryOrder[];
  /** 下一页仍使用严格小于此值的 local_sequence 条件；null 表示没有下一页。 */
  nextBeforeLocalSequence: number | null;
  /** 与当前筛选条件匹配的可恢复 pending outbox 总数，不受当前页限制。 */
  pendingCount: number;
}>;

export type LocalSyncHistorySupportSnapshotQuery = Readonly<{
  /** 支持导出硬上限；仓储必须在同一只读事务内完成计数和有界读取。 */
  limit: number;
  filters: LocalSyncHistoryFilters;
}>;

export type LocalSyncHistorySupportSnapshot = Readonly<{
  orders: readonly LocalSyncHistoryOrder[];
  totalMatchingCount: number;
}>;

export type LocalSyncHistoryRestoreResult = Readonly<{
  restoredOrderGuids: readonly string[];
  skippedOrderGuids: readonly string[];
}>;

export type LocalSyncHistorySupportContext = Readonly<{
  appId: string;
  appVersion: string;
  deviceCode: string;
  storeCode: string;
}>;

/**
 * DB/runtime 适配器的最小边界。
 *
 * `restoreExistingOrderOutboxToPending` 必须是耐久的、受状态保护的更新：仅既有
 * kind=order-sync 且 state=pending 的 outbox 可将 next_attempt_at 调整为可立即消费；
 * 不得删除订单、修改价格/金额、重建 OrderGuid，亦不得解锁 blocked403 或 rejected。
 */
export interface LocalSyncHistoryPort {
  listLocalSyncHistory(query: LocalSyncHistoryPageQuery): Promise<LocalSyncHistoryPage>;
  getLocalSyncHistorySupportSnapshot(
    query: LocalSyncHistorySupportSnapshotQuery,
  ): Promise<LocalSyncHistorySupportSnapshot>;
  restoreExistingOrderOutboxToPending(orderGuids: readonly string[]): Promise<LocalSyncHistoryRestoreResult>;
  getSupportContext(): Promise<LocalSyncHistorySupportContext>;
}

export type SyncHistoryRetransmitGate =
  | Readonly<{ kind: "allowed" }>
  | Readonly<{
      kind: "blocked";
      reason:
        | "synced"
        | "syncing"
        | "reauthentication-required"
        | "supervisor-required"
        | "no-pending-outbox";
    }>;

/** 不在历史页绕过同步、重新认证或主管处置的状态机。 */
export function retransmitGate(order: LocalSyncHistoryOrder): SyncHistoryRetransmitGate {
  if (order.state === "Synced" || order.outbox?.state === "succeeded") {
    return { kind: "blocked", reason: "synced" };
  }
  if (order.state === "Syncing" || order.outbox?.state === "leased") {
    return { kind: "blocked", reason: "syncing" };
  }
  if (order.state === "Blocked403" || order.outbox?.state === "blocked403") {
    return { kind: "blocked", reason: "reauthentication-required" };
  }
  if (order.state === "Rejected" || order.outbox?.state === "rejected") {
    return { kind: "blocked", reason: "supervisor-required" };
  }
  if ((order.state === "CompletedLocal" || order.state === "PendingSync") && order.outbox?.state === "pending") {
    return { kind: "allowed" };
  }
  return { kind: "blocked", reason: "no-pending-outbox" };
}

export type SyncHistorySupportExport = Readonly<{
  format: "hb-pos-sync-history-v1";
  app: Readonly<{ id: string; version: string }>;
  device: Readonly<{ code: string }>;
  store: Readonly<{ code: string }>;
  snapshot: Readonly<{
    createdAtIso: string;
    filters: LocalSyncHistoryFilters;
    exportedCount: number;
    totalMatchingCount: number;
    truncated: boolean;
  }>;
  orders: readonly Readonly<{
    orderGuid: string;
    localSequence: number;
    storeCode: string;
    deviceCode: string;
    soldAtUtcDate: string | null;
    state: LocalSyncHistoryOrderState;
    totalCents: number;
    discountCents: number;
    actualAmountCents: number;
    tenders: readonly SyncHistoryTenderSummary[];
    outbox: Readonly<{
      state: LocalSyncHistoryOutboxState;
      attemptCount: number;
      lastErrorCode: string | null;
      nextAttemptAtIso: string | null;
    }> | null;
  }>[];
}>;

/**
 * 纯白名单映射。故意不展开 source object，避免新增未知字段时意外导出卡号、券码或回执。
 */
export function buildSyncHistorySupportExport(
  context: LocalSyncHistorySupportContext,
  orders: readonly LocalSyncHistoryOrder[],
  snapshot: SyncHistorySupportExport["snapshot"],
): SyncHistorySupportExport {
  const orderAlias = createStableAliasResolver("order", true);
  const storeAlias = createStableAliasResolver("store");
  const deviceAlias = createStableAliasResolver("device");

  return {
    format: "hb-pos-sync-history-v1",
    app: { id: context.appId, version: context.appVersion },
    device: { code: deviceAlias(context.deviceCode) },
    store: { code: storeAlias(context.storeCode) },
    snapshot: {
      createdAtIso: snapshot.createdAtIso,
      filters: {
        dateFromIso: snapshot.filters.dateFromIso,
        dateToIso: snapshot.filters.dateToIso,
        states: [...snapshot.filters.states],
      },
      exportedCount: snapshot.exportedCount,
      totalMatchingCount: snapshot.totalMatchingCount,
      truncated: snapshot.truncated,
    },
    orders: orders.map((order) => ({
      orderGuid: orderAlias(order.orderGuid),
      localSequence: order.localSequence,
      storeCode: storeAlias(order.storeCode),
      deviceCode: deviceAlias(order.deviceCode),
      soldAtUtcDate: utcDateOnly(order.soldAtIso),
      state: order.state,
      totalCents: order.totalCents,
      discountCents: order.discountCents,
      actualAmountCents: order.actualAmountCents,
      tenders: order.tenders.map((tender) => ({
        method: tender.method,
        amountCents: tender.amountCents,
      })),
      outbox: order.outbox
        ? {
            state: order.outbox.state,
            attemptCount: order.outbox.attemptCount,
            lastErrorCode: safeErrorCode(order.outbox.lastErrorCode),
            nextAttemptAtIso: order.outbox.nextAttemptAtIso,
          }
        : null,
    })),
  };
}

function createStableAliasResolver(prefix: string, alwaysNumber = false) {
  const aliases = new Map<string, string>();
  return (value: string): string => {
    const existing = aliases.get(value);
    if (existing) return existing;
    const position = aliases.size + 1;
    const alias =
      alwaysNumber || position > 1
        ? `${prefix}-${String(position).padStart(4, "0")}`
        : prefix;
    aliases.set(value, alias);
    return alias;
  };
}

function utcDateOnly(value: string): string | null {
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp)
    ? new Date(timestamp).toISOString().slice(0, 10)
    : null;
}

export function serializeSyncHistorySupportExport(value: SyncHistorySupportExport): string {
  return JSON.stringify(value);
}

export function safeSyncHistoryErrorCode(value: string | null): string | null {
  return safeErrorCode(value);
}

function safeErrorCode(value: string | null): string | null {
  if (!value || !/^[A-Z][A-Z0-9_]{0,63}$/.test(value)) return null;
  // 中文注释：保留 VOUCHER/CARD 等符号化错误码；长数字串不是错误码，可能是 PAN 或券码。
  return /\d{6,}/.test(value) ? null : value;
}
