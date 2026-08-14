import type { SqliteConnectionPort, SqlValue } from "./types";

import {
  normalizeLocalHistoryQuery,
  type LocalHistoryDetails,
  type LocalHistoryLine,
  type LocalHistoryOrderState,
  type LocalHistoryPage,
  type LocalHistoryPort,
  type LocalHistoryQuery,
  type LocalHistorySummary,
  type LocalHistoryTenderSummary,
} from "@/features/local-history/local-history-domain";

export type LocalHistoryStoreScope = Readonly<{
  storeCode: string;
  deviceCode: string;
}>;

const visibleStates = [
  "CompletedLocal",
  "PendingSync",
  "Syncing",
  "Synced",
  "Blocked403",
  "Rejected",
] as const satisfies readonly LocalHistoryOrderState[];

const visibleStateSql =
  "('CompletedLocal', 'PendingSync', 'Syncing', 'Synced', 'Blocked403', 'Rejected')";

type SummaryRow = Readonly<{
  order_guid: unknown;
  local_sequence: unknown;
  sold_at_iso: unknown;
  cashier_name: unknown;
  state: unknown;
  total_cents: unknown;
  discount_cents: unknown;
  actual_amount_cents: unknown;
  line_count: unknown;
}>;

type LineRow = Readonly<{
  line_id: unknown;
  product_code: unknown;
  item_number: unknown;
  lookup_code: unknown;
  display_name: unknown;
  quantity: unknown;
  unit_price_cents: unknown;
  discount_cents: unknown;
  actual_amount_cents: unknown;
  line_kind: unknown;
}>;

type TenderRow = Readonly<{
  order_guid?: unknown;
  method: unknown;
  amount_cents: unknown;
}>;

export class SqliteLocalHistoryStore implements LocalHistoryPort {
  private readonly scope: LocalHistoryStoreScope;

  public constructor(
    private readonly connection: SqliteConnectionPort,
    scope: LocalHistoryStoreScope,
  ) {
    this.scope = Object.freeze({
      storeCode: requiredText(scope.storeCode, "local history store code"),
      deviceCode: requiredText(scope.deviceCode, "local history device code"),
    });
  }

  public async list(query: LocalHistoryQuery): Promise<LocalHistoryPage> {
    const normalized = normalizeLocalHistoryQuery(query);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const filter = buildListFilter(normalized, this.scope);
      const rows = await transaction.getAll<SummaryRow>(
        `SELECT
          o.order_guid,
          o.local_sequence,
          o.sold_at_iso,
          o.cashier_name,
          o.state,
          o.total_cents,
          o.discount_cents,
          o.actual_amount_cents,
          (
            SELECT COUNT(*)
            FROM local_order_lines counted
            WHERE counted.order_guid = o.order_guid
          ) AS line_count
         FROM local_orders o
         WHERE ${filter.where}
         ORDER BY o.local_sequence DESC
         LIMIT ?`,
        [...filter.parameters, normalized.limit + 1],
      );
      const hasMore = rows.length > normalized.limit;
      const pageRows = rows.slice(0, normalized.limit);
      const orderGuids = pageRows.map((row) =>
        requiredText(row.order_guid, "persisted local history order guid"),
      );
      const tenders = await loadTenderSummaries(
        transaction,
        this.scope,
        orderGuids,
      );
      const orders = pageRows.map((row) =>
        mapSummary(
          row,
          tenders.get(
            requiredText(
              row.order_guid,
              "persisted local history order guid",
            ),
          ) ?? [],
        ),
      );
      return Object.freeze({
        orders: Object.freeze(orders),
        nextCursor: hasMore
          ? orders.at(-1)?.localSequence ?? null
          : null,
      });
    });
  }

  public async getDetails(
    orderGuid: string,
  ): Promise<LocalHistoryDetails | null> {
    const normalizedOrderGuid = requiredText(
      orderGuid,
      "local history order guid",
    );
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const parameters = [
        normalizedOrderGuid,
        this.scope.storeCode,
        this.scope.deviceCode,
      ] as const;
      const header = await transaction.getFirst<SummaryRow>(
        `SELECT
          o.order_guid,
          o.local_sequence,
          o.sold_at_iso,
          o.cashier_name,
          o.state,
          o.total_cents,
          o.discount_cents,
          o.actual_amount_cents,
          (
            SELECT COUNT(*)
            FROM local_order_lines counted
            WHERE counted.order_guid = o.order_guid
          ) AS line_count
         FROM local_orders o
         WHERE o.order_guid = ?
           AND o.store_code = ?
           AND o.device_code = ?
           AND o.state IN ${visibleStateSql}`,
        parameters,
      );
      if (!header) return null;

      const lines = await transaction.getAll<LineRow>(
        `SELECT
          line.line_id,
          line.product_code,
          line.item_number,
          line.lookup_code,
          line.display_name,
          line.quantity,
          line.unit_price_cents,
          line.discount_cents,
          line.actual_amount_cents,
          line.line_kind
         FROM local_order_lines line
         INNER JOIN local_orders o
           ON o.order_guid = line.order_guid
         WHERE line.order_guid = ?
           AND o.store_code = ?
           AND o.device_code = ?
           AND o.state IN ${visibleStateSql}
         ORDER BY line.line_sequence ASC`,
        parameters,
      );
      const tenderRows = await transaction.getAll<TenderRow>(
        `SELECT
          tender.method,
          tender.amount_cents
         FROM order_tenders tender
         INNER JOIN local_orders o
           ON o.order_guid = tender.order_guid
         WHERE tender.order_guid = ?
           AND o.store_code = ?
           AND o.device_code = ?
           AND o.state IN ${visibleStateSql}
         ORDER BY tender.created_at_iso ASC, tender.tender_guid ASC`,
        parameters,
      );
      return mapDetails(header, lines, tenderRows);
    });
  }
}

function buildListFilter(
  query: LocalHistoryQuery,
  scope: LocalHistoryStoreScope,
): Readonly<{ where: string; parameters: readonly SqlValue[] }> {
  const clauses = [
    "o.store_code = ?",
    "o.device_code = ?",
    `o.state IN ${visibleStateSql}`,
    "o.sold_at_iso >= ?",
    "o.sold_at_iso <= ?",
  ];
  const parameters: SqlValue[] = [
    scope.storeCode,
    scope.deviceCode,
    query.soldFromIso,
    query.soldToIso,
  ];
  if (query.cursor !== null) {
    clauses.push("o.local_sequence < ?");
    parameters.push(query.cursor);
  }
  if (query.keyword !== null) {
    const pattern = `%${escapeLike(query.keyword)}%`;
    const normalizedOrderPattern =
      `%${escapeLike(query.keyword.replaceAll("-", ""))}%`;
    clauses.push(
      `(o.order_guid LIKE ? ESCAPE '\\' COLLATE NOCASE
        OR REPLACE(o.order_guid, '-', '') LIKE ? ESCAPE '\\' COLLATE NOCASE
        OR CAST(o.local_sequence AS TEXT) LIKE ? ESCAPE '\\'
        OR EXISTS (
          SELECT 1
          FROM local_order_lines search
          WHERE search.order_guid = o.order_guid
            AND (
              search.product_code LIKE ? ESCAPE '\\' COLLATE NOCASE
              OR COALESCE(search.item_number, '') LIKE ? ESCAPE '\\' COLLATE NOCASE
              OR search.lookup_code LIKE ? ESCAPE '\\' COLLATE NOCASE
              OR search.display_name LIKE ? ESCAPE '\\' COLLATE NOCASE
            )
        ))`,
    );
    parameters.push(
      pattern,
      normalizedOrderPattern,
      pattern,
      pattern,
      pattern,
      pattern,
      pattern,
    );
  }
  return { where: clauses.join(" AND "), parameters };
}

async function loadTenderSummaries(
  connection: SqliteConnectionPort,
  scope: LocalHistoryStoreScope,
  orderGuids: readonly string[],
): Promise<ReadonlyMap<string, readonly LocalHistoryTenderSummary[]>> {
  if (!orderGuids.length) return new Map();
  const placeholders = orderGuids.map(() => "?").join(", ");
  const rows = await connection.getAll<TenderRow>(
    `SELECT
      tender.order_guid,
      tender.method,
      tender.amount_cents
     FROM order_tenders tender
     INNER JOIN local_orders o
       ON o.order_guid = tender.order_guid
     WHERE o.store_code = ?
       AND o.device_code = ?
       AND o.state IN ${visibleStateSql}
       AND tender.order_guid IN (${placeholders})
     ORDER BY tender.order_guid ASC,
       tender.created_at_iso ASC,
       tender.tender_guid ASC`,
    [scope.storeCode, scope.deviceCode, ...orderGuids],
  );
  const result = new Map<string, LocalHistoryTenderSummary[]>();
  for (const row of rows) {
    const orderGuid = requiredText(
      row.order_guid,
      "persisted local history tender order guid",
    );
    const current = result.get(orderGuid) ?? [];
    current.push(mapTender(row));
    result.set(orderGuid, current);
  }
  return result;
}

function mapSummary(
  row: SummaryRow,
  tenders: readonly LocalHistoryTenderSummary[],
): LocalHistorySummary {
  return Object.freeze({
    orderGuid: requiredText(
      row.order_guid,
      "persisted local history order guid",
    ),
    localSequence: positiveInteger(
      row.local_sequence,
      "persisted local history sequence",
    ),
    soldAtIso: persistedIso(
      row.sold_at_iso,
      "persisted local history soldAtIso",
    ),
    cashierName: requiredText(
      row.cashier_name,
      "persisted local history cashier name",
      512,
    ),
    state: localHistoryState(row.state),
    totalCents: integer(row.total_cents, "persisted local history total"),
    discountCents: integer(
      row.discount_cents,
      "persisted local history discount",
    ),
    actualAmountCents: integer(
      row.actual_amount_cents,
      "persisted local history actual amount",
    ),
    lineCount: nonNegativeInteger(
      row.line_count,
      "persisted local history line count",
    ),
    paymentSummary: paymentSummary(tenders),
  });
}

function mapDetails(
  header: SummaryRow,
  lineRows: readonly LineRow[],
  tenderRows: readonly TenderRow[],
): LocalHistoryDetails {
  const summary = mapSummary(header, []);
  return Object.freeze({
    orderGuid: summary.orderGuid,
    localSequence: summary.localSequence,
    soldAtIso: summary.soldAtIso,
    cashierName: summary.cashierName,
    state: summary.state,
    totalCents: summary.totalCents,
    discountCents: summary.discountCents,
    actualAmountCents: summary.actualAmountCents,
    lines: Object.freeze(lineRows.map(mapLine)),
    tenders: Object.freeze(tenderRows.map(mapTender)),
  });
}

function mapLine(row: LineRow): LocalHistoryLine {
  const itemNumber =
    row.item_number === null || row.item_number === undefined
      ? null
      : requiredText(
          row.item_number,
          "persisted local history item number",
        );
  const kind = requiredText(
    row.line_kind,
    "persisted local history line kind",
  );
  if (kind !== "sale" && kind !== "return") {
    throw new Error("Invalid persisted local history line kind.");
  }
  return Object.freeze({
    lineId: requiredText(row.line_id, "persisted local history line id"),
    productCode: requiredText(
      row.product_code,
      "persisted local history product code",
    ),
    itemNumber,
    lookupCode: requiredText(
      row.lookup_code,
      "persisted local history lookup code",
    ),
    displayName: requiredText(
      row.display_name,
      "persisted local history display name",
      4_096,
    ),
    quantity: requiredText(
      row.quantity,
      "persisted local history quantity",
    ),
    unitPriceCents: integer(
      row.unit_price_cents,
      "persisted local history unit price",
    ),
    discountCents: integer(
      row.discount_cents,
      "persisted local history line discount",
    ),
    actualAmountCents: integer(
      row.actual_amount_cents,
      "persisted local history line actual amount",
    ),
    kind,
  });
}

function mapTender(row: TenderRow): LocalHistoryTenderSummary {
  const method = requiredText(
    row.method,
    "persisted local history tender method",
  );
  if (method !== "cash" && method !== "card" && method !== "voucher") {
    throw new Error("Invalid persisted local history tender method.");
  }
  return Object.freeze({
    method,
    amountCents: integer(
      row.amount_cents,
      "persisted local history tender amount",
    ),
  });
}

function paymentSummary(
  tenders: readonly LocalHistoryTenderSummary[],
): string {
  const labels = new Map<LocalHistoryTenderSummary["method"], string>([
    ["cash", "Cash"],
    ["card", "Card"],
    ["voucher", "Voucher"],
  ]);
  return (
    [...new Set(tenders.map((tender) => labels.get(tender.method) ?? ""))]
      .filter(Boolean)
      .join(", ")
  );
}

function escapeLike(value: string): string {
  return value.replaceAll("\\", "\\\\").replaceAll("%", "\\%").replaceAll("_", "\\_");
}

function localHistoryState(value: unknown): LocalHistoryOrderState {
  const state = requiredText(value, "persisted local history state");
  if (visibleStates.includes(state as LocalHistoryOrderState)) {
    return state as LocalHistoryOrderState;
  }
  throw new Error("Invalid persisted local history state.");
}

function persistedIso(value: unknown, label: string): string {
  const text = requiredText(value, label);
  if (!Number.isFinite(Date.parse(text))) {
    throw new Error(`${label} is invalid.`);
  }
  return text;
}

function requiredText(
  value: unknown,
  label: string,
  maximumLength = 128,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`${label} is required.`);
  }
  const normalized = value.trim();
  if (
    !normalized ||
    normalized.length > maximumLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized;
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error(`${label} is invalid.`);
  }
  return parsed;
}

function positiveInteger(value: unknown, label: string): number {
  const parsed = integer(value, label);
  if (parsed <= 0) throw new Error(`${label} is invalid.`);
  return parsed;
}

function nonNegativeInteger(value: unknown, label: string): number {
  const parsed = integer(value, label);
  if (parsed < 0) throw new Error(`${label} is invalid.`);
  return parsed;
}
