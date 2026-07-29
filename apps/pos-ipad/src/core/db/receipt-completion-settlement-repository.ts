import type { SqliteConnectionPort } from "./types";

export type StoredReceiptCompletionSettlement = Readonly<{
  cashChangeCents: number;
}>;

type CompletionAuditRow = Readonly<{
  event_type: unknown;
  payload_json: unknown;
}>;

/**
 * 重打现金小票所需的找零只允许来自完成事务已持久化的审计。
 *
 * 同一订单缺失或出现多份完成审计都视为账本证据不唯一，调用方必须拒绝重打，
 * 不能从 tender、当前购物车或设备时间推算。
 */
export class ReceiptCompletionSettlementRepository {
  public constructor(private readonly connection: SqliteConnectionPort) {}

  public async getByOrderGuid(
    orderGuid: string,
  ): Promise<StoredReceiptCompletionSettlement | null> {
    if (typeof orderGuid !== "string" || !orderGuid.trim()) return null;

    const rows = await this.connection.getAll<CompletionAuditRow>(
      `SELECT event_type, payload_json
       FROM audit_events
       WHERE order_guid = ?
         AND event_type IN ('SALE_COMPLETE', 'RETURN_REFUND_COMPLETE')
       ORDER BY occurred_at_iso DESC, event_id DESC
       LIMIT 2`,
      [orderGuid],
    );
    if (rows.length !== 1) return null;

    const row = rows[0];
    if (
      !row ||
      (row.event_type !== "SALE_COMPLETE" &&
        row.event_type !== "RETURN_REFUND_COMPLETE") ||
      typeof row.payload_json !== "string"
    ) {
      return null;
    }

    try {
      const payload: unknown = JSON.parse(row.payload_json);
      if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
        return null;
      }
      const cashChangeCents = (
        payload as Readonly<Record<string, unknown>>
      ).changeCents;
      return typeof cashChangeCents === "number" &&
        Number.isSafeInteger(cashChangeCents) &&
        cashChangeCents >= 0
        ? { cashChangeCents }
        : null;
    } catch {
      return null;
    }
  }
}
