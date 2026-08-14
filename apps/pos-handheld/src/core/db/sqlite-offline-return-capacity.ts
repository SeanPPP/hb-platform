import type { CartSnapshot } from "../contracts/cart";

import type { SqliteConnectionPort } from "./types";

export interface OfflineReturnCapacityFacade {
  hasCapacity(snapshot: CartSnapshot): Promise<boolean>;
}

type AggregatedReturnNeed = Readonly<{
  returnSourceKey: string;
  originalOrderGuid: string;
  originalOrderDetailGuid: string | null;
  requiredQuantity: number;
}>;

type ReturnCapacityRow = Readonly<{
  original_order_guid: unknown;
  original_order_detail_guid: unknown;
  remaining_quantity: unknown;
}>;

/**
 * 只读预检只决定离线退款能否继续进入结账；不预留或扣减容量。
 * 最终容量裁决仍由现金订单 committer 在同一 SQLCipher 事务内执行 CAS。
 */
export class SqliteOfflineReturnCapacity
implements OfflineReturnCapacityFacade {
  readonly #connection: SqliteConnectionPort;

  public constructor(connection: SqliteConnectionPort) {
    this.#connection = connection;
  }

  public async hasCapacity(snapshot: CartSnapshot): Promise<boolean> {
    const needs = aggregateReturnNeeds(snapshot);
    if (needs === null) return false;

    for (const need of needs) {
      const row = await this.#connection.getFirst<ReturnCapacityRow>(
        `SELECT original_order_guid, original_order_detail_guid,
          remaining_quantity
         FROM return_capacity
         WHERE return_source_key = ?`,
        [need.returnSourceKey],
      );
      if (!row || !matchesCapacityIdentity(row, need)) return false;
      const remainingQuantity = nonNegativeInteger(row.remaining_quantity);
      if (
        remainingQuantity === null ||
        remainingQuantity < need.requiredQuantity
      ) {
        return false;
      }
    }

    return true;
  }
}

function aggregateReturnNeeds(
  snapshot: CartSnapshot,
): readonly AggregatedReturnNeed[] | null {
  if (
    !isRecord(snapshot) ||
    snapshot.mode !== "return" ||
    !Array.isArray(snapshot.lines) ||
    snapshot.lines.length === 0
  ) {
    return null;
  }

  const bySource = new Map<string, AggregatedReturnNeed>();
  for (const line of snapshot.lines as readonly unknown[]) {
    const need = parseReturnNeed(line);
    if (need === null) return null;
    const existing = bySource.get(need.returnSourceKey);
    if (!existing) {
      bySource.set(need.returnSourceKey, need);
      continue;
    }
    if (
      existing.originalOrderGuid !== need.originalOrderGuid ||
      existing.originalOrderDetailGuid !== need.originalOrderDetailGuid
    ) {
      return null;
    }
    const requiredQuantity =
      existing.requiredQuantity + need.requiredQuantity;
    if (!Number.isSafeInteger(requiredQuantity)) return null;
    bySource.set(need.returnSourceKey, {
      ...existing,
      requiredQuantity,
    });
  }
  return [...bySource.values()];
}

function parseReturnNeed(value: unknown): AggregatedReturnNeed | null {
  if (!isRecord(value) || value.kind !== "return") return null;
  const returnSourceKey = exactNonBlankText(value.returnSourceKey);
  const originalOrderGuid = exactNonBlankText(value.originalOrderGuid);
  const originalOrderDetailGuid =
    value.originalOrderDetailGuid === null
      ? null
      : exactNonBlankText(value.originalOrderDetailGuid);
  const requiredQuantity = positiveIntegerText(value.quantity);
  if (
    returnSourceKey === null ||
    originalOrderGuid === null ||
    (value.originalOrderDetailGuid !== null &&
      originalOrderDetailGuid === null) ||
    requiredQuantity === null
  ) {
    return null;
  }
  return {
    returnSourceKey,
    originalOrderGuid,
    originalOrderDetailGuid,
    requiredQuantity,
  };
}

function matchesCapacityIdentity(
  row: ReturnCapacityRow,
  need: AggregatedReturnNeed,
): boolean {
  return (
    row.original_order_guid === need.originalOrderGuid &&
    row.original_order_detail_guid === need.originalOrderDetailGuid
  );
}

function exactNonBlankText(value: unknown): string | null {
  if (typeof value !== "string" || !value.trim() || value !== value.trim()) {
    return null;
  }
  return value;
}

function positiveIntegerText(value: unknown): number | null {
  if (typeof value !== "string" || !/^[1-9]\d*$/.test(value)) return null;
  const integer = Number(value);
  return Number.isSafeInteger(integer) ? integer : null;
}

function nonNegativeInteger(value: unknown): number | null {
  if (
    typeof value !== "string" ||
    !/^(?:0|[1-9]\d*)$/.test(value)
  ) {
    return null;
  }
  const integer = Number(value);
  return Number.isSafeInteger(integer) ? integer : null;
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
