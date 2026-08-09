import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type { InstallmentSnapshot } from "../contracts/installments";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosDatabase } from "./pos-database";
import {
  INSTALLMENT_SENSITIVE_PAYLOAD_REVISION,
  SqliteInstallmentSnapshotRepository,
} from "./sqlite-installment-snapshot-repository";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqliteDriverPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const T0 = "2026-07-28T00:00:00.000Z";
const T1 = "2026-07-28T01:00:00.000Z";
const GUID_A = "10000000-0000-4000-8000-000000000001";
const GUID_B = "10000000-0000-4000-8000-000000000002";
const GUID_FAIL = "10000000-0000-4000-8000-000000000003";

test("M18 新增独立安全快照表且不改动 M6 legacy installments", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
    );
    await connection.run(
      `INSERT INTO installments (
        installment_id, remote_installment_id, state, customer_ciphertext,
        note_ciphertext, total_cents, paid_cents, created_at_iso, updated_at_iso
      ) VALUES ('legacy-1', 'remote-1', 'Active', ?, ?, 1000, 200, ?, ?)`,
      [new Uint8Array([1]), new Uint8Array([2]), T0, T0],
    );

    await applyMigrations(
      connection,
      () => T1,
      POS_DATABASE_MIGRATIONS.filter(
        (migration) => migration.version <= 18,
      ),
    );

    assert.equal(await schemaVersion(connection), 18);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installments WHERE installment_id = 'legacy-1'",
      ),
      1,
    );
    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info(installment_snapshots)",
    );
    assert.deepEqual(
      columns.map((column) => column.name),
      [
        "store_code",
        "installment_guid",
        "created_at_iso",
        "updated_at_iso",
        "total_cents",
        "down_payment_cents",
        "paid_cents",
        "balance_cents",
        "status",
        "encrypted_sensitive_revision",
        "sensitive_payload_ciphertext",
      ],
    );
    assert.equal(
      columns.some((column) =>
        /customer|phone|cashier|note|device|installment_number/u.test(
          column.name,
        ),
      ),
      false,
    );
  });
});

test("replaceForStore 加密敏感字段并提供确定性分页和门店限定 get", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new ReversingEncryptor();
    const repository = new SqliteInstallmentSnapshotRepository(
      connection,
      encryptor,
    );
    await repository.replaceForStore("STORE-1", [
      snapshot({
        installmentGuid: GUID_A,
        customerName: "Old Customer",
        createdAtIso: T0,
        updatedAtIso: T0,
      }),
      snapshot({
        installmentGuid: GUID_B,
        customerName: "New Customer",
        createdAtIso: T1,
        updatedAtIso: T1,
      }),
    ]);

    assert.equal(encryptor.encryptedPlaintexts.length, 2);
    const protectedPayload = JSON.parse(
      encryptor.encryptedPlaintexts[0] ?? "",
    ) as Record<string, unknown>;
    assert.equal(protectedPayload.storeCode, "STORE-1");
    assert.equal(protectedPayload.installmentGuid, GUID_A);
    assert.equal(protectedPayload.customerName, "Old Customer");
    assert.equal(protectedPayload.customerPhone, "0400000000");
    assert.equal(protectedPayload.note, "Private note");
    assert.equal(protectedPayload.cashierName, "Alice");
    assert.equal(protectedPayload.deviceCode, "IPAD-1");
    assert.equal(protectedPayload.installmentNumber, "INST-001");

    const stored = await connection.getFirst<Record<string, unknown>>(
      "SELECT * FROM installment_snapshots WHERE store_code = ? AND installment_guid = ?",
      ["STORE-1", GUID_A],
    );
    assert.ok(stored);
    assert.deepEqual(Object.keys(stored), [
      "store_code",
      "installment_guid",
      "created_at_iso",
      "updated_at_iso",
      "total_cents",
      "down_payment_cents",
      "paid_cents",
      "balance_cents",
      "status",
      "encrypted_sensitive_revision",
      "sensitive_payload_ciphertext",
    ]);
    assert.ok(stored.sensitive_payload_ciphertext instanceof Uint8Array);
    assert.equal(
      new TextDecoder()
        .decode(stored.sensitive_payload_ciphertext)
        .includes("Old Customer"),
      false,
    );

    assert.deepEqual(
      (await repository.listForStore("STORE-1", 1, 0)).map(
        (entry) => entry.installmentGuid,
      ),
      [GUID_B],
    );
    assert.deepEqual(
      (await repository.listForStore("STORE-1", 1, 1)).map(
        (entry) => entry.installmentGuid,
      ),
      [GUID_A],
    );
    assert.equal(
      (await repository.get("STORE-1", GUID_A))?.customerName,
      "Old Customer",
    );
    assert.equal(await repository.get("STORE-2", GUID_A), null);
  });
});

test("upsertForStore 增量更新当前页且任一写失败完整保留未返回历史", async () => {
  await withMigratedDatabase(async (connection) => {
    const repository = new SqliteInstallmentSnapshotRepository(
      connection,
      new ReversingEncryptor(),
    );
    await repository.replaceForStore("STORE-1", [
      snapshot({
        installmentGuid: GUID_A,
        customerName: "Historical A",
        updatedAtIso: T0,
      }),
      snapshot({
        installmentGuid: GUID_B,
        customerName: "Historical B",
        createdAtIso: T1,
        updatedAtIso: T1,
      }),
    ]);

    await repository.upsertForStore("STORE-1", [
      snapshot({
        installmentGuid: GUID_A,
        customerName: "Updated A",
        paidCents: 4_000,
        balanceCents: 6_000,
        updatedAtIso: T1,
      }),
    ]);
    assert.deepEqual(
      (await repository.listForStore("STORE-1", 20, 0)).map(
        (entry) => [
          entry.installmentGuid,
          entry.customerName,
          entry.paidCents,
        ],
      ),
      [
        [GUID_B, "Historical B", 2_000],
        [GUID_A, "Updated A", 4_000],
      ],
    );

    await connection.exec(`
      CREATE TRIGGER fail_installment_snapshot_page_insert
      BEFORE INSERT ON installment_snapshots
      FOR EACH ROW
      WHEN NEW.installment_guid = '${GUID_FAIL}'
      BEGIN
        SELECT RAISE(ABORT, 'INSTALLMENT_SNAPSHOT_PAGE_FAILURE');
      END;
    `);
    await assert.rejects(
      () =>
        repository.upsertForStore("STORE-1", [
          snapshot({
            installmentGuid: GUID_A,
            customerName: "Must Roll Back",
          }),
          snapshot({
            installmentGuid: GUID_FAIL,
            customerName: "Must Not Insert",
          }),
        ]),
      /INSTALLMENT_SNAPSHOT_PAGE_FAILURE/,
    );
    assert.equal(
      (await repository.get("STORE-1", GUID_A))?.customerName,
      "Updated A",
    );
    assert.equal(
      await repository.get("STORE-1", GUID_FAIL),
      null,
    );

    await assert.rejects(
      () =>
        repository.upsertForStore("STORE-1", [
          snapshot(),
          snapshot({ customerName: "Duplicate" }),
        ]),
      /duplicate/i,
    );
    assert.deepEqual(
      (await repository.listForStore("STORE-1", 20, 0)).map(
        (entry) => entry.installmentGuid,
      ),
      [GUID_B, GUID_A],
    );
  });
});

test("跨门店输入、校验、加密或 SQL 失败均完整保留旧快照", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new ReversingEncryptor();
    const repository = new SqliteInstallmentSnapshotRepository(
      connection,
      encryptor,
    );
    await repository.replaceForStore("STORE-1", [
      snapshot({ installmentGuid: GUID_A, customerName: "Original" }),
    ]);
    await repository.replaceForStore("STORE-2", [
      snapshot({
        installmentGuid: GUID_B,
        storeCode: "STORE-2",
        customerName: "Other Store",
      }),
    ]);

    const invalid = [
      snapshot({ storeCode: "STORE-2" }),
      snapshot({ totalCents: 1.5 }),
      snapshot({ paidCents: -1 }),
      snapshot({ balanceCents: Number.MAX_SAFE_INTEGER + 1 }),
      snapshot({ createdAtIso: "2026-07-28T00:00:00Z" }),
      snapshot({ createdAtIso: "2026-02-30T00:00:00.000Z" }),
      snapshot({ status: "Unknown" as InstallmentSnapshot["status"] }),
      snapshot({ encryptedSensitiveRevision: 2 }),
    ];
    for (const candidate of invalid) {
      await assert.rejects(
        repository.replaceForStore("STORE-1", [candidate]),
        /installment/i,
      );
    }
    await assert.rejects(
      repository.replaceForStore("STORE-1", [
        snapshot(),
        snapshot({ customerName: "Duplicate" }),
      ]),
      /duplicate/i,
    );

    const failingEncryptor: SensitivePayloadEncryptor = {
      encrypt(plaintext) {
        if (plaintext.includes("FAIL ENCRYPTION")) {
          throw new Error("TEST_ENCRYPTION_FAILURE");
        }
        return encryptor.encrypt(plaintext);
      },
      decrypt: (ciphertext) => encryptor.decrypt(ciphertext),
    };
    await assert.rejects(
      new SqliteInstallmentSnapshotRepository(
        connection,
        failingEncryptor,
      ).replaceForStore("STORE-1", [
        snapshot({ customerName: "FAIL ENCRYPTION" }),
      ]),
      /TEST_ENCRYPTION_FAILURE/,
    );

    await connection.exec(`
      CREATE TRIGGER fail_installment_snapshot_insert
      BEFORE INSERT ON installment_snapshots
      FOR EACH ROW
      WHEN NEW.installment_guid = '${GUID_FAIL}'
      BEGIN
        SELECT RAISE(ABORT, 'INSTALLMENT_SNAPSHOT_TEST_FAILURE');
      END;
    `);
    await assert.rejects(
      repository.replaceForStore("STORE-1", [
        snapshot({ installmentGuid: GUID_FAIL }),
      ]),
      /INSTALLMENT_SNAPSHOT_TEST_FAILURE/,
    );

    assert.deepEqual(
      (await repository.listForStore("STORE-1", 20, 0)).map(
        (entry) => [entry.installmentGuid, entry.customerName],
      ),
      [[GUID_A, "Original"]],
    );
    assert.deepEqual(
      (await repository.listForStore("STORE-2", 20, 0)).map(
        (entry) => entry.installmentGuid,
      ),
      [GUID_B],
    );
  });
});

test("密文与行 scope 绑定，且分页参数不接受非安全整数", async () => {
  await withMigratedDatabase(async (connection) => {
    const repository = new SqliteInstallmentSnapshotRepository(
      connection,
      new ReversingEncryptor(),
    );
    await repository.replaceForStore("STORE-1", [snapshot()]);
    await repository.replaceForStore("STORE-2", [
      snapshot({
        installmentGuid: GUID_B,
        storeCode: "STORE-2",
        customerName: "Other Store",
      }),
    ]);
    await connection.run(
      `UPDATE installment_snapshots
       SET sensitive_payload_ciphertext = (
         SELECT sensitive_payload_ciphertext
         FROM installment_snapshots
         WHERE store_code = 'STORE-2' AND installment_guid = ?
       )
       WHERE store_code = 'STORE-1' AND installment_guid = ?`,
      [GUID_B, GUID_A],
    );

    await assert.rejects(repository.get("STORE-1", GUID_A), /scope/i);
    await assert.rejects(
      repository.listForStore("STORE-1", 0, 0),
      /limit/i,
    );
    await assert.rejects(
      repository.listForStore("STORE-1", 10, -1),
      /offset/i,
    );
    await assert.rejects(
      repository.get("STORE-1", "not-a-guid"),
      /guid/i,
    );
  });
});

test("committed snapshot 不暴露可伪造的 prepared batch 写入 API", async () => {
  await withMigratedDatabase(async (connection) => {
    const repository = new SqliteInstallmentSnapshotRepository(
      connection,
      new ReversingEncryptor(),
    );

    assert.equal("prepareUpsertForStore" in repository, false);
    assert.equal("upsertPreparedInTransaction" in repository, false);
  });
});

test("PosDatabase 只通过 installmentSnapshots(encryptor) 暴露窄仓储", async () => {
  const driver = new SystemSqliteDriver();
  const database = await PosDatabase.open({
    databaseName: ":memory:",
    driver,
    keyProvider: {
      getOrCreateDatabaseKey: async () => "a".repeat(64),
    },
    nowIso: () => T0,
  });
  try {
    const repository = database.installmentSnapshots(
      new ReversingEncryptor(),
    );
    await repository.replaceForStore("STORE-1", [snapshot()]);
    assert.equal(
      (await repository.get("STORE-1", GUID_A))?.installmentGuid,
      GUID_A,
    );
  } finally {
    await database.close();
  }
});

function snapshot(
  overrides: Partial<InstallmentSnapshot> = {},
): InstallmentSnapshot {
  return {
    installmentGuid: GUID_A,
    installmentNumber: "INST-001",
    storeCode: "STORE-1",
    deviceCode: "IPAD-1",
    cashierName: "Alice",
    customerName: "Customer",
    customerPhone: "0400000000",
    createdAtIso: T0,
    totalCents: 10_000,
    downPaymentCents: 2_000,
    paidCents: 2_000,
    balanceCents: 8_000,
    status: "Active",
    updatedAtIso: T0,
    note: "Private note",
    encryptedSensitiveRevision: INSTALLMENT_SENSITIVE_PAYLOAD_REVISION,
    ...overrides,
  };
}

class ReversingEncryptor implements SensitivePayloadEncryptor {
  public readonly encryptedPlaintexts: string[] = [];

  public async encrypt(plaintext: string): Promise<Uint8Array> {
    this.encryptedPlaintexts.push(plaintext);
    return new TextEncoder().encode(reverse(plaintext));
  }

  public async decrypt(ciphertext: Uint8Array): Promise<string> {
    return reverse(new TextDecoder().decode(ciphertext));
  }
}

function reverse(value: string): string {
  return [...value].reverse().join("");
}

async function schemaVersion(
  connection: SqliteConnectionPort,
): Promise<number> {
  return Number(
    (
      await connection.getFirst<{ version: unknown }>(
        "SELECT MAX(version) AS version FROM schema_migrations",
      )
    )?.version,
  );
}

async function scalar(
  connection: SqliteConnectionPort,
  sql: string,
): Promise<number> {
  return Number(
    (
      await connection.getFirst<{ count: unknown }>(sql)
    )?.count,
  );
}

class SystemSqliteDriver implements SqliteDriverPort {
  public async open(_databaseName: string): Promise<SqliteConnectionPort> {
    return new SystemSqliteConnection(new DatabaseSync(":memory:"));
  }
}

class SystemSqliteConnection implements SqliteConnectionPort {
  public constructor(private readonly database: DatabaseSync) {
    this.database.exec("PRAGMA foreign_keys = ON;");
  }

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    const result = this.database
      .prepare(sql)
      .run(...parameters.map(toSqlInputValue));
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    // Node 内置 SQLite 不含 SQLCipher；仅为测试的精确探针提供有效版本。
    if (sql === "PRAGMA cipher_version;") {
      return { cipher_version: "4.6.1" } as unknown as T;
    }
    return (
      this.database
        .prepare(sql)
        .get(...parameters.map(toSqlInputValue)) as T | undefined
    ) ?? null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqlInputValue)) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE;");
    const transaction = new TransactionConnection(this.database);
    try {
      const result = await operation(transaction);
      this.database.exec("COMMIT;");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK;");
      throw error;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

class TransactionConnection extends SystemSqliteConnection {
  public override withExclusiveTransaction<T>(): Promise<T> {
    return Promise.reject(new Error("Nested test transaction."));
  }

  public override close(): Promise<void> {
    return Promise.reject(new Error("Transaction cannot close database."));
  }
}

async function withMigratedDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    await operation(connection);
  });
}

async function withDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  const connection = new SystemSqliteConnection(new DatabaseSync(":memory:"));
  try {
    await operation(connection);
  } finally {
    await connection.close();
  }
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}
