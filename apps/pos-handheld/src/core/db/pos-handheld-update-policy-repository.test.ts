import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosHandheldOtaUpdatePolicyRepository } from "./pos-handheld-ota-update-policy-repository";
import { PosHandheldUpdatePolicyRepository } from "./pos-handheld-update-policy-repository";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

import type { PosHandheldUpdatePolicy } from "@/core/contracts/app-updates";
import type {
  NativeAppUpdateCacheScope,
  OtaAppUpdateCacheScope,
  PosHandheldOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";

const policy: PosHandheldUpdatePolicy = Object.freeze({
  enabled: true,
  state: "optional",
  policyVersion: "ios-native-130",
  platform: "iOS",
  required: false,
  latestVersion: "1.3.0",
  latestBuild: "130",
  minimumSupportedVersion: "1.2.0",
  distribution: "app-store",
  downloadUrl: "https://apps.apple.com/au/app/hot-bargain/id123456789",
  fileSize: null,
  sha256: null,
  packageName: null,
  signingCertificateSha256: null,
  bundleIdentifier: "com.hbweb.poshandheld",
  appStoreId: "123456789",
  releaseMessage: "请在空闲时更新。",
});
const nativeScope: NativeAppUpdateCacheScope = Object.freeze({
  kind: "native",
  apiOrigin: "https://hotbargain.vip",
  storeCode: "S001",
  appKey: "pos-handheld",
  platform: "iOS",
  installedVersion: "1.2.3",
  installedBuild: "42",
});
const otaScope: OtaAppUpdateCacheScope = Object.freeze({
  kind: "ota",
  apiOrigin: "https://hotbargain.vip",
  storeCode: "S001",
  appKey: "pos-handheld",
  projectId: "123e4567-e89b-42d3-a456-426614174000",
  projectName: "hb-pos-handheld",
  platform: "iOS",
  configuredChannel: "pos-handheld-production",
  runtimeVersion: "1.2.3",
  currentUpdateId: null,
  currentUpdateGroupId: null,
});
const otaPolicy: PosHandheldOtaUpdatePolicy = Object.freeze({
  state: "required",
  policyVersion: "policy-42",
  appKey: "pos-handheld",
  projectName: "hb-pos-handheld",
  platform: "iOS",
  required: true,
  channel: "pos-handheld-production",
  runtimeVersion: "1.2.3",
  updateId: "123e4567-e89b-42d3-a456-426614174000",
  updateGroupId: "223e4567-e89b-42d3-a456-426614174000",
  releaseMessage: "必须更新。",
});
const requiredNativePolicy: PosHandheldUpdatePolicy = Object.freeze({
  ...policy,
  state: "required",
  policyVersion: "ios-native-required-43",
  required: true,
  latestVersion: "1.2.3",
  latestBuild: "43",
  minimumSupportedVersion: "1.2.3",
  releaseMessage: "必须更新。",
});
const currentNativeScope: NativeAppUpdateCacheScope = Object.freeze({
  ...nativeScope,
  installedVersion: policy.latestVersion ?? "1.3.0",
  installedBuild: policy.latestBuild ?? "130",
});
const noneNativePolicy: PosHandheldUpdatePolicy = Object.freeze({
  enabled: true,
  state: "none",
  policyVersion: "none",
  platform: "iOS",
  required: false,
  latestVersion: null,
  latestBuild: null,
  minimumSupportedVersion: null,
  distribution: null,
  downloadUrl: null,
  fileSize: null,
  sha256: null,
  packageName: null,
  signingCertificateSha256: null,
  bundleIdentifier: null,
  appStoreId: null,
  releaseMessage: null,
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
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      nativeScope,
    );
    assert.equal(await repository.get(), null);
    assert.deepEqual(await repository.save(policy), policy);

    const row = await connection.getFirst<{
      setting_value: string;
      updated_at_iso: string;
    }>(
      "SELECT setting_value, updated_at_iso FROM app_settings WHERE setting_key LIKE 'pos_handheld_native_update_policy_v4:%'",
    );
    assert.equal(row?.updated_at_iso, "2026-07-28T00:00:00.000Z");
    assert.deepEqual(
      JSON.parse(row?.setting_value ?? "{}"),
      {
        scope: { ...nativeScope, policyVersion: "native-v3" },
        policy,
      },
    );
    assert.deepEqual(
      await new PosHandheldUpdatePolicyRepository(
        new SystemSqliteConnection(connection.databasePath),
        () => "2026-07-28T00:01:00.000Z",
        nativeScope,
      ).get(),
      policy,
    );
  });
});

test("损坏、未知或敏感缓存一律不使用，非法保存不污染已验证缓存", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      nativeScope,
    );
    await repository.save(policy);
    await assert.rejects(
      () => repository.save({ ...policy, authorizationToken: "forbidden" } as unknown as PosHandheldUpdatePolicy),
      /unsupported field/,
    );
    assert.deepEqual(await repository.get(), policy);

    await connection.run(
      "UPDATE app_settings SET setting_value = ? WHERE setting_key LIKE 'pos_handheld_native_update_policy_v4:%'",
      ["{not json"],
    );
    assert.equal(await repository.get(), null);
    await connection.run(
      "UPDATE app_settings SET setting_value = ? WHERE setting_key LIKE 'pos_handheld_native_update_policy_v4:%'",
      [JSON.stringify({ ...policy, cardReference: "forbidden" })],
    );
    assert.equal(await repository.get(), null);
  });
});

test("native 缓存按 origin、门店、appKey、platform、installedVersion/build 隔离", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      nativeScope,
    );
    await repository.save(policy);

    for (const mismatch of [
      { ...nativeScope, apiOrigin: "https://example.invalid" },
      { ...nativeScope, storeCode: "S002" },
      { ...nativeScope, platform: "Android" as const },
      { ...nativeScope, installedVersion: "1.2.4" },
      { ...nativeScope, installedBuild: "43" },
    ]) {
      assert.equal(
        await new PosHandheldUpdatePolicyRepository(
          connection,
          () => "2026-07-28T00:00:00.000Z",
          mismatch,
        ).get(),
        null,
      );
    }
  });
});

test("同 appVersion 升级 installed build 后不恢复旧 required，当前 target 也防御拒绝", async () => {
  await withDatabase(async (connection) => {
    const oldBuild = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      nativeScope,
    );
    await oldBuild.save(requiredNativePolicy);
    assert.deepEqual(await oldBuild.get(), requiredNativePolicy);

    const installedTarget = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:01:00.000Z",
      { ...nativeScope, installedBuild: "43" },
    );
    assert.equal(await installedTarget.get(), null);

    // 即使错误迁移或远端竞态把旧 required 写到当前 identity，读取仍须按已安装 target 拒绝。
    await installedTarget.save(requiredNativePolicy);
    assert.equal(await installedTarget.get(), null);
  });
});

test("native optional target 已等于当前安装版本与 build 时不恢复", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      currentNativeScope,
    );
    await repository.save(policy);
    assert.equal(await repository.get(), null);
  });
});

test("native optional target 版本或 build 落后于当前安装时不恢复", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      currentNativeScope,
    );
    for (const stalePolicy of [
      { ...policy, latestVersion: "1.2.9", latestBuild: "129" },
      { ...policy, latestBuild: "129" },
    ]) {
      await repository.save(stalePolicy);
      assert.equal(await repository.get(), null);
    }
  });
});

test("native optional target 真正较新时仍恢复", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      currentNativeScope,
    );
    const newerPolicy = Object.freeze({
      ...policy,
      latestVersion: "1.3.1",
      latestBuild: "131",
    });
    await repository.save(newerPolicy);
    assert.deepEqual(await repository.get(), newerPolicy);
  });
});

test("native none 策略不含 target，仍可作为合法缓存恢复", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      currentNativeScope,
    );
    await repository.save(noneNativePolicy);
    assert.deepEqual(await repository.get(), noneNativePolicy);
  });
});

test("OTA 缓存 record 再按 policyVersion 隔离，并用 scope pointer 找回最近策略", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      otaScope,
    );
    assert.equal(await repository.get(), null);
    assert.deepEqual(await repository.save(otaPolicy), otaPolicy);
    assert.deepEqual(await repository.get(), otaPolicy);

    const records = await connection.getFirst<{ total: number }>(
      "SELECT count(*) AS total FROM app_settings WHERE setting_key LIKE 'pos_handheld_ota_update_policy_v4:record:%'",
    );
    assert.equal(Number(records?.total), 1);

    const newer = Object.freeze({
      ...otaPolicy,
      policyVersion: "policy-43",
      updateId: "323e4567-e89b-42d3-a456-426614174000",
    });
    await repository.save(newer);
    assert.deepEqual(await repository.get(), newer);
    const recordsAfter = await connection.getFirst<{ total: number }>(
      "SELECT count(*) AS total FROM app_settings WHERE setting_key LIKE 'pos_handheld_ota_update_policy_v4:record:%'",
    );
    assert.equal(Number(recordsAfter?.total), 2);

    assert.equal(
      await new PosHandheldOtaUpdatePolicyRepository(
        connection,
        () => "2026-07-28T00:00:00.000Z",
        { ...otaScope, storeCode: "S002" },
      ).get(),
      null,
    );
  });
});

test("OTA 缓存 record 额外按目标 release channel 与 update identity 隔离", async () => {
  await withDatabase(async (connection) => {
    const productionScope = Object.freeze({
      ...otaScope,
      configuredChannel: "pos-handheld-production",
    });
    const legacy = Object.freeze({
      ...otaPolicy,
      channel: "pos-handheld-production",
    });
    const release = Object.freeze({
      ...legacy,
      channel:
        "pos-handheld-production-ios-release-20260827t101500z-a1b2c3",
      updateId: "323e4567-e89b-42d3-a456-426614174000",
      updateGroupId: "423e4567-e89b-42d3-a456-426614174000",
    });
    const repository = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-08-27T10:15:00.000Z",
      productionScope,
    );

    await repository.save(legacy);
    await repository.save(release);
    assert.deepEqual(await repository.get(), release);

    const records = await connection.getFirst<{ total: number }>(
      "SELECT count(*) AS total FROM app_settings WHERE setting_key LIKE 'pos_handheld_ota_update_policy_v4:record:%'",
    );
    assert.equal(Number(records?.total), 2);

    await repository.save(legacy);
    assert.deepEqual(await repository.get(), legacy);
    const recordsAfterLegacyRestore = await connection.getFirst<{ total: number }>(
      "SELECT count(*) AS total FROM app_settings WHERE setting_key LIKE 'pos_handheld_ota_update_policy_v4:record:%'",
    );
    assert.equal(Number(recordsAfterLegacyRestore?.total), 2);
  });
});

test("OTA 当前 updateId 已等于缓存 target 时拒绝 cached required", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      { ...otaScope, currentUpdateId: otaPolicy.updateId },
    );
    await repository.save(otaPolicy);
    assert.equal(await repository.get(), null);
  });
});

test("OTA 当前 group 已等于缓存 target group 时拒绝 cached required", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      { ...otaScope, currentUpdateGroupId: otaPolicy.updateGroupId },
    );
    await repository.save(otaPolicy);
    assert.equal(await repository.get(), null);
  });
});

test("preview 永不恢复后台 OTA 缓存，且 production 与不同 project 不复用", async () => {
  await withDatabase(async (connection) => {
    const previewScope = Object.freeze({
      ...otaScope,
      configuredChannel: "preview",
    });
    const previewPolicy = Object.freeze({
      ...otaPolicy,
      channel: "preview",
    });
    const preview = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:00:00.000Z",
      previewScope,
    );
    await preview.save(previewPolicy);
    assert.equal(await preview.get(), null);

    for (const mismatch of [
      { ...previewScope, configuredChannel: "production" },
      {
        ...previewScope,
        projectId: "323e4567-e89b-42d3-a456-426614174000",
      },
    ]) {
      assert.equal(
        await new PosHandheldOtaUpdatePolicyRepository(
          connection,
          () => "2026-07-28T00:01:00.000Z",
          mismatch,
        ).get(),
        null,
      );
    }

    const wrongChannel = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:02:00.000Z",
      { ...previewScope, configuredChannel: "production" },
    );
    await wrongChannel.save(previewPolicy);
    assert.equal(await wrongChannel.get(), null);

    const wrongProject = new PosHandheldOtaUpdatePolicyRepository(
      connection,
      () => "2026-07-28T00:03:00.000Z",
      { ...previewScope, projectName: "another-project" },
    );
    await wrongProject.save(previewPolicy);
    assert.equal(await wrongProject.get(), null);
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
