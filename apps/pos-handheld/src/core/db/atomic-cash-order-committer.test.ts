import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { SqliteApprovedPaymentOrderCommitter, SqliteAtomicCashOrderCommitter } from "./pos-database";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

import type { CompleteCashOrderCommand } from "@/core/contracts";

class TransactionRecordingConnection implements SqliteConnectionPort {
  public readonly runs: Readonly<{
    sql: string;
    parameters: readonly SqlValue[];
  }>[] = [];
  public transactionCount = 0;
  public failSql: string | null = null;

  public async exec(): Promise<void> {}

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    if (this.failSql && sql.includes(this.failSql)) {
      throw new Error("simulated transaction write failure");
    }
    this.runs.push({ sql, parameters });
    return { changes: 1, lastInsertRowId: 1 };
  }

  public async getFirst<T extends object>(): Promise<T | null> {
    return null;
  }

  public async getAll<T extends object>(): Promise<readonly T[]> {
    return [];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.transactionCount += 1;
    const start = this.runs.length;
    try {
      return await operation(this);
    } catch (error) {
      // 测试替身模拟 SQLite 回滚，证明后续写失败不会留下前半笔账本。
      this.runs.splice(start);
      throw error;
    }
  }

  public async close(): Promise<void> {}
}

class ApprovedCasFailingConnection extends TransactionRecordingConnection {
  public casFailures = 0;

  public override async getFirst<T extends object>(sql = ""): Promise<T | null> {
    if (sql.includes("FROM payment_attempts")) {
      return {
        attempt_id: "attempt-cas",
        order_guid: "order-cas",
        provider: "square",
        operation: "purchase",
        amount_cents: 500,
        state: "Approved",
      } as T;
    }
    if (sql.includes("FROM local_orders")) {
      return {
        order_guid: "order-cas",
        state: "Draft",
        actual_amount_cents: 500,
      } as T;
    }
    if (sql.includes("payment_attempt_id = ?")) return null;
    if (sql.includes("SUM(amount_cents)")) return { tender_total: 0 } as T;
    return null;
  }

  public override async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    if (sql.includes("UPDATE local_orders SET state = 'PendingSync'")) {
      this.casFailures += 1;
      return { changes: 0, lastInsertRowId: 0 };
    }
    return super.run(sql, parameters);
  }
}

class SystemSqliteConnection implements SqliteConnectionPort {
  private tail: Promise<void> = Promise.resolve();

  public constructor(private readonly databasePath: string) {}

  public async exec(sql: string): Promise<void> { this.execute(sql); }
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    const output = this.execute(`${bind(sql, parameters)}; SELECT changes() AS changes;`).trim().split("\n");
    return { changes: Number(output.at(-1)), lastInsertRowId: 0 };
  }
  public async getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    const result = spawnSqlite(this.databasePath, ["-json"], withForeignKeys(bind(sql, parameters)));
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
    const result = spawnSqlite(this.databasePath, [], withForeignKeys(sql));
    if (result.status !== 0) throw new Error(result.stderr);
    return result.stdout;
  }
}

class TransactionalNodeSqliteConnection implements SqliteConnectionPort {
  public failNextOutboxInsert = false;
  private tail: Promise<void> = Promise.resolve();
  private transactionActive = false;

  public constructor(private readonly database: DatabaseSync) {}

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    if (
      this.failNextOutboxInsert &&
      sql.includes("INSERT INTO outbox_messages")
    ) {
      this.failNextOutboxInsert = false;
      throw new Error("simulated return outbox failure");
    }
    const result = this.database
      .prepare(sql)
      .run(...parameters as readonly SQLInputValue[]);
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    const row = this.database
      .prepare(sql)
      .get(...parameters as readonly SQLInputValue[]);
    return row === undefined ? null : row as T;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    if (this.transactionActive) {
      return Promise.reject(new Error("Nested test transaction."));
    }
    const previous = this.tail;
    let release!: () => void;
    this.tail = new Promise<void>((resolve) => { release = resolve; });
    await previous;
    this.transactionActive = true;
    this.database.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.database.exec("COMMIT");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK");
      throw error;
    } finally {
      this.transactionActive = false;
      release();
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

test("订单、审计、outbox、加密小票与钱箱在同一个独占事务提交", async () => {
  const connection = new TransactionRecordingConnection();
  const encryptedPayloads: string[] = [];
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    {
      async encrypt(plaintext) {
        encryptedPayloads.push(plaintext);
        return new TextEncoder().encode(`cipher:${plaintext}`);
      },
      async decrypt() {
        throw new Error("not used");
      },
    },
    () => "2026-07-28T00:00:00.000Z",
  );

  await committer.completeCashOrderWithFulfilment(command(), {
    print: {
      jobId: "print-1",
      orderGuid: "order-1",
      printerId: "xprinter-1",
      receiptBytes: Uint8Array.from([0x1b, 0x40, 0x0a]),
      isReprint: false,
    },
    drawer: {
      eventId: "drawer-1",
      orderGuid: "order-1",
      printerId: "xprinter-1",
      printJobId: "print-1",
      reason: "cash-sale",
    },
  });

  assert.equal(connection.transactionCount, 1);
  for (const table of [
    "local_orders",
    "local_order_lines",
    "order_tenders",
    "audit_events",
    "outbox_messages",
    "print_jobs",
    "drawer_events",
  ]) {
    assert.ok(
      connection.runs.some((entry) => entry.sql.includes(`INSERT INTO ${table}`)),
      `missing ${table}`,
    );
  }
  assert.deepEqual(encryptedPayloads, ["[27,64,10]"]);
  const printInsert = connection.runs.find((entry) =>
    entry.sql.includes("INSERT INTO print_jobs"));
  assert.ok(printInsert?.parameters.some((value) => value instanceof Uint8Array));
  assert.equal(
    printInsert?.parameters.some((value) => value === "[27,64,10]"),
    false,
  );
  const drawerInsert = connection.runs.find((entry) =>
    entry.sql.includes("INSERT INTO drawer_events"));
  assert.match(drawerInsert?.sql ?? "", /printer_id/);
  assert.equal(drawerInsert?.parameters[2], "xprinter-1");
  const lineInsert = connection.runs.find((entry) =>
    entry.sql.includes("INSERT INTO local_order_lines"));
  assert.match(lineInsert?.sql ?? "", /reference_code/);
  assert.match(lineInsert?.sql ?? "", /sync_price_source/);
  assert.ok(lineInsert?.parameters.includes("REF-P1"));
  assert.ok(lineInsert?.parameters.includes(3));
});

test("现金订单缺少冻结同步来源时在开事务前拒绝", async () => {
  const connection = new TransactionRecordingConnection();
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    {
      async encrypt(value) {
        return new TextEncoder().encode(value);
      },
      async decrypt(value) {
        return new TextDecoder().decode(value);
      },
    },
    () => "2026-07-28T00:00:00.000Z",
  );
  const input = command();
  const line = input.order.lines[0];
  assert.ok(line);
  const { syncProvenance: _syncProvenance, ...legacyLine } = line;

  await assert.rejects(
    () =>
      committer.completeCashOrderWithFulfilment(
        {
          ...input,
          requiresDrawer: false,
          printPolicy: "never",
          order: {
            ...input.order,
            lines: [legacyLine],
          },
        },
        {
          print: null,
          drawer: null,
        },
      ),
    /line sync provenance/i,
  );
  assert.equal(connection.transactionCount, 0);
  assert.equal(connection.runs.length, 0);
});

test("履约写失败整体回滚，不留下已完成订单或可补传 outbox", async () => {
  const connection = new TransactionRecordingConnection();
  connection.failSql = "INSERT INTO print_jobs";
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    {
      async encrypt(plaintext) {
        return new TextEncoder().encode(plaintext);
      },
      async decrypt() {
        throw new Error("not used");
      },
    },
    () => "2026-07-28T00:00:00.000Z",
  );

  await assert.rejects(
    () => committer.completeCashOrderWithFulfilment(command(), {
      print: {
        jobId: "print-1",
        orderGuid: "order-1",
        printerId: "xprinter-1",
        receiptBytes: Uint8Array.from([1]),
        isReprint: false,
      },
      drawer: {
        eventId: "drawer-1",
        orderGuid: "order-1",
        printerId: "xprinter-1",
        printJobId: "print-1",
        reason: "cash-sale",
      },
    }),
    /simulated transaction write failure/,
  );

  assert.equal(connection.transactionCount, 1);
  assert.equal(connection.runs.length, 0);
});

test("履约关系或策略不一致时在加密和开事务之前拒绝", async () => {
  const connection = new TransactionRecordingConnection();
  let encryptCalls = 0;
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    {
      async encrypt() {
        encryptCalls += 1;
        return Uint8Array.from([1]);
      },
      async decrypt() {
        throw new Error("not used");
      },
    },
    () => "2026-07-28T00:00:00.000Z",
  );

  await assert.rejects(
    () => committer.completeCashOrderWithFulfilment(command(), {
      print: null,
      drawer: {
        eventId: "drawer-1",
        orderGuid: "another-order",
        printerId: "xprinter-1",
        printJobId: null,
        reason: "cash-sale",
      },
    }),
    /automatic receipt policy/i,
  );
  assert.equal(encryptCalls, 0);
  assert.equal(connection.transactionCount, 0);
});

test("现金履约在加密和开事务前拒绝空 printerId 或与打印任务不一致的钱箱绑定", async () => {
  const connection = new TransactionRecordingConnection();
  let encryptCalls = 0;
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    {
      async encrypt() {
        encryptCalls += 1;
        return Uint8Array.of(1);
      },
      async decrypt() {
        throw new Error("not used");
      },
    },
    () => "2026-07-28T00:00:00.000Z",
  );
  const print = {
    jobId: "print-1",
    orderGuid: "order-1",
    printerId: "xprinter-1",
    receiptBytes: Uint8Array.of(1),
    isReprint: false as const,
  };

  await assert.rejects(
    () => committer.completeCashOrderWithFulfilment(command(), {
      print,
      drawer: {
        eventId: "drawer-empty",
        orderGuid: "order-1",
        printerId: "",
        printJobId: "print-1",
        reason: "cash-sale",
      },
    }),
    /drawer event is invalid/i,
  );
  await assert.rejects(
    () => committer.completeCashOrderWithFulfilment(command(), {
      print,
      drawer: {
        eventId: "drawer-wrong",
        orderGuid: "order-1",
        printerId: "xprinter-2",
        printJobId: "print-1",
        reason: "cash-sale",
      },
    }),
    /printer does not match/i,
  );

  assert.equal(encryptCalls, 0);
  assert.equal(connection.transactionCount, 0);
});

test("Approved 最终支付的订单状态 CAS 失败时整事务中止，不能先写 tender 或伪装完成", async () => {
  const connection = new ApprovedCasFailingConnection();
  const committer = new SqliteApprovedPaymentOrderCommitter(
    connection,
    {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
    () => "2026-07-28T00:00:00.000Z",
  );

  await assert.rejects(
    () => committer.completeApprovedPaymentOrder(
      approvedPaymentInput("attempt-cas", "order-cas", "tender-cas", null),
    ),
    /order state changed/i,
  );

  assert.equal(connection.casFailures, 1);
  assert.equal(connection.transactionCount, 1);
  assert.equal(
    connection.runs.some((entry) => entry.sql.includes("INSERT INTO order_tenders")),
    false,
  );
});

test("真实 SQLite：同一现金 intent 崩溃重放只返回原订单，换内容拒绝，并发不会重复写履约", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-cash-intent-"));
  const databasePath = join(folder, "cash.db");
  const encryptor = {
    async encrypt(value: string) { return new TextEncoder().encode(value); },
    async decrypt(value: Uint8Array) { return new TextDecoder().decode(value); },
  };
  try {
    const connection = new SystemSqliteConnection(databasePath);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    const input = durableInput();
    const first = await new SqliteAtomicCashOrderCommitter(connection, encryptor, () => "2026-07-28T00:00:00.000Z")
      .completeDurableCashOrder(input);
    assert.deepEqual(first, { replayed: false, orderGuid: "order-1", cashDueCents: 500, changeCents: 0 });

    // 新连接模拟订单已落库、App 在显示成功页前被杀。
    const reopened = new SystemSqliteConnection(databasePath);
    const committer = new SqliteAtomicCashOrderCommitter(reopened, encryptor, () => "2026-07-28T00:00:01.000Z");
    const replay = await committer.completeDurableCashOrder(input);
    assert.deepEqual(replay, { ...first, replayed: true });
    await assert.rejects(
      () => committer.completeDurableCashOrder({ ...input, intent: { ...input.intent, requestSignature: "different-content" } }),
      /replayed with different content/,
    );

    const concurrent = await Promise.all([
      committer.completeDurableCashOrder(input),
      committer.completeDurableCashOrder(input),
    ]);
    assert.ok(concurrent.every((result) => result.replayed));
    const counts = await reopened.getFirst<{ orders: number; outbox: number; printJobs: number; drawers: number; printPrinter: string; drawerPrinter: string }>(
      "SELECT (SELECT COUNT(*) FROM local_orders) AS orders, (SELECT COUNT(*) FROM outbox_messages) AS outbox, (SELECT COUNT(*) FROM print_jobs) AS printJobs, (SELECT COUNT(*) FROM drawer_events) AS drawers, (SELECT printer_id FROM print_jobs WHERE job_id = 'print-1') AS printPrinter, (SELECT printer_id FROM drawer_events WHERE event_id = 'drawer-1') AS drawerPrinter",
    );
    assert.deepEqual(counts, { orders: 1, outbox: 1, printJobs: 1, drawers: 1, printPrinter: "xprinter-1", drawerPrinter: "xprinter-1" });
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});

test("真实 SQLite：普通终端全退现金退款原子保存负 tender、审计、outbox、履约并扣减容量", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-cash-return-"));
  const databasePath = join(folder, "return.db");
  const encryptor = {
    async encrypt(value: string) { return new TextEncoder().encode(value); },
    async decrypt(value: Uint8Array) { return new TextDecoder().decode(value); },
  };
  try {
    const connection = new SystemSqliteConnection(databasePath);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await connection.run(
      `INSERT INTO return_capacity (
        return_source_key, original_order_guid, original_order_detail_guid,
        original_quantity, remaining_quantity, updated_at_iso
      ) VALUES (?, ?, ?, ?, ?, ?)`,
      [
        "return-source-1",
        "original-order-1",
        "original-line-1",
        "1",
        "1",
        "2026-07-28T00:00:00.000Z",
      ],
    );

    const result = await new SqliteAtomicCashOrderCommitter(
      connection,
      encryptor,
      () => "2026-07-28T00:05:00.000Z",
    ).completeDurableCashOrder(durableReturnInput());

    assert.deepEqual(result, {
      replayed: false,
      orderGuid: "return-order-1",
      cashDueCents: -500,
      changeCents: 0,
    });
    const replay = await new SqliteAtomicCashOrderCommitter(
      connection,
      encryptor,
      () => "2026-07-28T00:06:00.000Z",
    ).completeDurableCashOrder(durableReturnInput());
    assert.deepEqual(replay, { ...result, replayed: true });
    const persisted = await connection.getFirst<{
      orders: number;
      tenders: number;
      audits: number;
      outboxMessages: number;
      drawers: number;
      orderState: string;
      orderActual: number;
      orderOriginal: string;
      lineKind: string;
      lineActual: number;
      returnSource: string;
      lineOriginal: string;
      tenderMethod: string;
      tenderAmount: number;
      capacityRemaining: string;
      auditType: string;
      auditOrder: string;
      outboxState: string;
      outboxOrder: string;
      intentOrder: string;
      printJobs: number;
      drawerState: string;
      drawerReason: string;
      drawerOrder: string;
    }>(
      `SELECT
        (SELECT COUNT(*) FROM local_orders WHERE order_guid = 'return-order-1') AS orders,
        (SELECT COUNT(*) FROM order_tenders WHERE order_guid = 'return-order-1') AS tenders,
        (SELECT COUNT(*) FROM audit_events WHERE order_guid = 'return-order-1') AS audits,
        (SELECT COUNT(*) FROM outbox_messages WHERE aggregate_id = 'return-order-1') AS outboxMessages,
        (SELECT COUNT(*) FROM drawer_events WHERE order_guid = 'return-order-1') AS drawers,
        (SELECT state FROM local_orders WHERE order_guid = 'return-order-1') AS orderState,
        (SELECT actual_amount_cents FROM local_orders WHERE order_guid = 'return-order-1') AS orderActual,
        (SELECT original_order_guid FROM local_orders WHERE order_guid = 'return-order-1') AS orderOriginal,
        (SELECT line_kind FROM local_order_lines WHERE order_guid = 'return-order-1') AS lineKind,
        (SELECT actual_amount_cents FROM local_order_lines WHERE order_guid = 'return-order-1') AS lineActual,
        (SELECT return_source_key FROM local_order_lines WHERE order_guid = 'return-order-1') AS returnSource,
        (SELECT original_order_guid FROM local_order_lines WHERE order_guid = 'return-order-1') AS lineOriginal,
        (SELECT method FROM order_tenders WHERE order_guid = 'return-order-1') AS tenderMethod,
        (SELECT amount_cents FROM order_tenders WHERE order_guid = 'return-order-1') AS tenderAmount,
        (SELECT remaining_quantity FROM return_capacity WHERE return_source_key = 'return-source-1') AS capacityRemaining,
        (SELECT event_type FROM audit_events WHERE event_id = 'return-audit-1') AS auditType,
        (SELECT order_guid FROM audit_events WHERE event_id = 'return-audit-1') AS auditOrder,
        (SELECT state FROM outbox_messages WHERE message_id = 'return-outbox-1') AS outboxState,
        (SELECT aggregate_id FROM outbox_messages WHERE message_id = 'return-outbox-1') AS outboxOrder,
        (SELECT order_guid FROM cash_checkout_intents WHERE checkout_intent_id = 'return-intent-1') AS intentOrder,
        (SELECT COUNT(*) FROM print_jobs WHERE order_guid = 'return-order-1') AS printJobs,
        (SELECT state FROM drawer_events WHERE event_id = 'return-drawer-1') AS drawerState,
        (SELECT reason FROM drawer_events WHERE event_id = 'return-drawer-1') AS drawerReason,
        (SELECT order_guid FROM drawer_events WHERE event_id = 'return-drawer-1') AS drawerOrder`,
    );
    assert.deepEqual(persisted, {
      orders: 1,
      tenders: 1,
      audits: 1,
      outboxMessages: 1,
      drawers: 1,
      orderState: "PendingSync",
      orderActual: -500,
      orderOriginal: "original-order-1",
      lineKind: "return",
      lineActual: -500,
      returnSource: "return-source-1",
      lineOriginal: "original-order-1",
      tenderMethod: "cash",
      tenderAmount: -500,
      capacityRemaining: "0",
      auditType: "RETURN_REFUND_COMPLETE",
      auditOrder: "return-order-1",
      outboxState: "pending",
      outboxOrder: "return-order-1",
      intentOrder: "return-order-1",
      printJobs: 0,
      drawerState: "Required",
      drawerReason: "cash-refund",
      drawerOrder: "return-order-1",
    });
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});

test("普通 durable 现金提交拒绝 sale/return 混合及元数据或金额非法的全退单", async () => {
  const connection = new TransactionRecordingConnection();
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
    () => "2026-07-28T00:05:00.000Z",
  );
  const base = durableReturnInput();
  const saleLine = {
    ...command().order.lines[0]!,
    lineId: "mixed-sale-line",
  };
  const invalidInputs = [
    {
      name: "mixed",
      pattern: /mix sale and return/i,
      input: {
        ...base,
        command: {
          ...base.command,
          order: {
            ...base.command.order,
            lines: [...base.command.order.lines, saleLine],
          },
        },
      },
    },
    {
      name: "missing-metadata",
      pattern: /return metadata/i,
      input: {
        ...base,
        command: {
          ...base.command,
          order: {
            ...base.command.order,
            lines: [{
              ...base.command.order.lines[0]!,
              returnSourceKey: null,
            }],
          },
        },
      },
    },
    {
      name: "positive-refund",
      pattern: /signed amounts/i,
      input: {
        ...base,
        intent: {
          ...base.intent,
          cashDueCents: 500,
        },
        command: {
          ...base.command,
          order: {
            ...base.command.order,
            total: { currency: "AUD", cents: 500 },
            actualAmount: { currency: "AUD", cents: 500 },
            lines: [{
              ...base.command.order.lines[0]!,
              actualAmount: { currency: "AUD", cents: 500 },
            }],
            tenders: [{
              ...base.command.order.tenders[0]!,
              amount: { currency: "AUD", cents: 500 },
            }],
          },
        },
      },
    },
    {
      name: "line-order-amount-mismatch",
      pattern: /line actual amounts mismatch/i,
      input: {
        ...base,
        command: {
          ...base.command,
          order: {
            ...base.command.order,
            lines: [{
              ...base.command.order.lines[0]!,
              actualAmount: { currency: "AUD", cents: -1 },
            }],
          },
        },
      },
    },
    {
      name: "refund-settlement-mismatch",
      pattern: /refund cash settlement mismatch/i,
      input: {
        ...base,
        intent: {
          ...base.intent,
          cashDueCents: -495,
        },
        command: {
          ...base.command,
          auditEvents: [{
            ...base.command.auditEvents[0]!,
            payload: {
              ...base.command.auditEvents[0]!.payload,
              cashDueCents: -495,
            },
          }],
        },
      },
    },
  ] as const;

  for (const scenario of invalidInputs) {
    await assert.rejects(
      () => committer.completeDurableCashOrder(scenario.input),
      scenario.pattern,
      scenario.name,
    );
  }
  assert.equal(connection.runs.length, 0);
});

test("真实 SQLite 事务：错误原订单明细不扣容量且不写退款账本", async () => {
  const connection = await openTransactionalReturnDatabase();
  try {
    const base = durableReturnInput();
    await assert.rejects(
      () => new SqliteAtomicCashOrderCommitter(
        connection,
        testEncryptor,
        () => "2026-07-28T00:05:00.000Z",
      ).completeDurableCashOrder({
        ...base,
        command: {
          ...base.command,
          order: {
            ...base.command.order,
            lines: [{
              ...base.command.order.lines[0]!,
              originalOrderDetailGuid: "wrong-original-line",
            }],
          },
        },
      }),
      /capacity is unknown or exhausted/i,
    );
    assert.deepEqual(await readReturnRollbackState(connection), {
      capacityRemaining: "1",
      orders: 0,
      tenders: 0,
      audits: 0,
      outboxMessages: 0,
      intents: 0,
      drawers: 0,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite 事务：容量扣减后的 outbox 失败回滚退款订单、intent 与履约", async () => {
  const connection = await openTransactionalReturnDatabase();
  try {
    connection.failNextOutboxInsert = true;
    await assert.rejects(
      () => new SqliteAtomicCashOrderCommitter(
        connection,
        testEncryptor,
        () => "2026-07-28T00:05:00.000Z",
      ).completeDurableCashOrder(durableReturnInput()),
      /simulated return outbox failure/,
    );
    assert.deepEqual(await readReturnRollbackState(connection), {
      capacityRemaining: "1",
      orders: 0,
      tenders: 0,
      audits: 0,
      outboxMessages: 0,
      intents: 0,
      drawers: 0,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：Approved 支付重开后只绑定原订单，部分 tender 不完成，所有已落库后续状态都保持幂等", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-approved-payment-"));
  const databasePath = join(folder, "approved.db");
  const encryptor = { async encrypt(value: string) { return new TextEncoder().encode(value); }, async decrypt(value: Uint8Array) { return new TextDecoder().decode(value); } };
  try {
    const connection = new SystemSqliteConnection(databasePath);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await insertDraftOrder(connection, "order-card", 1, 1_000);
    await insertApprovedAttempt(connection, "attempt-card-1", "order-card", 400);
    await insertApprovedAttempt(connection, "attempt-card-2", "order-card", 600);
    const committer = new SqliteApprovedPaymentOrderCommitter(connection, encryptor, () => "2026-07-28T00:00:00.000Z");

    const partial = await committer.completeApprovedPaymentOrder(approvedPaymentInput("attempt-card-1", "order-card", "tender-card-1", null));
    assert.deepEqual(partial, { replayed: false, orderGuid: "order-card", tenderGuid: "tender-card-1", completed: false, signedTenderAmountCents: 400 });
    const partialReplay = await committer.completeApprovedPaymentOrder(approvedPaymentInput("attempt-card-1", "order-card", "another-partial-tender", null));
    assert.deepEqual(partialReplay, { ...partial, replayed: true });
    const final = await committer.completeApprovedPaymentOrder(approvedPaymentInput("attempt-card-2", "order-card", "tender-card-2", { print: { jobId: "payment-print", orderGuid: "order-card", printerId: "XP-1", receiptBytes: Uint8Array.of(1), isReprint: false }, drawer: { eventId: "payment-drawer", orderGuid: "order-card", printerId: "XP-1", printJobId: "payment-print", reason: "card-sale" } }));
    assert.equal(final.completed, true);

    const reopened = new SystemSqliteConnection(databasePath);
    const reopenedCommitter = new SqliteApprovedPaymentOrderCommitter(reopened, encryptor, () => "2026-07-28T00:01:00.000Z");
    for (const state of ["PendingSync", "CompletedLocal", "Syncing", "Synced", "Blocked403", "Rejected"] as const) {
      await reopened.run("UPDATE local_orders SET state = ? WHERE order_guid = ?", [state, "order-card"]);
      const replay = await reopenedCommitter.completeApprovedPaymentOrder(
        approvedPaymentInput("attempt-card-2", "order-card", `replay-${state}`, null),
      );
      assert.deepEqual(replay, { ...final, replayed: true }, state);
    }
    const counts = await reopened.getFirst<{ state: string; tenders: number; outbox: number; prints: number; drawers: number; printPrinter: string; drawerPrinter: string }>(
      "SELECT (SELECT state FROM local_orders WHERE order_guid = 'order-card') AS state, (SELECT COUNT(*) FROM order_tenders WHERE order_guid = 'order-card') AS tenders, (SELECT COUNT(*) FROM outbox_messages WHERE aggregate_id = 'order-card') AS outbox, (SELECT COUNT(*) FROM print_jobs WHERE order_guid = 'order-card') AS prints, (SELECT COUNT(*) FROM drawer_events WHERE order_guid = 'order-card') AS drawers, (SELECT printer_id FROM print_jobs WHERE order_guid = 'order-card') AS printPrinter, (SELECT printer_id FROM drawer_events WHERE order_guid = 'order-card') AS drawerPrinter",
    );
    assert.deepEqual(counts, { state: "Rejected", tenders: 2, outbox: 1, prints: 1, drawers: 1, printPrinter: "XP-1", drawerPrinter: "XP-1" });

    await insertDraftOrder(reopened, "other-order", 2, 500);
    await insertApprovedAttempt(reopened, "attempt-other", "other-order", 500);
    await assert.rejects(
      () => new SqliteApprovedPaymentOrderCommitter(reopened, encryptor, () => "2026-07-28T00:02:00.000Z").completeApprovedPaymentOrder(approvedPaymentInput("attempt-other", "order-card", "wrong-bind", null)),
      /different order/,
    );

    await insertDraftOrder(reopened, "synced-order", 3, 500);
    await reopened.run("UPDATE local_orders SET state = 'Synced' WHERE order_guid = 'synced-order'");
    await insertApprovedAttempt(reopened, "attempt-synced", "synced-order", 500);
    await assert.rejects(
      () => reopenedCommitter.completeApprovedPaymentOrder(
        approvedPaymentInput("attempt-synced", "synced-order", "tender-synced", null),
      ),
      /cannot accept a new approved tender/i,
    );
    const terminalCounts = await reopened.getFirst<{ tenders: number; outbox: number }>(
      "SELECT (SELECT COUNT(*) FROM order_tenders WHERE order_guid = 'synced-order') AS tenders, (SELECT COUNT(*) FROM outbox_messages WHERE aggregate_id = 'synced-order') AS outbox",
    );
    assert.deepEqual(terminalCounts, { tenders: 0, outbox: 0 });
  } finally { rmSync(folder, { recursive: true, force: true }); }
});

function command(): CompleteCashOrderCommand {
  return {
    order: {
      orderGuid: "order-1",
      localSequence: 1,
      storeCode: "S1",
      deviceCode: "IPAD1",
      cashierId: "cashier-1",
      cashierName: "Cashier",
      soldAtIso: "2026-07-28T00:00:00.000Z",
      state: "PendingSync",
      total: { currency: "AUD", cents: 500 },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: 500 },
      lines: [{
        lineId: "line-1",
        productCode: "P1",
        itemNumber: null,
        lookupCode: "123",
        displayName: "Item",
        quantity: "1",
        unitPrice: { currency: "AUD", cents: 500 },
        discount: { currency: "AUD", cents: 0 },
        actualAmount: { currency: "AUD", cents: 500 },
        priceSource: "catalog",
        syncProvenance: {
          referenceCode: "REF-P1",
          priceSource: 3,
        },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      }],
      tenders: [{
        tenderGuid: "tender-1",
        method: "cash",
        amount: { currency: "AUD", cents: 500 },
        reference: null,
        reservationToken: null,
      }],
      originalOrderGuid: null,
    },
    auditEvents: [{
      eventId: "audit-1",
      eventType: "SALE_COMPLETE",
      occurredAtIso: "2026-07-28T00:00:00.000Z",
      orderGuid: "order-1",
      correlationId: "order-1",
      payload: { amountCents: 500 },
    }],
    outbox: {
      messageId: "outbox-1",
      aggregateId: "order-1",
      kind: "order-sync",
      payloadJson: "{\"orderGuid\":\"order-1\"}",
      nextAttemptAtIso: "2026-07-28T00:00:00.000Z",
    },
    requiresDrawer: true,
    printPolicy: "automatic",
  };
}

function durableInput() {
  return {
    intent: { checkoutIntentId: "intent-1", requestSignature: "cart-v1-cash-500", cashDueCents: 500, changeCents: 0 },
    command: command(),
    fulfilment: {
      print: { jobId: "print-1", orderGuid: "order-1", printerId: "xprinter-1", receiptBytes: Uint8Array.from([0x1b, 0x40]), isReprint: false },
      drawer: { eventId: "drawer-1", orderGuid: "order-1", printerId: "xprinter-1", printJobId: "print-1", reason: "cash-sale" },
    },
    terminalContext: { kind: "none" },
    recalledHoldCompletion: null,
  } as const;
}

function durableReturnInput() {
  return {
    intent: {
      checkoutIntentId: "return-intent-1",
      requestSignature: "cash-v2-return-500",
      cashDueCents: -500,
      changeCents: 0,
    },
    command: {
      order: {
        orderGuid: "return-order-1",
        localSequence: 2,
        storeCode: "S1",
        deviceCode: "IPAD1",
        cashierId: "cashier-1",
        cashierName: "Cashier",
        soldAtIso: "2026-07-28T00:05:00.000Z",
        state: "PendingSync",
        total: { currency: "AUD", cents: -500 },
        discount: { currency: "AUD", cents: 0 },
        actualAmount: { currency: "AUD", cents: -500 },
        lines: [{
          lineId: "return-line-1",
          productCode: "P1",
          itemNumber: null,
          lookupCode: "123",
          displayName: "Returned Item",
          quantity: "1",
          unitPrice: { currency: "AUD", cents: 500 },
          discount: { currency: "AUD", cents: 0 },
          actualAmount: { currency: "AUD", cents: -500 },
          priceSource: "catalog",
          syncProvenance: {
            referenceCode: "REF-RETURN-P1",
            priceSource: 0,
          },
          kind: "return",
          returnSourceKey: "return-source-1",
          originalOrderGuid: "original-order-1",
          originalOrderDetailGuid: "original-line-1",
        }],
        tenders: [{
          tenderGuid: "return-tender-1",
          method: "cash",
          amount: { currency: "AUD", cents: -500 },
          reference: null,
          reservationToken: null,
        }],
        originalOrderGuid: "original-order-1",
      },
      auditEvents: [{
        eventId: "return-audit-1",
        eventType: "RETURN_REFUND_COMPLETE",
        occurredAtIso: "2026-07-28T00:05:00.000Z",
        orderGuid: "return-order-1",
        correlationId: "return-order-1",
        payload: {
          checkoutIntentId: "return-intent-1",
          localSequence: 2,
          cashDueCents: -500,
          changeCents: 0,
        },
      }],
      outbox: {
        messageId: "return-outbox-1",
        aggregateId: "return-order-1",
        kind: "order-sync",
        payloadJson: "{\"orderGuid\":\"return-order-1\"}",
        nextAttemptAtIso: "2026-07-28T00:05:00.000Z",
      },
      requiresDrawer: true,
      printPolicy: "never",
    },
    fulfilment: {
      print: null,
      drawer: {
        eventId: "return-drawer-1",
        orderGuid: "return-order-1",
        printerId: "xprinter-1",
        printJobId: null,
        reason: "cash-refund",
      },
    },
    terminalContext: { kind: "none" },
    recalledHoldCompletion: null,
  } as const;
}

const testEncryptor = {
  async encrypt(value: string) {
    return new TextEncoder().encode(value);
  },
  async decrypt(value: Uint8Array) {
    return new TextDecoder().decode(value);
  },
};

async function openTransactionalReturnDatabase(): Promise<TransactionalNodeSqliteConnection> {
  const connection = new TransactionalNodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await connection.exec(
    POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"),
  );
  await connection.run(
    `INSERT INTO return_capacity (
      return_source_key, original_order_guid, original_order_detail_guid,
      original_quantity, remaining_quantity, updated_at_iso
    ) VALUES (?, ?, ?, ?, ?, ?)`,
    [
      "return-source-1",
      "original-order-1",
      "original-line-1",
      "1",
      "1",
      "2026-07-28T00:00:00.000Z",
    ],
  );
  return connection;
}

async function readReturnRollbackState(
  connection: SqliteConnectionPort,
): Promise<Readonly<{
  capacityRemaining: string;
  orders: number;
  tenders: number;
  audits: number;
  outboxMessages: number;
  intents: number;
  drawers: number;
}> | null> {
  const row = await connection.getFirst<{
    capacityRemaining: string;
    orders: number;
    tenders: number;
    audits: number;
    outboxMessages: number;
    intents: number;
    drawers: number;
  }>(
    `SELECT
      (SELECT remaining_quantity FROM return_capacity WHERE return_source_key = 'return-source-1') AS capacityRemaining,
      (SELECT COUNT(*) FROM local_orders) AS orders,
      (SELECT COUNT(*) FROM order_tenders) AS tenders,
      (SELECT COUNT(*) FROM audit_events) AS audits,
      (SELECT COUNT(*) FROM outbox_messages) AS outboxMessages,
      (SELECT COUNT(*) FROM cash_checkout_intents) AS intents,
      (SELECT COUNT(*) FROM drawer_events) AS drawers`,
  );
  return row === null ? null : { ...row };
}

function approvedPaymentInput(attemptId: string, orderGuid: string, tenderGuid: string, fulfilment: { print: { jobId: string; orderGuid: string; printerId: string; receiptBytes: Uint8Array; isReprint: false } | null; drawer: { eventId: string; orderGuid: string; printerId: string; printJobId: string | null; reason: string } | null } | null) {
  return { attemptId, orderGuid, tenderGuid, completionAuditEvents: [{ eventId: `audit-${attemptId}`, eventType: "PAYMENT_COMPLETE", occurredAtIso: "2026-07-28T00:00:00.000Z", orderGuid, correlationId: orderGuid, payload: { source: "approved-payment" } }], outbox: { messageId: `outbox-${attemptId}`, aggregateId: orderGuid, kind: "order-sync", payloadJson: JSON.stringify({ orderGuid }), nextAttemptAtIso: "2026-07-28T00:00:00.000Z" }, fulfilment: fulfilment ?? { print: null, drawer: null } } as const;
}

async function insertDraftOrder(connection: SqliteConnectionPort, orderGuid: string, sequence: number, amountCents: number): Promise<void> {
  await connection.run("INSERT INTO local_orders (order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name, sold_at_iso, state, total_cents, discount_cents, actual_amount_cents, original_order_guid, created_at_iso, updated_at_iso) VALUES (?, ?, ?, ?, ?, ?, ?, 'Draft', ?, 0, ?, NULL, ?, ?)", [orderGuid, sequence, "S1", "IPAD1", "cashier-1", "Cashier", "2026-07-28T00:00:00.000Z", amountCents, amountCents, "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z"]);
}

async function insertApprovedAttempt(connection: SqliteConnectionPort, attemptId: string, orderGuid: string, amountCents: number): Promise<void> {
  await connection.run("INSERT INTO payment_attempts (attempt_id, idempotency_key, order_guid, provider, operation, amount_cents, state, checkout_id, payment_id, session_id, txn_ref, rfn, provider_payload_ciphertext, provider_receipt_ciphertext, provider_response_code, created_at_iso, updated_at_iso, last_error_code) VALUES (?, ?, ?, 'square', 'purchase', ?, 'Approved', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ?, ?, NULL)", [attemptId, `key-${attemptId}`, orderGuid, amountCents, "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z"]);
}

function withForeignKeys(sql: string): string { return `PRAGMA foreign_keys = ON;\n${sql}`; }

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
