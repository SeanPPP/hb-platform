import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import test from "node:test";

import { SqliteOrderSyncStatusRepository } from "./sqlite-order-sync-status-repository";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

test("订单同步状态只去重统计非 succeeded 的 order-sync 聚合", async () => {
  const database = new DatabaseSync(":memory:");
  database.exec(`
    CREATE TABLE outbox_messages (
      message_id TEXT PRIMARY KEY,
      aggregate_id TEXT NOT NULL,
      kind TEXT NOT NULL,
      state TEXT NOT NULL
    );
    INSERT INTO outbox_messages VALUES
      ('m1', 'order-1', 'order-sync', 'pending'),
      ('m2', 'order-1', 'order-sync', 'leased'),
      ('m3', 'order-2', 'order-sync', 'blocked403'),
      ('m4', 'order-3', 'order-sync', 'rejected'),
      ('m5', 'order-4', 'order-sync', 'succeeded'),
      ('m6', 'audit-1', 'audit-batch', 'pending'),
      ('m7', 'order-5', 'order-sync', 'leased');
  `);

  try {
    const repository = new SqliteOrderSyncStatusRepository(
      readOnlyConnection(database),
    );

    assert.equal(await repository.readPendingOrderSyncCount(), 4);
  } finally {
    database.close();
  }
});

test("订单同步状态拒绝异常计数，不能把未知数据库结果显示为零", async () => {
  for (const value of [null, -1, 0.5, Number.MAX_SAFE_INTEGER + 1]) {
    const repository = new SqliteOrderSyncStatusRepository({
      ...unsupportedConnection(),
      async getFirst<T extends object>(): Promise<T | null> {
        return { pending_order_sync_count: value } as unknown as T;
      },
    });

    await assert.rejects(
      () => repository.readPendingOrderSyncCount(),
      TypeError,
    );
  }
});

function readOnlyConnection(database: DatabaseSync): SqliteConnectionPort {
  return {
    ...unsupportedConnection(),
    async getFirst<T extends object>(sql: string) {
      return (database.prepare(sql).get() ?? null) as T | null;
    },
  };
}

function unsupportedConnection(): SqliteConnectionPort {
  const unsupported = (): Promise<never> =>
    Promise.reject(new Error("unsupported test connection operation"));
  return {
    exec: unsupported,
    run: unsupported as (
      sql: string,
      parameters?: readonly SqlValue[],
    ) => Promise<SqlRunResult>,
    getFirst: unsupported,
    getAll: unsupported,
    withExclusiveTransaction: unsupported,
    close: async () => undefined,
  };
}
