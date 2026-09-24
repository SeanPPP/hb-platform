/**
 * expo-sqlite 驱动封装。
 *
 * 来源：apps/pos-ipad/src/core/db/expo-sqlite-driver.ts（复制，去掉 SQLCipher 相关说明）。
 * 商品目录不含敏感数据，使用普通 expo-sqlite；Expo Go 不含该原生模块，
 * 只能在 Development/Preview/Production Build 中使用。
 */
import * as SQLite from "expo-sqlite";
import { SerializedSqliteConnection, type NativeSqliteOperations } from "./serialized-sqlite-connection";
import type { SqliteConnectionPort, SqliteDriverPort, SqlValue } from "./types";

type ExpoDatabase = Awaited<ReturnType<typeof SQLite.openDatabaseAsync>>;

export class ExpoSqliteDriver implements SqliteDriverPort {
  public async open(databaseName: string): Promise<SqliteConnectionPort> {
    const database = await SQLite.openDatabaseAsync(databaseName);
    // 默认是 journal_mode=delete + synchronous=FULL：每次 COMMIT 都要写回滚日志并多次
    // fsync，脏页还要双写。40 万行的全量同步约 800 次落库事务加约 800 次回收事务，
    // 在门店低端机的 eMMC 上这是主要的墙钟成本。对照 pos-ipad 的 pos-database.ts，
    // 这里补齐同一套 pragma。auto_vacuum 必须在建表前设置，且退役快照删除后要靠
    // incremental_vacuum 把页还给文件系统，否则文件会长期停在双倍体积。
    await database.execAsync(
      "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA auto_vacuum = INCREMENTAL; PRAGMA foreign_keys = ON;",
    );
    return new SerializedSqliteConnection(toNativeOperations(database));
  }
}

function toNativeOperations(database: ExpoDatabase): NativeSqliteOperations {
  return {
    exec: (sql) => database.execAsync(sql),
    run: async (sql, parameters) => {
      const result = await database.runAsync(sql, [...parameters]);
      return {
        changes: result.changes,
        lastInsertRowId: Number(result.lastInsertRowId),
      };
    },
    getFirst: <T extends object>(sql: string, parameters: readonly SqlValue[]) =>
      database.getFirstAsync<T>(sql, [...parameters]),
    getAll: <T extends object>(sql: string, parameters: readonly SqlValue[]) =>
      database.getAllAsync<T>(sql, [...parameters]),
    close: () => database.closeAsync(),
  };
}
