/**
 * 串行化的 SQLite 连接封装。
 *
 * 来源：packages/pos-db/src/core/db/serialized-sqlite-connection.ts（复制最小实现）。
 * 所有操作经同一串行队列执行；事务在同一 native 连接上 BEGIN IMMEDIATE，
 * 事务期间外部查询会排队等待，避免误入事务或并发写冲突。
 */
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

export interface NativeSqliteOperations {
  exec(sql: string): Promise<void>;
  run(sql: string, parameters: readonly SqlValue[]): Promise<SqlRunResult>;
  getFirst<T extends object>(sql: string, parameters: readonly SqlValue[]): Promise<T | null>;
  getAll<T extends object>(sql: string, parameters: readonly SqlValue[]): Promise<readonly T[]>;
  close(): Promise<void>;
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

  public drain(): Promise<void> {
    return this.tail;
  }
}

export class SerializedSqliteConnection implements SqliteConnectionPort {
  public constructor(
    private readonly native: NativeSqliteOperations,
    private readonly queue = new AsyncSerialQueue(),
    private readonly transactionScope = false,
  ) {}

  public exec(sql: string): Promise<void> {
    return this.queue.enqueue(() => this.native.exec(sql));
  }

  public run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    return this.queue.enqueue(() => this.native.run(sql, parameters));
  }

  public getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    return this.queue.enqueue(() => this.native.getFirst<T>(sql, parameters));
  }

  public getAll<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<readonly T[]> {
    return this.queue.enqueue(() => this.native.getAll<T>(sql, parameters));
  }

  public withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    if (this.transactionScope) {
      return Promise.reject(new Error("Nested SQLite transactions are not supported."));
    }

    return this.queue.enqueue(async () => {
      await this.native.exec("BEGIN IMMEDIATE;");
      const transactionQueue = new AsyncSerialQueue();
      const transaction = new SerializedSqliteConnection(this.native, transactionQueue, true);

      try {
        const result = await operation(transaction);
        await transactionQueue.drain();
        await this.native.exec("COMMIT;");
        return result;
      } catch (error: unknown) {
        try {
          await transactionQueue.drain();
          await this.native.exec("ROLLBACK;");
        } catch (rollbackError: unknown) {
          throw rollbackFailure(error, rollbackError);
        }
        throw error;
      }
    });
  }

  public close(): Promise<void> {
    if (this.transactionScope) {
      return Promise.reject(new Error("A transaction-scoped connection cannot be closed."));
    }
    return this.queue.enqueue(() => this.native.close());
  }
}

function rollbackFailure(originalError: unknown, rollbackError: unknown): Error {
  const originalMessage =
    originalError instanceof Error ? originalError.message : String(originalError);
  const rollbackMessage =
    rollbackError instanceof Error ? rollbackError.message : String(rollbackError);
  return new Error(
    `SQLite operation failed (${originalMessage}) and rollback failed (${rollbackMessage}).`,
    { cause: originalError },
  );
}
