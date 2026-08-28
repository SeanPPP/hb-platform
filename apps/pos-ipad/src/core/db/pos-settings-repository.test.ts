import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import {
  DEFAULT_RECEIPT_PRINTER_SETTINGS,
  PosSettingsRepository,
  type ReceiptPrinterSettings,
} from "./pos-settings-repository";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "@hb/pos-db/core/db/types";

class SystemSqliteConnection implements SqliteConnectionPort {
  private tail: Promise<void> = Promise.resolve();

  public constructor(public readonly databasePath: string) {}

  public async exec(sql: string): Promise<void> { this.execute(sql); }
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    const lines = this.execute(`${bind(sql, parameters)}; SELECT changes() AS changes;`).trim().split("\n");
    return { changes: Number(lines.at(-1)), lastInsertRowId: 0 };
  }
  public async getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    const result = spawnSqlite(this.databasePath, ["-json"], bind(sql, parameters));
    if (result.status !== 0) throw new Error(result.stderr);
    const rows = result.stdout.trim() ? JSON.parse(result.stdout) as readonly T[] : [];
    return rows[0] ?? null;
  }
  public async getAll<T extends object>(): Promise<readonly T[]> { return []; }
  public async withExclusiveTransaction<T>(operation: (transaction: SqliteConnectionPort) => Promise<T>): Promise<T> {
    const previous = this.tail;
    let release!: () => void;
    this.tail = new Promise<void>((resolve) => { release = resolve; });
    await previous;
    try { return await operation(this); } finally { release(); }
  }
  public async close(): Promise<void> {}

  private execute(sql: string): string {
    const result = spawnSqlite(this.databasePath, [], sql);
    if (result.status !== 0) throw new Error(result.stderr);
    return result.stdout;
  }
}

function settings(overrides: Partial<ReceiptPrinterSettings> = {}): ReceiptPrinterSettings {
  return {
    printEnabled: true,
    drawerEnabled: true,
    peripheralId: "XP-N160I",
    paper: "80mm",
    locale: "zh-CN",
    brandName: "HB POS",
    storeName: "Hot Bargain",
    address: "1 Main Street",
    phone: "07 1234 5678",
    abn: "12 345 678 901",
    returnPolicy: "Change of mind returns within 14 days.",
    profileStoreCode: "BNE-01",
    ...overrides,
  };
}

test("旧 receipt_printer_v1 自动补新资料字段且不清空打印/钱箱/外设", async () => {
  await withDatabase(async (connection) => {
    const legacy = {
      printEnabled: true,
      drawerEnabled: true,
      peripheralId: "XP-N160I",
      paper: "58mm",
      locale: "zh-CN",
      brandName: "Hot Bargain",
      storeName: "Legacy Store",
      address: "1 Old St",
      phone: "0411 111 111",
      abn: "99 999 999 999",
    };
    await connection.run(
      "INSERT INTO app_settings (setting_key, setting_value, updated_at_iso) VALUES (?, ?, ?)",
      ["receipt_printer_v1", JSON.stringify(legacy), "2026-07-28T00:00:00.000Z"],
    );
    const repository = new PosSettingsRepository(connection, () => "2026-07-28T00:01:00.000Z");
    const current = await repository.getReceiptPrinterSettings();
    assert.equal(current.printEnabled, true);
    assert.equal(current.drawerEnabled, true);
    assert.equal(current.peripheralId, "XP-N160I");
    assert.equal(current.paper, "58mm");
    assert.equal(current.returnPolicy, "");
    assert.equal(current.profileStoreCode, "");
  });
});

test("returnPolicy 允许 CR/LF/TAB，其余控制字符拒绝；profileStoreCode 拒绝控制字符", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosSettingsRepository(connection, () => "2026-07-28T00:00:00.000Z");
    const saved = await repository.saveReceiptPrinterSettings(
      settings({ returnPolicy: "Line 1\r\nLine 2\tTabbed" }),
    );
    assert.equal(saved.returnPolicy, "Line 1\r\nLine 2\tTabbed");
    assert.deepEqual(await repository.getReceiptPrinterSettings(), saved);

    await assert.rejects(
      () => repository.saveReceiptPrinterSettings(settings({ returnPolicy: "Bad\u0007policy" })),
      /returnPolicy is invalid/,
    );
    await assert.rejects(
      () => repository.saveReceiptPrinterSettings(settings({ returnPolicy: "Bad\u007fpolicy" })),
      /returnPolicy is invalid/,
    );
    await assert.rejects(
      () => repository.saveReceiptPrinterSettings(settings({ profileStoreCode: "BNE\u001b01" })),
      /profileStoreCode is invalid/,
    );
  });
});

test("真实 SQLite：首次读取使用禁用打印和钱箱的默认值，完整配置跨重开保留 updated_at", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosSettingsRepository(connection, () => "2026-07-28T00:00:00.000Z");
    assert.deepEqual(await repository.getReceiptPrinterSettings(), DEFAULT_RECEIPT_PRINTER_SETTINGS);
    const expected = settings();
    assert.deepEqual(await repository.saveReceiptPrinterSettings(expected), expected);

    const reopened = new PosSettingsRepository(new SystemSqliteConnection(connection.databasePath), () => "2026-07-28T00:01:00.000Z");
    assert.deepEqual(await reopened.getReceiptPrinterSettings(), expected);
    const row = await connection.getFirst<{ updated_at_iso: string; setting_value: string }>("SELECT updated_at_iso, setting_value FROM app_settings WHERE setting_key = 'receipt_printer_v1'");
    assert.equal(row?.updated_at_iso, "2026-07-28T00:00:00.000Z");
    assert.deepEqual(JSON.parse(row?.setting_value ?? "{}"), expected);
  });
});

test("损坏或敏感 JSON fail-closed 为禁用默认值，非法写入不覆盖原有效配置", async () => {
  await withDatabase(async (connection) => {
    const repository = new PosSettingsRepository(connection, () => "2026-07-28T00:00:00.000Z");
    await repository.saveReceiptPrinterSettings(settings());
    await assert.rejects(
      () => repository.saveReceiptPrinterSettings({ ...settings(), drawerEnabled: true, peripheralId: null }),
      /Drawer can only be enabled/,
    );
    await assert.rejects(
      () => repository.saveReceiptPrinterSettings({ ...settings(), paper: "57mm" as "80mm" }),
      /paper is invalid/,
    );
    await assert.rejects(
      () => repository.saveReceiptPrinterSettings({ ...settings(), authorizationToken: "forbidden" } as unknown as ReceiptPrinterSettings),
      /unsupported or sensitive/,
    );
    assert.deepEqual(await repository.getReceiptPrinterSettings(), settings());

    await connection.run("UPDATE app_settings SET setting_value = ? WHERE setting_key = 'receipt_printer_v1'", ["{not json"]);
    assert.deepEqual(await repository.getReceiptPrinterSettings(), DEFAULT_RECEIPT_PRINTER_SETTINGS);
    await connection.run("UPDATE app_settings SET setting_value = ? WHERE setting_key = 'receipt_printer_v1'", [JSON.stringify({ ...settings(), cardReference: "forbidden" })]);
    assert.deepEqual(await repository.getReceiptPrinterSettings(), DEFAULT_RECEIPT_PRINTER_SETTINGS);
  });
});

test("并发保存以最后一次完整对象原子替换，禁止字段拼接", async () => {
  await withDatabase(async (connection) => {
    let now = 0;
    const repository = new PosSettingsRepository(connection, () => `2026-07-28T00:00:0${++now}.000Z`);
    const first = settings({ brandName: "First", paper: "58mm", locale: "en", drawerEnabled: false, peripheralId: null });
    const last = settings({ brandName: "Last", storeName: "Last store", phone: "0400 000 000" });
    await Promise.all([
      repository.saveReceiptPrinterSettings(first),
      repository.saveReceiptPrinterSettings(last),
    ]);

    assert.deepEqual(await repository.getReceiptPrinterSettings(), last);
    const row = await connection.getFirst<{ setting_value: string }>("SELECT setting_value FROM app_settings WHERE setting_key = 'receipt_printer_v1'");
    assert.deepEqual(JSON.parse(row?.setting_value ?? "{}"), last);
  });
});

async function withDatabase(operation: (connection: SystemSqliteConnection) => Promise<void>): Promise<void> {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-settings-"));
  const path = join(folder, "settings.db");
  try {
    const connection = new SystemSqliteConnection(path);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await operation(connection);
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
}

function spawnSqlite(databasePath: string, arguments_: readonly string[], input: string): Readonly<{ status: number | null; stdout: string; stderr: string }> {
  const result = spawnSync(process.env.SQLITE3_BINARY ?? "sqlite3", [...arguments_, databasePath], { input, encoding: "utf8" });
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
