import type { SqliteConnectionPort, SqlValue } from "@hb/pos-db/core/db/types";

import type {
  LocalSyncHistoryFilters,
  LocalSyncHistoryOrder,
  LocalSyncHistoryOrderState,
  LocalSyncHistoryOutbox,
  LocalSyncHistoryOutboxState,
  LocalSyncHistoryPage,
  LocalSyncHistoryPageQuery,
  LocalSyncHistoryPort,
  LocalSyncHistoryRestoreResult,
  LocalSyncHistorySupportSnapshot,
  LocalSyncHistorySupportSnapshotQuery,
  LocalSyncHistorySupportContext,
  SyncHistoryTenderSummary,
} from "@/features/sync-history";

const visibleOrderStates = [
  "CompletedLocal",
  "PendingSync",
  "Syncing",
  "Synced",
  "Blocked403",
  "Rejected",
] as const satisfies readonly LocalSyncHistoryOrderState[];

const visibleOrderStateSql =
  "('CompletedLocal', 'PendingSync', 'Syncing', 'Synced', 'Blocked403', 'Rejected')";

type HistoryOrderRow = Readonly<{
  order_guid: unknown;
  local_sequence: unknown;
  store_code: unknown;
  device_code: unknown;
  sold_at_iso: unknown;
  state: unknown;
  total_cents: unknown;
  discount_cents: unknown;
  actual_amount_cents: unknown;
  outbox_state: unknown;
  outbox_attempt_count: unknown;
  outbox_last_error_code: unknown;
  outbox_next_attempt_at_iso: unknown;
}>;

type HistoryTenderRow = Readonly<{
  order_guid: unknown;
  method: unknown;
  amount_cents: unknown;
}>;

type CountRow = Readonly<{ pending_count: unknown }>;
type MatchingCountRow = Readonly<{ matching_count: unknown }>;

type FilterSql = Readonly<{
  where: string;
  parameters: readonly SqlValue[];
}>;

/**
 * 同步历史只暴露白名单摘要；context 由 runtime 明确注入，不读取 Keychain 或设备凭据。
 */
export class SqliteLocalSyncHistoryStore implements LocalSyncHistoryPort {
  private readonly supportContext: LocalSyncHistorySupportContext;

  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly nowIso: () => string,
    supportContext: LocalSyncHistorySupportContext,
  ) {
    this.supportContext = validateSupportContext(supportContext);
  }

  public listLocalSyncHistory(
    query: LocalSyncHistoryPageQuery,
  ): Promise<LocalSyncHistoryPage> {
    const normalized = normalizeQuery(query);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const pageFilter = buildFilterSql(
        normalized.filters,
        normalized.beforeLocalSequence,
      );
      const rows = await transaction.getAll<HistoryOrderRow>(
        `SELECT
          o.order_guid,
          o.local_sequence,
          o.store_code,
          o.device_code,
          o.sold_at_iso,
          o.state,
          o.total_cents,
          o.discount_cents,
          o.actual_amount_cents,
          ob.state AS outbox_state,
          ob.attempt_count AS outbox_attempt_count,
          ob.last_error_code AS outbox_last_error_code,
          ob.next_attempt_at_iso AS outbox_next_attempt_at_iso
         FROM local_orders o
         LEFT JOIN outbox_messages ob
           ON ob.aggregate_id = o.order_guid
          AND ob.kind = 'order-sync'
         WHERE ${pageFilter.where}
         ORDER BY o.local_sequence DESC
         LIMIT ?`,
        [...pageFilter.parameters, normalized.limit + 1],
      );

      const hasMore = rows.length > normalized.limit;
      const pageRows = rows.slice(0, normalized.limit);
      const tenderMap = await loadTenderSummaries(
        transaction,
        pageRows.map((row) => text(row.order_guid, "order_guid")),
      );
      const orders = pageRows.map((row) =>
        mapHistoryOrder(row, tenderMap.get(text(row.order_guid, "order_guid")) ?? []),
      );

      const countFilter = buildFilterSql(normalized.filters, null);
      const count = await transaction.getFirst<CountRow>(
        `SELECT COUNT(*) AS pending_count
         FROM local_orders o
         INNER JOIN outbox_messages ob
           ON ob.aggregate_id = o.order_guid
          AND ob.kind = 'order-sync'
          AND ob.state = 'pending'
         WHERE ${countFilter.where}
           AND o.state IN ('CompletedLocal', 'PendingSync')`,
        countFilter.parameters,
      );
      const pendingCount = nonNegativeInteger(
        count?.pending_count ?? 0,
        "pending_count",
      );
      return {
        orders,
        nextBeforeLocalSequence: hasMore
          ? orders.at(-1)?.localSequence ?? null
          : null,
        pendingCount,
      };
    });
  }

  public getLocalSyncHistorySupportSnapshot(
    query: LocalSyncHistorySupportSnapshotQuery,
  ): Promise<LocalSyncHistorySupportSnapshot> {
    const normalized = normalizeSupportSnapshotQuery(query);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const filter = buildFilterSql(normalized.filters, null);
      const count = await transaction.getFirst<MatchingCountRow>(
        `SELECT COUNT(*) AS matching_count
         FROM local_orders o
         WHERE ${filter.where}`,
        filter.parameters,
      );
      const totalMatchingCount = nonNegativeInteger(
        count?.matching_count ?? 0,
        "matching_count",
      );
      const rows = await transaction.getAll<HistoryOrderRow>(
        `SELECT
          o.order_guid,
          o.local_sequence,
          o.store_code,
          o.device_code,
          o.sold_at_iso,
          o.state,
          o.total_cents,
          o.discount_cents,
          o.actual_amount_cents,
          ob.state AS outbox_state,
          ob.attempt_count AS outbox_attempt_count,
          ob.last_error_code AS outbox_last_error_code,
          ob.next_attempt_at_iso AS outbox_next_attempt_at_iso
         FROM local_orders o
         LEFT JOIN outbox_messages ob
           ON ob.aggregate_id = o.order_guid
          AND ob.kind = 'order-sync'
         WHERE ${filter.where}
         ORDER BY o.local_sequence DESC
         LIMIT ?`,
        [...filter.parameters, normalized.limit],
      );
      const tenderMap = await loadTenderSummaries(
        transaction,
        rows.map((row) => text(row.order_guid, "order_guid")),
      );
      return {
        orders: rows.map((row) =>
          mapHistoryOrder(
            row,
            tenderMap.get(text(row.order_guid, "order_guid")) ?? [],
          ),
        ),
        totalMatchingCount,
      };
    });
  }

  public restoreExistingOrderOutboxToPending(
    orderGuids: readonly string[],
  ): Promise<LocalSyncHistoryRestoreResult> {
    const uniqueOrderGuids = [...new Set(orderGuids)];
    for (const orderGuid of uniqueOrderGuids) {
      if (!orderGuid.trim()) throw new TypeError("orderGuid is required.");
    }

    return this.connection.withExclusiveTransaction(async (transaction) => {
      const restoredOrderGuids: string[] = [];
      const skippedOrderGuids: string[] = [];
      const now = requireIso(this.nowIso(), "nowIso");
      for (const orderGuid of uniqueOrderGuids) {
        const changed = await transaction.run(
          `UPDATE outbox_messages
           SET next_attempt_at_iso = ?,
               last_error_code = NULL,
               updated_at_iso = ?
           WHERE aggregate_id = ?
             AND kind = 'order-sync'
             AND state = 'pending'
             AND EXISTS (
               SELECT 1
               FROM local_orders o
               WHERE o.order_guid = outbox_messages.aggregate_id
                 AND o.state IN ('CompletedLocal', 'PendingSync')
             )`,
          [now, now, orderGuid],
        );
        if (changed.changes === 1) restoredOrderGuids.push(orderGuid);
        else if (changed.changes === 0) skippedOrderGuids.push(orderGuid);
        else throw new Error("Order sync restore CAS changed multiple rows.");
      }
      return { restoredOrderGuids, skippedOrderGuids };
    });
  }

  public async getSupportContext(): Promise<LocalSyncHistorySupportContext> {
    return { ...this.supportContext };
  }
}

async function loadTenderSummaries(
  connection: SqliteConnectionPort,
  orderGuids: readonly string[],
): Promise<ReadonlyMap<string, readonly SyncHistoryTenderSummary[]>> {
  if (!orderGuids.length) return new Map();
  const result = new Map<string, SyncHistoryTenderSummary[]>();
  // SQLite 默认变量上限可能仅 999；固定分块避免 10,000 条支持快照触发超限。
  for (let offset = 0; offset < orderGuids.length; offset += 200) {
    const chunk = orderGuids.slice(offset, offset + 200);
    const placeholders = chunk.map(() => "?").join(", ");
    const rows = await connection.getAll<HistoryTenderRow>(
      `SELECT order_guid, method, amount_cents
       FROM order_tenders
       WHERE order_guid IN (${placeholders})
       ORDER BY order_guid ASC, created_at_iso ASC, tender_guid ASC`,
      chunk,
    );
    for (const row of rows) {
      const orderGuid = text(row.order_guid, "tender order_guid");
      const list = result.get(orderGuid) ?? [];
      list.push({
        method: tenderMethod(row.method),
        amountCents: integer(row.amount_cents, "tender amount_cents"),
      });
      result.set(orderGuid, list);
    }
  }
  return result;
}

function mapHistoryOrder(
  row: HistoryOrderRow,
  tenders: readonly SyncHistoryTenderSummary[],
): LocalSyncHistoryOrder {
  return {
    orderGuid: text(row.order_guid, "order_guid"),
    localSequence: positiveInteger(row.local_sequence, "local_sequence"),
    storeCode: text(row.store_code, "store_code"),
    deviceCode: text(row.device_code, "device_code"),
    soldAtIso: requireIso(text(row.sold_at_iso, "sold_at_iso"), "sold_at_iso"),
    state: historyOrderState(row.state),
    totalCents: integer(row.total_cents, "total_cents"),
    discountCents: integer(row.discount_cents, "discount_cents"),
    actualAmountCents: integer(row.actual_amount_cents, "actual_amount_cents"),
    tenders,
    outbox: mapOutbox(row),
  };
}

function mapOutbox(row: HistoryOrderRow): LocalSyncHistoryOutbox | null {
  if (row.outbox_state === null || row.outbox_state === undefined) return null;
  const nextAttemptAtIso =
    row.outbox_next_attempt_at_iso === null ||
    row.outbox_next_attempt_at_iso === undefined
      ? null
      : requireIso(
        text(row.outbox_next_attempt_at_iso, "next_attempt_at_iso"),
        "next_attempt_at_iso",
      );
  return {
    state: outboxState(row.outbox_state),
    attemptCount: nonNegativeInteger(
      row.outbox_attempt_count,
      "outbox attempt_count",
    ),
    lastErrorCode: safeErrorCode(row.outbox_last_error_code),
    nextAttemptAtIso,
  };
}

function normalizeQuery(query: LocalSyncHistoryPageQuery): LocalSyncHistoryPageQuery {
  if (!Number.isSafeInteger(query.limit) || query.limit < 1 || query.limit > 200) {
    throw new TypeError("Sync history limit must be between 1 and 200.");
  }
  if (
    query.beforeLocalSequence !== null &&
    (!Number.isSafeInteger(query.beforeLocalSequence) ||
      query.beforeLocalSequence <= 0)
  ) {
    throw new TypeError("beforeLocalSequence must be a positive integer.");
  }
  const filters = normalizeFilters(query.filters);
  return {
    limit: query.limit,
    beforeLocalSequence: query.beforeLocalSequence,
    filters,
  };
}

function normalizeSupportSnapshotQuery(
  query: LocalSyncHistorySupportSnapshotQuery,
): LocalSyncHistorySupportSnapshotQuery {
  if (
    !Number.isSafeInteger(query.limit) ||
    query.limit < 1 ||
    query.limit > 10_000
  ) {
    throw new TypeError(
      "Sync history support snapshot limit must be between 1 and 10000.",
    );
  }
  return {
    limit: query.limit,
    filters: normalizeFilters(query.filters),
  };
}

function normalizeFilters(
  filters: LocalSyncHistoryFilters,
): LocalSyncHistoryFilters {
  const dateFromIso = filters.dateFromIso
    ? requireIso(filters.dateFromIso, "dateFromIso")
    : null;
  const dateToIso = filters.dateToIso
    ? requireIso(filters.dateToIso, "dateToIso")
    : null;
  if (
    dateFromIso &&
    dateToIso &&
    Date.parse(dateFromIso) > Date.parse(dateToIso)
  ) {
    throw new TypeError("Sync history date range is reversed.");
  }
  const states = [...new Set(filters.states)];
  for (const state of states) {
    if (!visibleOrderStates.includes(state)) {
      throw new TypeError(`Unsupported sync history state: ${String(state)}`);
    }
  }
  return { dateFromIso, dateToIso, states };
}

function buildFilterSql(
  filters: LocalSyncHistoryFilters,
  beforeLocalSequence: number | null,
): FilterSql {
  const clauses = [`o.state IN ${visibleOrderStateSql}`];
  const parameters: SqlValue[] = [];
  if (beforeLocalSequence !== null) {
    clauses.push("o.local_sequence < ?");
    parameters.push(beforeLocalSequence);
  }
  if (filters.dateFromIso !== null) {
    clauses.push("o.sold_at_iso >= ?");
    parameters.push(filters.dateFromIso);
  }
  if (filters.dateToIso !== null) {
    clauses.push("o.sold_at_iso <= ?");
    parameters.push(filters.dateToIso);
  }
  if (filters.states.length) {
    clauses.push(`o.state IN (${filters.states.map(() => "?").join(", ")})`);
    parameters.push(...filters.states);
  }
  return { where: clauses.join(" AND "), parameters };
}

function validateSupportContext(
  value: LocalSyncHistorySupportContext,
): LocalSyncHistorySupportContext {
  return {
    appId: safeContextText(value.appId, "appId"),
    appVersion: safeContextText(value.appVersion, "appVersion"),
    deviceCode: safeContextText(value.deviceCode, "deviceCode"),
    storeCode: safeContextText(value.storeCode, "storeCode"),
  };
}

function safeContextText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized || normalized.length > 128 || /[\u0000-\u001f\u007f]/.test(normalized)) {
    throw new TypeError(`Invalid sync history support ${label}.`);
  }
  return normalized;
}

function safeErrorCode(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") return null;
  if (!/^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$/.test(value)) return null;
  return /authorization|token|pan|voucher|card|secret|reference|receipt/i.test(value)
    ? null
    : value;
}

function historyOrderState(value: unknown): LocalSyncHistoryOrderState {
  const state = text(value, "history state");
  if (visibleOrderStates.includes(state as LocalSyncHistoryOrderState)) {
    return state as LocalSyncHistoryOrderState;
  }
  throw new Error("Invalid persisted sync history order state.");
}

function outboxState(value: unknown): LocalSyncHistoryOutboxState {
  const state = text(value, "outbox state");
  if (
    state === "pending" ||
    state === "leased" ||
    state === "succeeded" ||
    state === "blocked403" ||
    state === "rejected"
  ) {
    return state;
  }
  throw new Error("Invalid persisted order-sync outbox state.");
}

function tenderMethod(value: unknown): SyncHistoryTenderSummary["method"] {
  const method = text(value, "tender method");
  if (method === "cash" || method === "card" || method === "voucher") {
    return method;
  }
  throw new Error("Invalid persisted tender method.");
}

function requireIso(value: string, label: string): string {
  if (!value.trim() || !Number.isFinite(Date.parse(value))) {
    throw new TypeError(`Invalid ${label}.`);
  }
  return value;
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`Invalid persisted ${label}.`);
  }
  return value;
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error(`Invalid persisted ${label}.`);
  }
  return parsed;
}

function positiveInteger(value: unknown, label: string): number {
  const parsed = integer(value, label);
  if (parsed <= 0) throw new Error(`Invalid persisted ${label}.`);
  return parsed;
}

function nonNegativeInteger(value: unknown, label: string): number {
  const parsed = integer(value, label);
  if (parsed < 0) throw new Error(`Invalid persisted ${label}.`);
  return parsed;
}
