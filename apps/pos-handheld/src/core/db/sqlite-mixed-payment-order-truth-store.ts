import { createAud, type LocalOrder, type OrderTender } from "../contracts";

import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type {
  MixedPaymentOrderTruth,
  MixedPaymentOrderTruthPort,
  MixedTenderReversalLink,
} from "@/features/payments/mixed";

type MixedOrderRow = Readonly<{
  order_guid: unknown;
  local_sequence: unknown;
  state: unknown;
  actual_amount_cents: unknown;
}>;

type MixedTenderRow = Readonly<{
  tender_guid: unknown;
  method: unknown;
  amount_cents: unknown;
}>;

type MixedTenderReversalLinkRow = Readonly<{
  action_id: unknown;
  source_tender_guid: unknown;
  reversal_tender_guid: unknown;
}>;

/**
 * Mixed payment 只读取一个事务内的订单头和完整 tender 账本。
 * reversal 是负数 tender，不能被过滤、合并或覆盖原 tender。
 */
export class SqliteMixedPaymentOrderTruthStore
implements MixedPaymentOrderTruthPort {
  public constructor(private readonly connection: SqliteConnectionPort) {}

  public getPaymentTruth(
    orderGuid: string,
  ): Promise<MixedPaymentOrderTruth | null> {
    if (!orderGuid.trim()) throw new TypeError("orderGuid is required.");
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const order = await transaction.getFirst<MixedOrderRow>(
        `SELECT order_guid, local_sequence, state, actual_amount_cents
         FROM local_orders
         WHERE order_guid = ?`,
        [orderGuid],
      );
      if (!order) return null;

      // local_sequence 是不可变本地事实；虽然 feature Port 不导出它，仍在同一快照内校验。
      positiveInteger(order.local_sequence, "local_sequence");
      const tenders = await transaction.getAll<MixedTenderRow>(
        `SELECT tender_guid, method, amount_cents
         FROM order_tenders
         WHERE order_guid = ?
         ORDER BY created_at_iso ASC, tender_guid ASC`,
        [orderGuid],
      );
      const reversalLinks =
        await transaction.getAll<MixedTenderReversalLinkRow>(
          `SELECT action_id, source_tender_guid, reversal_tender_guid
           FROM payment_tender_reversal_links
           WHERE order_guid = ?
           ORDER BY created_at_iso ASC, action_id ASC`,
          [orderGuid],
        );
      return {
        orderGuid: text(order.order_guid, "order_guid"),
        state: localOrderState(order.state),
        actualAmount: createAud(integer(order.actual_amount_cents, "actual_amount_cents")),
        tenders: tenders.map(mapTender),
        reversalLinks: reversalLinks.map(mapReversalLink),
      };
    });
  }
}

function mapReversalLink(
  row: MixedTenderReversalLinkRow,
): MixedTenderReversalLink {
  return {
    actionId: text(row.action_id, "reversal action_id"),
    sourceTenderGuid: text(
      row.source_tender_guid,
      "reversal source_tender_guid",
    ),
    reversalTenderGuid: text(
      row.reversal_tender_guid,
      "reversal reversal_tender_guid",
    ),
  };
}

function mapTender(row: MixedTenderRow): OrderTender {
  return {
    tenderGuid: text(row.tender_guid, "tender_guid"),
    method: tenderMethod(row.method),
    amount: createAud(integer(row.amount_cents, "amount_cents")),
    reference: null,
    reservationToken: null,
  };
}

function localOrderState(value: unknown): LocalOrder["state"] {
  const state = text(value, "state");
  if (
    state === "Draft" ||
    state === "Completing" ||
    state === "CompletedLocal" ||
    state === "PendingSync" ||
    state === "Syncing" ||
    state === "Synced" ||
    state === "Blocked403" ||
    state === "Rejected"
  ) {
    return state;
  }
  throw new Error("Invalid persisted local order state.");
}

function tenderMethod(value: unknown): OrderTender["method"] {
  const method = text(value, "method");
  if (method === "cash" || method === "card" || method === "voucher") {
    return method;
  }
  throw new Error("Invalid persisted tender method.");
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
