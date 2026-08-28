import assert from "node:assert/strict";
import test from "node:test";

import {
  type NativeSqliteOperations,
  SerializedSqliteConnection,
} from "./serialized-sqlite-connection";
import type { SqlRunResult, SqlValue } from "./types";

class FakeNativeDatabase implements NativeSqliteOperations {
  public readonly events: string[] = [];
  public failOn: string | null = null;

  public async exec(sql: string): Promise<void> {
    this.events.push(sql);
    if (this.failOn === sql) {
      throw new Error(`failed: ${sql}`);
    }
  }

  public async run(
    sql: string,
    _parameters: readonly SqlValue[],
  ): Promise<SqlRunResult> {
    this.events.push(sql);
    return { changes: 1, lastInsertRowId: 1 };
  }

  public async getFirst<T extends object>(
    sql: string,
    _parameters: readonly SqlValue[],
  ): Promise<T | null> {
    this.events.push(sql);
    return null;
  }

  public async getAll<T extends object>(
    sql: string,
    _parameters: readonly SqlValue[],
  ): Promise<readonly T[]> {
    this.events.push(sql);
    return [];
  }

  public async close(): Promise<void> {
    this.events.push("CLOSE");
  }
}

test("独占事务在同一已解锁连接上按 BEGIN IMMEDIATE/COMMIT 串行执行", async () => {
  const native = new FakeNativeDatabase();
  const connection = new SerializedSqliteConnection(native);

  const result = await connection.withExclusiveTransaction(async (tx) => {
    await tx.run("INSERT A");
    await tx.getFirst("SELECT A");
    return "committed";
  });

  assert.equal(result, "committed");
  assert.deepEqual(native.events, [
    "BEGIN IMMEDIATE;",
    "INSERT A",
    "SELECT A",
    "COMMIT;",
  ]);
});

test("事务运行期间，根连接查询排到 COMMIT 之后", async () => {
  const native = new FakeNativeDatabase();
  const connection = new SerializedSqliteConnection(native);
  let releaseTransaction!: () => void;
  const transactionGate = new Promise<void>((resolve) => {
    releaseTransaction = resolve;
  });

  const transaction = connection.withExclusiveTransaction(async (tx) => {
    await tx.run("INSERT IN TRANSACTION");
    await transactionGate;
  });
  const outside = connection.run("INSERT OUTSIDE");

  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(native.events, [
    "BEGIN IMMEDIATE;",
    "INSERT IN TRANSACTION",
  ]);

  releaseTransaction();
  await Promise.all([transaction, outside]);
  assert.deepEqual(native.events, [
    "BEGIN IMMEDIATE;",
    "INSERT IN TRANSACTION",
    "COMMIT;",
    "INSERT OUTSIDE",
  ]);
});

test("业务操作失败时回滚，原错误保持不变", async () => {
  const native = new FakeNativeDatabase();
  const connection = new SerializedSqliteConnection(native);
  const original = new Error("ledger rejected");

  await assert.rejects(
    connection.withExclusiveTransaction(async (tx) => {
      await tx.run("INSERT A");
      throw original;
    }),
    (error: unknown) => error === original,
  );
  assert.deepEqual(native.events, [
    "BEGIN IMMEDIATE;",
    "INSERT A",
    "ROLLBACK;",
  ]);
});

test("禁止嵌套事务和事务作用域主动关闭连接", async () => {
  const native = new FakeNativeDatabase();
  const connection = new SerializedSqliteConnection(native);

  await connection.withExclusiveTransaction(async (tx) => {
    await assert.rejects(
      tx.withExclusiveTransaction(async () => undefined),
      /Nested SQLite transactions/,
    );
    await assert.rejects(
      tx.close(),
      /transaction-scoped connection cannot be closed/,
    );
  });
});
