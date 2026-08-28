import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosIpadOtaUpdatePolicyRepository } from "./pos-ipad-ota-update-policy-repository";
import { PosIpadUpdatePolicyRepository } from "./pos-ipad-update-policy-repository";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "@hb/pos-db/core/db/types";

import type { PosIpadUpdatePolicy } from "@/core/contracts/app-updates";
import type {
  AppUpdateCacheScope,
  PosIpadOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";

const policy: PosIpadUpdatePolicy = Object.freeze({
  enabled: true,
  minimumSupportedVersion: "1.2.0",
  latestVersion: "1.3.0",
  forceUpdate: false,
  appStoreUrl: "https://apps.apple.com/au/app/hot-bargain/id123456789",
  releaseMessage: "请在空闲时更新。",
});
const scope: AppUpdateCacheScope = Object.freeze({
  apiOrigin: "https://hotbargain.vip",
  storeCode: "S001",
  runtimeVersion: "1.2.3",
  installedVersion: "1.2.3",
});
const otaPolicy: PosIpadOtaUpdatePolicy = Object.freeze({
  state: "required",
  policyVersion: "policy-42",
  channel: "store-s001",
  runtimeVersion: "1.2.3",
  iosUpdateId: "123e4567-e89b-42d3-a456-426614174000",
  updateGroupId: "223e4567-e89b-42d3-a456-426614174000",
  releaseMessage: "必须更新。",
});

class SystemSqliteConnection implements SqliteConnectionPort {
  private tail: Promise<void> = Promise.resolve();

  public constructor(public readonly databasePath: string) {}

  public async exec(sql: string): Promise<void> {
    this.execute(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    this.execute(`${bind(sql, parameters)}; SELECT changes() AS changes;`);
    return { changes: 1, lastInsertRowId: 0 };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    const result = spawnSqlite(this.databasePath, ["-json"], bind(sql, parameters));
    if (result.status !== 0) throw new Error(result.stderr);
    const rows = result.stdout.trim() ? JSON.parse(result.stdout) as readonly T[] : [];
    return rows[0] ?? null;
  }

  public async getAll<T extends object>(): Promise<readonly T[]> {
    return [];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    const previous = this.tail;
    let release!: () => void;
    this.tail = new Promise<void>((resolve) => {
      release = resolve;
    });
    await previous;
    try {
      return await operation(this);
    } finally {
      release();
    }
  }

  public async close(): Promise<void> {}

  private execute(sql: string): void {
    const result = spawnSqlite(this.databasePath, [], sql);
    if (result.status !== 0) throw new Error(result.stderr);
  }
}

test("真实 SQLite：更新策略只以无敏感字段 JSON 缓存，跨重开可读", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosIpadUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      scope,
    );
    assert.equal(await repository.get(), null);
    assert.deepEqual(await repository.save(policy), policy);

    const row = await connection.getFirst<{
      setting_value: string;
      updated_at_iso: string;
    }>(
      "SELECT setting_value, updated_at_iso FROM app_settings WHERE setting_key LIKE 'pos_ipad_native_update_policy_v2:%'",
    );
    assert.equal(row?.updated_at_iso, "2026-07-28T00:00:00.000Z");
    assert.deepEqual(
      JSON.parse(row?.setting_value ?? "{}"),
      {
        scope: { ...scope, policyVersion: "native-v1" },
        policy,
      },
    );
    assert.deepEqual(
      await new PosIpadUpdatePolicyRepository(
        new SystemSqliteConnection(connection.databasePath),
        () => "2026-07-28T00:01:00.000Z",
        scope,
      ).get(),
      policy,
    );
  });
});

test("损坏、未知或敏感缓存一律不使用，非法保存不污染已验证缓存", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosIpadUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      scope,
    );
    await repository.save(policy);
    await assert.rejects(
      () => repository.save({ ...policy, authorizationToken: "forbidden" } as unknown as PosIpadUpdatePolicy),
      /unsupported field/,
    );
    assert.deepEqual(await repository.get(), policy);

    await connection.run(
      "UPDATE app_settings SET setting_value = ? WHERE setting_key LIKE 'pos_ipad_native_update_policy_v2:%'",
      ["{not json"],
    );
    assert.equal(await repository.get(), null);
    await connection.run(
      "UPDATE app_settings SET setting_value = ? WHERE setting_key LIKE 'pos_ipad_native_update_policy_v2:%'",
      [JSON.stringify({ ...policy, cardReference: "forbidden" })],
    );
    assert.equal(await repository.get(), null);
  });
});

test("native 缓存按 origin、门店、runtime、installedVersion 隔离", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosIpadUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      scope,
    );
    await repository.save(policy);

    for (const mismatch of [
      { ...scope, apiOrigin: "https://example.invalid" },
      { ...scope, storeCode: "S002" },
      { ...scope, runtimeVersion: "2.0.0" },
      { ...scope, installedVersion: "1.2.4" },
    ]) {
      assert.equal(
        await new PosIpadUpdatePolicyRepository(
          connection,
          () => "2026-07-28T00:00:00.000Z",
          mismatch,
        ).get(),
        null,
      );
    }
  });
});

test("OTA 缓存 record 再按 policyVersion 隔离，并用 scope pointer 找回最近策略", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosIpadOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      scope,
    );
    assert.equal(await repository.get(), null);
    assert.deepEqual(await repository.save(otaPolicy), otaPolicy);
    assert.deepEqual(await repository.get(), otaPolicy);

    const records = await connection.getFirst<{ total: number }>(
      "SELECT count(*) AS total FROM app_settings WHERE setting_key LIKE 'pos_ipad_ota_update_policy_v1:record:%'",
    );
    assert.equal(Number(records?.total), 1);

    const newer = Object.freeze({
      ...otaPolicy,
      policyVersion: "policy-43",
      iosUpdateId: "323e4567-e89b-42d3-a456-426614174000",
    });
    await repository.save(newer);
    assert.deepEqual(await repository.get(), newer);
    const recordsAfter = await connection.getFirst<{ total: number }>(
      "SELECT count(*) AS total FROM app_settings WHERE setting_key LIKE 'pos_ipad_ota_update_policy_v1:record:%'",
    );
    assert.equal(Number(recordsAfter?.total), 2);

    assert.equal(
      await new PosIpadOtaUpdatePolicyRepository(
        connection,
        () => "2026-07-28T00:00:00.000Z",
        { ...scope, storeCode: "S002" },
      ).get(),
      null,
    );
  });
});

async function withDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-update-policy-"));
  const path = join(folder, "update-policy.db");
  try {
    const connection = new SystemSqliteConnection(path);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await operation(connection);
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
}

function spawnSqlite(
  databasePath: string,
  arguments_: readonly string[],
  input: string,
): Readonly<{ status: number | null; stdout: string; stderr: string }> {
  const result = spawnSync(
    process.env.SQLITE3_BINARY ?? "sqlite3",
    [...arguments_, databasePath],
    { input, encoding: "utf8" },
  );
  if (result.error) throw result.error;
  return { status: result.status, stdout: result.stdout, stderr: result.stderr };
}

function bind(sql: string, parameters: readonly SqlValue[]): string {
  let index = 0;
  return sql.replace(/\?/g, () => sqliteLiteral(parameter(parameters, index++)));
}

function parameter(parameters: readonly SqlValue[], index: number): SqlValue {
  const value = parameters[index];
  if (value === undefined) throw new Error("Missing SQLite parameter.");
  return value;
}

function sqliteLiteral(value: SqlValue): string {
  if (value === null) return "NULL";
  if (typeof value === "number") return String(value);
  if (value instanceof Uint8Array) return `X'${Buffer.from(value).toString("hex")}'`;
  return `'${value.replace(/'/g, "''")}'`;
}
