import type { SqliteConnectionPort } from "./types";

type PendingOrderSyncCountRow = Readonly<{
  pending_order_sync_count: unknown;
}>;

/** 顶部状态条的轻量只读来源；不加载订单、支付、打印或审计明细。 */
export class SqliteOrderSyncStatusRepository {
  public constructor(private readonly db: SqliteConnectionPort) {}

  public async readPendingOrderSyncCount(): Promise<number> {
    const row = await this.db.getFirst<PendingOrderSyncCountRow>(
      `SELECT COUNT(DISTINCT aggregate_id) AS pending_order_sync_count
       FROM outbox_messages
       WHERE kind = 'order-sync'
         AND state <> 'succeeded'`,
    );
    const count = row?.pending_order_sync_count;
    if (
      typeof count !== "number" ||
      !Number.isSafeInteger(count) ||
      count < 0
    ) {
      throw new TypeError(
        "Pending order sync count must be a non-negative safe integer.",
      );
    }
    return count;
  }
}
