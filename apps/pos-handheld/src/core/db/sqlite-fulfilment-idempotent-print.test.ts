import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations } from "./migrations";
import { SqliteFulfilmentStore } from "./sqlite-fulfilment-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const NOW = "2026-07-31T00:00:00.000Z";
const ORDER_GUID = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d01";

test("礼券余额打印任务按稳定 jobId 幂等入队且校验冻结字节", async () => {
  const connection = new NodeSqliteConnection();
  try {
    await applyMigrations(connection, () => NOW);
    await seedOrder(connection);
    const store = new SqliteFulfilmentStore(connection, {
      encryptor: {
        async encrypt(plaintext) {
          return new TextEncoder().encode(plaintext);
        },
        async decrypt(ciphertext) {
          return new TextDecoder().decode(ciphertext);
        },
      },
      nowIso: () => NOW,
      createPrintJobId: () => "unused",
    });
    const input = {
      jobId: "voucher-balance:voucher-attempt-1",
      orderGuid: ORDER_GUID,
      printerId: "printer-1",
      receiptBytes: Uint8Array.of(1, 2, 3),
      isReprint: false,
    } as const;

    assert.equal(await store.enqueuePrintJobOnce(input), "created");
    assert.equal(await store.enqueuePrintJobOnce(input), "existing");
    assert.equal(await store.hasPrintJob(input.jobId), true);
    assert.equal(
      (
        await connection.getFirst<{ count: unknown }>(
          "SELECT COUNT(*) AS count FROM print_jobs WHERE job_id = ?",
          [input.jobId],
        )
      )?.count,
      1,
    );
    await assert.rejects(
      () =>
        store.enqueuePrintJobOnce({
          ...input,
          receiptBytes: Uint8Array.of(9),
        }),
      /does not match|conflict/i,
    );
  } finally {
    await connection.close();
  }
});

async function seedOrder(connection: SqliteConnectionPort): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, 1, 'S001', 'IPAD-1', 'cashier-1', 'Cashier',
      ?, 'Synced', 700, 0, 700, NULL, ?, ?)`,
    [ORDER_GUID, NOW, NOW, NOW],
  );
}

class NodeSqliteConnection implements SqliteConnectionPort {
  private readonly database = new DatabaseSync(":memory:");

  public constructor() {
    this.database.exec("PRAGMA foreign_keys = ON");
  }

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    const result = this.database
      .prepare(sql)
      .run(...parameters.map(toSqlInput));
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    return (
      (this.database
        .prepare(sql)
        .get(...parameters.map(toSqlInput)) as T | undefined) ?? null
    );
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqlInput)) as T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.database.exec("COMMIT");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK");
      throw error;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

function toSqlInput(value: SqlValue): SQLInputValue {
  return value instanceof Uint8Array ? Buffer.from(value) : value;
}
