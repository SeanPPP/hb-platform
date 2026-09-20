/**
 * 移动端 SQLite 最小接口。
 *
 * 来源：packages/pos-db/src/core/db/types.ts（apps/mobile 不在根 npm workspaces 内，
 * 无法直接依赖 @hb/pos-db，故复制最小子集）。业务代码只依赖这些端口，
 * Node 单测可注入内存实现记录 SQL。
 */
export type SqlValue = string | number | null | Uint8Array;

export type SqlRunResult = Readonly<{
  changes: number;
  lastInsertRowId: number;
}>;

export interface SqliteConnectionPort {
  exec(sql: string): Promise<void>;
  run(sql: string, parameters?: readonly SqlValue[]): Promise<SqlRunResult>;
  getFirst<T extends object>(sql: string, parameters?: readonly SqlValue[]): Promise<T | null>;
  getAll<T extends object>(sql: string, parameters?: readonly SqlValue[]): Promise<readonly T[]>;
  withExclusiveTransaction<T>(operation: (transaction: SqliteConnectionPort) => Promise<T>): Promise<T>;
  close(): Promise<void>;
}

export interface SqliteDriverPort {
  open(databaseName: string): Promise<SqliteConnectionPort>;
}
