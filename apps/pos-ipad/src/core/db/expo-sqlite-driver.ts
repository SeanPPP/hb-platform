import * as SQLite from "expo-sqlite";

import {
  type NativeSqliteOperations,
  SerializedSqliteConnection,
} from "./serialized-sqlite-connection";
import type {
  SqliteConnectionPort,
  SqliteDriverPort,
  SqlValue,
} from "./types";

type ExpoDatabase = Awaited<ReturnType<typeof SQLite.openDatabaseAsync>>;

/** Expo Go 不含 SQLCipher；仅 Development/Preview/Production Build 可使用此实现。 */
export class ExpoSqliteDriver implements SqliteDriverPort {
  public async open(databaseName: string): Promise<SqliteConnectionPort> {
    const database = await SQLite.openDatabaseAsync(databaseName);
    return new SerializedSqliteConnection(toNativeOperations(database));
  }
}

function toNativeOperations(
  database: ExpoDatabase,
): NativeSqliteOperations {
  return {
    exec: (sql) => database.execAsync(sql),
    run: async (sql, parameters) => {
      const result = await database.runAsync(sql, [...parameters]);
      return {
        changes: result.changes,
        lastInsertRowId: Number(result.lastInsertRowId),
      };
    },
    getFirst: <T extends object>(
      sql: string,
      parameters: readonly SqlValue[],
    ) => database.getFirstAsync<T>(sql, [...parameters]),
    getAll: <T extends object>(
      sql: string,
      parameters: readonly SqlValue[],
    ) => database.getAllAsync<T>(sql, [...parameters]),
    close: () => database.closeAsync(),
  };
}
