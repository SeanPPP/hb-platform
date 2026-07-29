import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type { PersistedInstallmentAction } from "../runtime/production-installment-runtime";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosDatabase } from "./pos-database";
import { SqliteInstallmentActionStore } from "./sqlite-installment-action-store";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqliteDriverPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const NOW = "2026-07-29T00:00:00.000Z";
const STORE = "STORE-1";
const DEVICE = "IPAD-1";
const ACTION_A = "10000000-0000-4000-8000-000000000001";
const ACTION_B = "10000000-0000-4000-8000-000000000002";
const ACTION_C = "10000000-0000-4000-8000-000000000003";
const INSTALLMENT = "20000000-0000-4000-8000-000000000001";
const PAYMENT = "30000000-0000-4000-8000-000000000001";
const LINE = "40000000-0000-4000-8000-000000000001";

test("M20/M21 分段建立耐久 action ledger 与不可变 CAS triggers，失败不推进版本", async () => {
  await withDatabase(async (connection) => {
    const throughM19 = POS_DATABASE_MIGRATIONS.filter(
      (migration) => migration.version <= 19,
    );
    await applyMigrations(connection, () => NOW, throughM19);
    const m20 = POS_DATABASE_MIGRATIONS.find(
      (migration) => migration.version === 20,
    );
    const m21 = POS_DATABASE_MIGRATIONS.find(
      (migration) => migration.version === 21,
    );
    assert.ok(m20);
    assert.ok(m21);

    const throughM20 = POS_DATABASE_MIGRATIONS.filter(
      (migration) => migration.version <= 20,
    );
    const throughM21 = POS_DATABASE_MIGRATIONS.filter(
      (migration) => migration.version <= 21,
    );
    await applyMigrations(connection, () => NOW, throughM20);
    assert.equal(await schemaVersion(connection), 20);
    assert.equal(await tableExists(connection, "installment_actions"), true);
    assert.equal(
      await triggerExists(
        connection,
        "trg_installment_actions_state_transition",
      ),
      false,
    );

    await assert.rejects(
      () =>
        applyMigrations(connection, () => NOW, [
          ...throughM20,
          {
            ...m21,
            sql: `${m21.sql}\nCREATE TABL invalid_m21;`,
          },
        ]),
      /syntax|near/i,
    );
    assert.equal(await schemaVersion(connection), 20);
    assert.equal(
      await triggerExists(
        connection,
        "trg_installment_actions_state_transition",
      ),
      false,
    );

    await applyMigrations(connection, () => NOW, throughM21);
    assert.equal(await schemaVersion(connection), 21);
    assert.equal(
      await triggerExists(
        connection,
        "trg_installment_actions_state_transition",
      ),
      true,
    );
    assert.equal(
      await triggerExists(
        connection,
        "trg_installment_actions_no_delete",
      ),
      true,
    );
  });
});

test("真实 SQLite：createIfNone 原子保留 terminal 唯一 blocking，冻结 command 只存认证密文", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const store = new SqliteInstallmentActionStore(
      connection,
      encryptor,
      () => NOW,
    );
    const original = createCandidate();
    assert.deepEqual(await store.createIfNone(original), {
      created: true,
      action: original,
    });
    assert.deepEqual(
      await store.loadBlocking({ storeCode: STORE, deviceCode: DEVICE }),
      original,
    );

    const row = await connection.getFirst<Record<string, unknown>>(
      "SELECT * FROM installment_actions WHERE action_id = ?",
      [ACTION_A],
    );
    assert.ok(row);
    assert.deepEqual(Object.keys(row), [
      "action_id",
      "store_code",
      "device_code",
      "installment_guid",
      "action_kind",
      "idempotency_key",
      "payment_guid",
      "payment_method",
      "amount_cents",
      "state",
      "resolution",
      "payload_revision",
      "command_ciphertext",
      "created_at_iso",
      "updated_at_iso",
      "resolved_at_iso",
    ]);
    assert.ok(row.command_ciphertext instanceof Uint8Array);
    const plainColumns = JSON.stringify(row);
    assert.equal(plainColumns.includes("Private Customer"), false);
    assert.equal(plainColumns.includes("0400000000"), false);
    assert.equal(plainColumns.includes("Private note"), false);
    assert.equal(plainColumns.includes("cartFingerprint"), false);

    const envelope = JSON.parse(
      encryptor.encryptedPlaintexts[0] ?? "",
    ) as Record<string, unknown>;
    assert.deepEqual(envelope.aad, {
      revision: 1,
      storeCode: STORE,
      deviceCode: DEVICE,
      actionId: ACTION_A,
    });
    assert.equal(
      JSON.stringify(envelope).includes("Private Customer"),
      true,
    );
    assert.equal(
      JSON.stringify(envelope).includes("intentFingerprint"),
      true,
    );

    const blockedCandidate = repaymentCandidate({
      actionId: ACTION_B,
    });
    assert.deepEqual(await store.createIfNone(blockedCandidate), {
      created: false,
      action: original,
    });
    assert.equal(encryptor.encryptedPlaintexts.length, 1);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installment_actions",
      ),
      1,
    );
    assert.equal(
      await store.loadBlocking({
        storeCode: STORE,
        deviceCode: "IPAD-OTHER",
      }),
      null,
    );

    const otherTerminal = repaymentCandidate({
      actionId: ACTION_B,
      deviceCode: "IPAD-OTHER",
    });
    assert.equal((await store.createIfNone(otherTerminal)).created, true);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installment_actions",
      ),
      2,
    );
  });
});

test("真实 SQLite：旧 V1 密文缺少支付选择字段仍按原形恢复", async () => {
  await withMigratedDatabase(async (connection) => {
    const legacy = createCandidate();
    const ciphertext = new Uint8Array([71]);
    const plaintext = JSON.stringify({
      format: "hb-pos-installment-action-v1",
      aad: {
        revision: 1,
        storeCode: STORE,
        deviceCode: DEVICE,
        actionId: ACTION_A,
      },
      action: legacy.action,
      command: legacy.command,
      intentFingerprint: legacy.intentFingerprint,
    });
    await connection.run(
      `INSERT INTO installment_actions (
        action_id, store_code, device_code, installment_guid,
        action_kind, idempotency_key, payment_guid, payment_method,
        amount_cents, state, resolution, payload_revision,
        command_ciphertext, created_at_iso, updated_at_iso,
        resolved_at_iso
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      [
        ACTION_A,
        STORE,
        DEVICE,
        INSTALLMENT,
        "create",
        ACTION_A,
        PAYMENT,
        "card",
        2_000,
        "Created",
        null,
        1,
        ciphertext,
        NOW,
        NOW,
        null,
      ],
    );
    const store = new SqliteInstallmentActionStore(
      connection,
      new FixtureDecryptor(ciphertext, plaintext),
      () => NOW,
    );

    const restored = await store.loadBlocking({
      storeCode: STORE,
      deviceCode: DEVICE,
    });

    assert.deepEqual(restored, legacy);
    assert.ok(restored?.command.kind === "create");
    assert.equal(
      Object.hasOwn(restored.command, "cardProvider"),
      false,
    );
    assert.equal(
      Object.hasOwn(restored.command, "cashTenderedCents"),
      false,
    );
  });
});

test("真实 SQLite：V1 方法特定字段只保留 card provider 或现金实收", async () => {
  const cases = [
    paymentSelectionCandidate(createCandidate(), {
      method: "card",
      selection: {
        cardProvider: "square",
      },
    }),
    paymentSelectionCandidate(
      repaymentCandidate({ actionId: ACTION_B }),
      {
        method: "card",
        selection: {
          cardProvider: "linkly-cloud",
        },
      },
    ),
    paymentSelectionCandidate(
      repaymentCandidate({ actionId: ACTION_B }),
      {
        method: "cash",
        selection: {
          cashTenderedCents: 800,
        },
      },
    ),
    paymentSelectionCandidate(
      repaymentCandidate({ actionId: ACTION_B }),
      {
        method: "voucher",
        selection: {},
      },
    ),
  ] as const;

  for (const candidate of cases) {
    await withMigratedDatabase(async (connection) => {
      const encryptor = new RecordingEncryptor();
      const store = new SqliteInstallmentActionStore(
        connection,
        encryptor,
        () => NOW,
      );

      assert.deepEqual(await store.createIfNone(candidate), {
        created: true,
        action: candidate,
      });
      assert.deepEqual(
        await store.loadBlocking({
          storeCode: STORE,
          deviceCode: DEVICE,
        }),
        candidate,
      );
      const envelope = JSON.parse(
        encryptor.encryptedPlaintexts[0] ?? "",
      ) as {
        command?: unknown;
      };
      assert.deepEqual(envelope.command, candidate.command);
      assert.equal(
        (
          await connection.getFirst<{ payload_revision: unknown }>(
            `SELECT payload_revision
             FROM installment_actions
             WHERE action_id = ?`,
            [candidate.action.actionId],
          )
        )?.payload_revision,
        1,
      );
    });
  }
});

test("真实 SQLite：拒绝不完整、越界或与支付方式冲突的新支付选择", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const store = new SqliteInstallmentActionStore(
      connection,
      encryptor,
      () => NOW,
    );
    const card = createCandidate();
    const cash = repaymentCandidate({ actionId: ACTION_B });
    const invalidCandidates = [
      paymentSelectionCandidate(card, {
        method: "card",
        selection: { cashTenderedCents: 2_000 },
      }),
      paymentSelectionCandidate(card, {
        method: "card",
        selection: {
          cardProvider: null,
        },
      }),
      paymentSelectionCandidate(card, {
        method: "card",
        selection: {
          cardProvider: "adyen",
        },
      }),
      paymentSelectionCandidate(card, {
        method: "card",
        selection: {
          cardProvider: "square",
          cashTenderedCents: 2_001,
        },
      }),
      paymentSelectionCandidate(cash, {
        method: "cash",
        selection: {
          cashTenderedCents: 499,
        },
      }),
      paymentSelectionCandidate(cash, {
        method: "cash",
        selection: {
          cashTenderedCents: Number.MAX_SAFE_INTEGER + 1,
        },
      }),
      paymentSelectionCandidate(cash, {
        method: "cash",
        selection: {
          cardProvider: "square",
        },
      }),
      paymentSelectionCandidate(cash, {
        method: "cash",
        selection: {
          cardProvider: null,
          cashTenderedCents: 500,
        },
      }),
      paymentSelectionCandidate(cash, {
        method: "voucher",
        selection: {
          cashTenderedCents: 501,
        },
      }),
      paymentSelectionCandidate(cash, {
        method: "voucher",
        selection: {
          cardProvider: "linkly-cloud",
        },
      }),
      cancelCandidateWithPaymentSelection(),
    ];

    for (const candidate of invalidCandidates) {
      await assert.rejects(
        () => store.createIfNone(candidate),
        /command|payment selection|cash tendered/i,
      );
    }
    assert.equal(encryptor.encryptedPlaintexts.length, 0);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installment_actions",
      ),
      0,
    );
  });
});

test("真实 SQLite：坏 blocking 密文使 createIfNone fail closed 且不插候选", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const store = new SqliteInstallmentActionStore(
      connection,
      encryptor,
      () => NOW,
    );
    await store.createIfNone(createCandidate());
    // 模拟磁盘/旧构建损坏；生产 M21 trigger 本身会拒绝普通 SQL 换密文。
    await connection.exec("DROP TRIGGER trg_installment_actions_immutable;");
    await connection.run(
      `UPDATE installment_actions
       SET command_ciphertext = ?
       WHERE action_id = ?`,
      [new Uint8Array([255]), ACTION_A],
    );

    await assert.rejects(
      () =>
        store.loadBlocking({
          storeCode: STORE,
          deviceCode: DEVICE,
        }),
      /ciphertext|installment action/i,
    );
    await assert.rejects(
      () =>
        store.createIfNone(
          repaymentCandidate({ actionId: ACTION_B }),
        ),
      /ciphertext|installment action/i,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installment_actions",
      ),
      1,
    );
  });
});

test("真实 SQLite：candidate 加密或 SQL 插入失败时不留下 blocking action", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const store = new SqliteInstallmentActionStore(
      connection,
      encryptor,
      () => NOW,
    );
    encryptor.failEncryption = true;
    await assert.rejects(
      () => store.createIfNone(createCandidate()),
      /TEST_ENCRYPTION_FAILURE/,
    );
    encryptor.failEncryption = false;
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installment_actions",
      ),
      0,
    );

    await connection.exec(`
      CREATE TRIGGER fail_installment_action_insert
      BEFORE INSERT ON installment_actions
      BEGIN
        SELECT RAISE(ABORT, 'INSTALLMENT_ACTION_INSERT_FAILURE');
      END;
    `);
    await assert.rejects(
      () => store.createIfNone(createCandidate()),
      /INSTALLMENT_ACTION_INSERT_FAILURE/,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM installment_actions",
      ),
      0,
    );
  });
});

test("真实 SQLite：transition/complete 按 scope+expectedState CAS，resolved fact 保留且不再 blocking", async () => {
  await withMigratedDatabase(async (connection) => {
    const store = new SqliteInstallmentActionStore(
      connection,
      new RecordingEncryptor(),
      () => NOW,
    );
    await store.createIfNone(createCandidate());
    await assert.rejects(
      () =>
        store.transition({
          actionId: ACTION_A,
          expectedState: "Created",
          nextState: "Approved",
          terminal: { storeCode: STORE, deviceCode: DEVICE },
        }),
      /transition|state/i,
    );
    await assert.rejects(
      () =>
        store.transition({
          actionId: ACTION_A,
          expectedState: "Created",
          nextState: "ProviderPending",
          terminal: { storeCode: STORE, deviceCode: "IPAD-OTHER" },
        }),
      /state|scope|action/i,
    );

    let current = await store.transition({
      actionId: ACTION_A,
      expectedState: "Created",
      nextState: "ProviderPending",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    assert.equal(current.state, "ProviderPending");
    await assert.rejects(
      () =>
        store.transition({
          actionId: ACTION_A,
          expectedState: "Created",
          nextState: "ProviderPending",
          terminal: { storeCode: STORE, deviceCode: DEVICE },
        }),
      /state|CAS|action/i,
    );

    current = await store.transition({
      actionId: ACTION_A,
      expectedState: "ProviderPending",
      nextState: "Unknown",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    assert.equal(current.state, "Unknown");
    current = await store.transition({
      actionId: ACTION_A,
      expectedState: "Unknown",
      nextState: "Approved",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    assert.equal(current.state, "Approved");

    await connection.exec(`
      CREATE TRIGGER fail_installment_backend_pending
      AFTER UPDATE OF state ON installment_actions
      WHEN NEW.state = 'BackendPending'
      BEGIN
        SELECT RAISE(ABORT, 'INSTALLMENT_BACKEND_PENDING_FAILURE');
      END;
    `);
    await assert.rejects(
      () =>
        store.transition({
          actionId: ACTION_A,
          expectedState: "Approved",
          nextState: "BackendPending",
          terminal: { storeCode: STORE, deviceCode: DEVICE },
        }),
      /INSTALLMENT_BACKEND_PENDING_FAILURE/,
    );
    assert.equal(
      (
        await store.loadBlocking({
          storeCode: STORE,
          deviceCode: DEVICE,
        })
      )?.state,
      "Approved",
    );
    await connection.exec("DROP TRIGGER fail_installment_backend_pending;");

    current = await store.transition({
      actionId: ACTION_A,
      expectedState: "Approved",
      nextState: "BackendPending",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    assert.equal(current.state, "BackendPending");
    await store.complete({
      actionId: ACTION_A,
      expectedState: "BackendPending",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    assert.equal(
      await store.loadBlocking({
        storeCode: STORE,
        deviceCode: DEVICE,
      }),
      null,
    );
    const resolved = await connection.getFirst<{
      state: unknown;
      resolution: unknown;
    }>(
      `SELECT state, resolution
       FROM installment_actions
       WHERE action_id = ?`,
      [ACTION_A],
    );
    assert.deepEqual(resolved === null ? null : { ...resolved }, {
      state: "BackendPending",
      resolution: "Completed",
    });

    await assert.rejects(
      () =>
        connection.run(
          `UPDATE installment_actions
           SET installment_guid = ?
           WHERE action_id = ?`,
          [ACTION_B, ACTION_A],
        ),
      /immutable/i,
    );
    await assert.rejects(
      () =>
        connection.run(
          "DELETE FROM installment_actions WHERE action_id = ?",
          [ACTION_A],
        ),
      /delete|immutable/i,
    );
    await assert.rejects(
      () => store.createIfNone(createCandidate()),
      /unique|constraint/i,
    );
  });
});

test("真实 SQLite：decline 仅释放 ProviderPending/Unknown 并保留 Declined 事实", async () => {
  await withMigratedDatabase(async (connection) => {
    const store = new SqliteInstallmentActionStore(
      connection,
      new RecordingEncryptor(),
      () => NOW,
    );
    const candidate = repaymentCandidate({ actionId: ACTION_C });
    await store.createIfNone(candidate);
    await assert.rejects(
      () =>
        store.decline({
          actionId: ACTION_C,
          expectedState: "Unknown",
          terminal: { storeCode: STORE, deviceCode: DEVICE },
        }),
      /state|CAS|action/i,
    );
    await store.transition({
      actionId: ACTION_C,
      expectedState: "Created",
      nextState: "ProviderPending",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    await store.decline({
      actionId: ACTION_C,
      expectedState: "ProviderPending",
      terminal: { storeCode: STORE, deviceCode: DEVICE },
    });
    assert.equal(
      await store.loadBlocking({
        storeCode: STORE,
        deviceCode: DEVICE,
      }),
      null,
    );
    assert.equal(
      (
        await connection.getFirst<{ resolution: unknown }>(
          "SELECT resolution FROM installment_actions WHERE action_id = ?",
          [ACTION_C],
        )
      )?.resolution,
      "Declined",
    );
    await assert.rejects(
      () =>
        store.decline({
          actionId: ACTION_C,
          expectedState: "ProviderPending",
          terminal: { storeCode: STORE, deviceCode: DEVICE },
        }),
      /state|CAS|action/i,
    );
  });
});

test("PosDatabase.installmentActions(encryptor) 暴露冻结 runtime Port", async () => {
  const database = await PosDatabase.open({
    databaseName: ":memory:",
    driver: new SystemSqliteDriver(),
    keyProvider: {
      getOrCreateDatabaseKey: async () => "a".repeat(64),
    },
    nowIso: () => NOW,
  });
  try {
    const store = database.installmentActions(
      new RecordingEncryptor(),
    );
    assert.ok(store instanceof SqliteInstallmentActionStore);
    assert.equal(
      (await store.createIfNone(createCandidate())).created,
      true,
    );
    assert.deepEqual(
      await store.loadBlocking({
        storeCode: STORE,
        deviceCode: DEVICE,
      }),
      createCandidate(),
    );
  } finally {
    await database.close();
  }
});

function createCandidate(): PersistedInstallmentAction {
  return Object.freeze({
    action: Object.freeze({
      actionId: ACTION_A,
      idempotencyKey: ACTION_A,
      kind: "create" as const,
      installmentGuid: INSTALLMENT,
      paymentGuid: PAYMENT,
      method: "card" as const,
      amountCents: 2_000,
    }),
    command: Object.freeze({
      deviceCode: DEVICE,
      cashierId: "cashier-1",
      cashierName: "Alice",
      kind: "create" as const,
      installmentGuid: INSTALLMENT,
      createdAtIso: NOW,
      totalCents: 10_000,
      downPaymentCents: 2_000,
      lines: Object.freeze([
        Object.freeze({
          installmentLineGuid: LINE,
          productCode: "P-1",
          referenceCode: null,
          displayName: "Tea",
          lookupCode: "TEA",
          quantity: "1",
          unitPriceCents: 10_000,
          discountCents: 0,
          actualAmountCents: 10_000,
          itemNumber: "I-1",
        }),
      ]),
      customerName: "Private Customer",
      customerPhone: "0400000000",
      note: "Private note",
      cartFingerprint: '{"private":"cartFingerprint"}',
      draftRevision: 1,
    }),
    deviceCode: DEVICE,
    intentFingerprint: '{"private":"intentFingerprint"}',
    state: "Created" as const,
    storeCode: STORE,
  });
}

function repaymentCandidate(
  overrides: Readonly<{
    actionId: string;
    deviceCode?: string;
  }>,
): PersistedInstallmentAction {
  const deviceCode = overrides.deviceCode ?? DEVICE;
  return Object.freeze({
    action: Object.freeze({
      actionId: overrides.actionId,
      idempotencyKey: overrides.actionId,
      kind: "repayment" as const,
      installmentGuid: INSTALLMENT,
      paymentGuid: PAYMENT,
      method: "cash" as const,
      amountCents: 500,
    }),
    command: Object.freeze({
      deviceCode,
      cashierId: "cashier-1",
      cashierName: "Alice",
      kind: "repayment" as const,
      installmentGuid: INSTALLMENT,
    }),
    deviceCode,
    intentFingerprint: '{"kind":"repayment"}',
    state: "Created" as const,
    storeCode: STORE,
  });
}

function paymentSelectionCandidate(
  candidate: PersistedInstallmentAction,
  input: Readonly<{
    method: "cash" | "card" | "voucher";
    selection: Readonly<Record<string, unknown>>;
  }>,
): PersistedInstallmentAction {
  if (
    candidate.action.kind === "cancel-refund" ||
    candidate.command.kind === "cancel-refund"
  ) {
    throw new Error("Test payment candidate must be payable.");
  }
  return Object.freeze({
    ...candidate,
    action: Object.freeze({
      ...candidate.action,
      method: input.method,
    }),
    command: Object.freeze({
      ...candidate.command,
      ...input.selection,
    }),
  }) as unknown as PersistedInstallmentAction;
}

function cancelCandidateWithPaymentSelection(): PersistedInstallmentAction {
  return Object.freeze({
    action: Object.freeze({
      actionId: ACTION_C,
      idempotencyKey: ACTION_C,
      kind: "cancel-refund",
      installmentGuid: INSTALLMENT,
      paymentGuid: null,
      method: null,
      amountCents: null,
    }),
    command: Object.freeze({
      deviceCode: DEVICE,
      cashierId: "cashier-1",
      cashierName: "Alice",
      kind: "cancel-refund",
      installmentGuid: INSTALLMENT,
      cancelledAtIso: NOW,
      reason: null,
      idempotencyKey: ACTION_C,
      cardProvider: null,
      cashTenderedCents: 1,
    }),
    deviceCode: DEVICE,
    intentFingerprint: '{"kind":"cancel-refund"}',
    state: "Created",
    storeCode: STORE,
  }) as unknown as PersistedInstallmentAction;
}

class FixtureDecryptor implements SensitivePayloadEncryptor {
  public constructor(
    private readonly ciphertext: Uint8Array,
    private readonly plaintext: string,
  ) {}

  public encrypt(): Promise<Uint8Array> {
    throw new Error("Fixture encryptor is read-only.");
  }

  public async decrypt(ciphertext: Uint8Array): Promise<string> {
    assert.deepEqual(ciphertext, this.ciphertext);
    return this.plaintext;
  }
}

class RecordingEncryptor implements SensitivePayloadEncryptor {
  public readonly encryptedPlaintexts: string[] = [];
  public failEncryption = false;
  private readonly plaintextById = new Map<number, string>();
  private sequence = 0;

  public async encrypt(plaintext: string): Promise<Uint8Array> {
    if (this.failEncryption) {
      throw new Error("TEST_ENCRYPTION_FAILURE");
    }
    this.encryptedPlaintexts.push(plaintext);
    this.sequence += 1;
    this.plaintextById.set(this.sequence, plaintext);
    return new Uint8Array([this.sequence]);
  }

  public async decrypt(ciphertext: Uint8Array): Promise<string> {
    const id = ciphertext.length === 1 ? ciphertext[0] : undefined;
    const plaintext =
      id === undefined ? undefined : this.plaintextById.get(id);
    if (plaintext === undefined) {
      throw new Error("TEST_INVALID_CIPHERTEXT");
    }
    return plaintext;
  }
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

async function tableExists(
  connection: SqliteConnectionPort,
  tableName: string,
): Promise<boolean> {
  return (
    Number(
      (
        await connection.getFirst<{ count: unknown }>(
          `SELECT COUNT(*) AS count
           FROM sqlite_master
           WHERE type = 'table' AND name = ?`,
          [tableName],
        )
      )?.count,
    ) === 1
  );
}

async function triggerExists(
  connection: SqliteConnectionPort,
  triggerName: string,
): Promise<boolean> {
  return (
    Number(
      (
        await connection.getFirst<{ count: unknown }>(
          `SELECT COUNT(*) AS count
           FROM sqlite_master
           WHERE type = 'trigger' AND name = ?`,
          [triggerName],
        )
      )?.count,
    ) === 1
  );
}

async function scalar(
  connection: SqliteConnectionPort,
  sql: string,
): Promise<number> {
  return Number(
    (await connection.getFirst<{ count: unknown }>(sql))?.count,
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
      .run(...parameters.map(toSqliteValue));
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    return (
      (this.database
        .prepare(sql)
        .get(...parameters.map(toSqliteValue)) as T | undefined) ?? null
    );
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqliteValue)) as T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE;");
    try {
      const result = await operation(
        new TransactionConnection(this.database),
      );
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
    throw new Error("Nested transaction is not supported.");
  }

  public override async close(): Promise<void> {
    throw new Error("Transaction cannot close the database.");
  }
}

function toSqliteValue(value: SqlValue): SQLInputValue {
  return value;
}

async function withMigratedDatabase(
  operation: (connection: SqliteConnectionPort) => Promise<void>,
): Promise<void> {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => NOW);
    await operation(connection);
  });
}

async function withDatabase(
  operation: (connection: SqliteConnectionPort) => Promise<void>,
): Promise<void> {
  const connection = new SystemSqliteConnection(
    new DatabaseSync(":memory:"),
  );
  try {
    await operation(connection);
  } finally {
    await connection.close();
  }
}
