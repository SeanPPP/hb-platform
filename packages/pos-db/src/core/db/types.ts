/**
 * 数据层只依赖这个最小接口，避免业务代码直接绑定 Expo SQLite。
 * Node 测试可注入记录调用的驱动，但不得把它当成 SQLCipher 真机验证。
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

/** 数据库密钥必须来自 Keychain；实现不得回退到 AsyncStorage。 */
export interface DatabaseKeyProviderPort {
  getOrCreateDatabaseKey(): Promise<string>;
}

export type PosDatabaseOptions = Readonly<{
  databaseName: string;
  driver: SqliteDriverPort;
  keyProvider: DatabaseKeyProviderPort;
  nowIso: () => string;
}>;
