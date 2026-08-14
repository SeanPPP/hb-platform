import { DatabaseSync, type SQLInputValue } from "node:sqlite";

import type { DatabaseMigration } from "@/core/db/migrations";
import { POS_DATABASE_MIGRATIONS, applyMigrations } from "@/core/db/migrations";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@/core/db/types";

export const TEST_NOW_ISO = "2026-07-28T08:00:00.000Z";

/**
 * 测试专用内存 SQLite 连接：串行化写事务，行为与生产 SerializedSqliteConnection
 * 一致（同一时刻至多一个写事务），用于验证真实约束与并发 fence。
 */
export class NodeSqliteConnection implements SqliteConnectionPort {
  private readonly queue = new AsyncSerialQueue();
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
    const row = this.database
      .prepare(sql)
      .get(...parameters as readonly SQLInputValue[]);
    return row === undefined ? null : row as T;
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
      return Promise.reject(new Error("Nested test transaction."));
    }
    return this.queue.enqueue(async () => {
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
    });
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

class AsyncSerialQueue {
  private tail: Promise<void> = Promise.resolve();

  public enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.tail.then(operation, operation);
    this.tail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }
}

export function openTestDatabase(
  migrations: readonly DatabaseMigration[] = POS_DATABASE_MIGRATIONS,
): Promise<NodeSqliteConnection> {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  // 默认使用完整迁移链；调用方可传入子集（例如 M1..M39）模拟升级路径。
  return applyMigrations(connection, () => TEST_NOW_ISO, migrations).then(
    () => connection,
  );
}

/** M9 held_order_records 所需的最小合法旧行；payload 为调用方提供的密文。 */
export async function insertHeldOrderRow(
  connection: SqliteConnectionPort,
  input: Readonly<{
    holdId: string;
    payloadCiphertext: Uint8Array;
    localSequence?: number;
    storeCode?: string;
    deviceCode?: string;
  }>,
): Promise<void> {
  const localSequence =
    input.localSequence ?? Date.now() + Math.floor(Math.random() * 1_000_000);
  await connection.run(
    `INSERT INTO held_order_records (
      hold_id, local_sequence, store_code, device_code, held_by_cashier_id,
      held_by_cashier_name, status, payload_version, payload_ciphertext,
      item_count, subtotal_cents, discount_cents, actual_amount_cents,
      held_at_iso, created_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, ?, ?, ?, 'Pending', 1, ?, 1, 100, 0, 100, ?, ?, ?)`,
    [
      input.holdId,
      localSequence,
      input.storeCode ?? "S1",
      input.deviceCode ?? "HANDHELD-01",
      "cashier-1",
      "Cashier One",
      input.payloadCiphertext,
      TEST_NOW_ISO,
      TEST_NOW_ISO,
      TEST_NOW_ISO,
    ],
  );
}

/**
 * 与现有 SensitivePayloadEncryptor 结构一致的最小假实现：base64 编码，
 * 便于断言数据库只保留密文、解密可精确还原。
 */
export const fakeEncryptor = {
  async encrypt(plaintext: string): Promise<Uint8Array> {
    return new TextEncoder().encode(
      Buffer.from(plaintext, "utf8").toString("base64"),
    );
  },
  async decrypt(ciphertext: Uint8Array): Promise<string> {
    return Buffer.from(new TextDecoder().decode(ciphertext), "base64").toString(
      "utf8",
    );
  },
};
