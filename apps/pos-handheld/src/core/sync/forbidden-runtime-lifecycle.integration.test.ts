import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type { LocalOrder } from "@hb/pos-domain/core/contracts/order";
import { PosDatabase } from "../db/pos-database";
import type { PosRepositoryBundle } from "../db/sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";
import {
  PosRuntimeController,
  type PosRuntimeServices,
} from "../runtime/pos-runtime";

import { PosSyncCoordinator } from "@hb/pos-sync/core/sync/sync-coordinator";

import { createCashierInvalidationHandler } from "@hb/pos-domain/features/cashier-login/cashier-session-invalidation-recovery";

const nowIso = "2026-07-28T06:00:00.000Z";
const orderGuid = "019fa81c-9c82-7a75-9f1f-f47be4a6fe81";
type TestRuntimeServices = PosRuntimeServices &
  Readonly<{ repositories: PosRepositoryBundle }>;

class NodeSqliteConnection implements SqliteConnectionPort {
  public closed = false;
  private transactionActive = false;

  public constructor(private readonly database: DatabaseSync) {}

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    const result = this.database
      .prepare(sql)
      .run(...parameters as readonly SQLInputValue[]);
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    // Node 内置 SQLite 不含 SQLCipher；仅为测试的精确探针提供有效版本。
    if (sql === "PRAGMA cipher_version;") {
      return { cipher_version: "4.6.1" } as unknown as T;
    }
    const row = this.database
      .prepare(sql)
      .get(...parameters as readonly SQLInputValue[]);
    return row === undefined ? null : row as unknown as T;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    if (this.transactionActive) {
      throw new Error("Nested test transaction.");
    }
    this.transactionActive = true;
    this.database.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.database.exec("COMMIT");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK");
      throw error;
    } finally {
      this.transactionActive = false;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
    this.closed = true;
  }
}

test("真实连接：403 先锁 UI 仍保持 SQLCipher 可用，订单和 outbox 随后耐久化为 Blocked403", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  let activeCashierClears = 0;

  const controller = new PosRuntimeController<TestRuntimeServices>(async () => {
    const database = await PosDatabase.open({
      databaseName: "forbidden-lifecycle.db",
      driver: {
        async open() {
          return connection;
        },
      },
      keyProvider: {
        async getOrCreateDatabaseKey() {
          return "ab".repeat(32);
        },
      },
      nowIso: () => nowIso,
    });
    const repositories = database.repositories(
      {
        async encrypt(value: string) {
          return new TextEncoder().encode(value);
        },
        async decrypt(value: Uint8Array) {
          return new TextDecoder().decode(value);
        },
      },
      () => "019fa81c-a3ae-7fa3-9b63-124d1d8a8417",
    );
    return {
      async shutdown() {
        await database.close();
      },
      backend: "reachable" as const,
      device: "authorized-online" as const,
      repositories,
    };
  });

  await controller.start();
  const runtimeServices = controller.getServices();
  assert.ok(runtimeServices);
  const repositoryBundle = runtimeServices.repositories;
  await repositoryBundle.orders.saveDraft(pendingOrder());
  await repositoryBundle.outbox.enqueue({
    messageId: "019fa81c-b394-76bc-94bb-3ea5c5cff3b9",
    aggregateId: orderGuid,
    kind: "order-sync",
    payloadJson: JSON.stringify({ orderGuid }),
    nextAttemptAtIso: nowIso,
  });

  const invalidation = createCashierInvalidationHandler({
    clearActiveCashier() {
      activeCashierClears += 1;
    },
    lockRuntime() {
      controller.updateOperationalState({
        backend: "rejected",
        device: "locked",
      });
    },
  });
  const coordinator = new PosSyncCoordinator({
    outbox: repositoryBundle.outbox,
    auditRepository: repositoryBundle.audit,
    orderSync: {
      async sync() {
        // 模拟 Axios 在把同一 403 抛回协调器之前先广播认证失效。
        invalidation("forbidden");
        assert.equal(connection.closed, false);
        assert.equal(controller.getState().database, "ready");
        return {
          kind: "blocked",
          failure: "forbidden",
          code: "DEVICE_DISABLED",
        };
      },
    },
    auditUploader: {
      async upload() {
        return { kind: "uploaded" };
      },
    },
    security: {
      async lockDevice() {
        invalidation("forbidden");
      },
    },
    now: () => new Date(nowIso),
    random: () => 0.5,
  });

  try {
    const report = await coordinator.requestDrain();
    assert.equal(report.orderBlocked, 1);
    assert.equal(
      (await repositoryBundle.orders.getByGuid(orderGuid))?.state,
      "Blocked403",
    );
    assert.equal(
      (
        await connection.getFirst<{ state: string }>(
          "SELECT state FROM outbox_messages WHERE aggregate_id = ?",
          [orderGuid],
        )
      )?.state,
      "blocked403",
    );
    assert.equal(controller.getState().phase, "locked");
    assert.equal(controller.getState().database, "ready");
    assert.equal(connection.closed, false);
    assert.equal(activeCashierClears, 2);
  } finally {
    await controller.stop();
  }
  assert.equal(connection.closed, true);
});

function pendingOrder(): LocalOrder {
  return {
    orderGuid,
    localSequence: 1,
    storeCode: "S1",
    deviceCode: "IPAD-1",
    cashierId: "C1",
    cashierName: "Cashier",
    soldAtIso: nowIso,
    state: "PendingSync",
    total: { currency: "AUD", cents: 500 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: 500 },
    originalOrderGuid: null,
    lines: [],
    tenders: [],
  };
}
