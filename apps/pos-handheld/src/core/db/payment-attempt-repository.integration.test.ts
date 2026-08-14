import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type {
  CardSyncEvidenceV1,
  PaymentAttempt,
} from "../contracts/payment";
import type { LocalOrderState } from "../contracts/state-machines";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { SqlitePaymentProtectedMaterialReader } from "./sqlite-payment-protected-material";
import { createSqliteRepositories } from "./sqlite-repositories";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

class SystemSqliteConnection implements SqliteConnectionPort {
  private readonly database: DatabaseSync;
  private readonly queue = new AsyncSerialQueue();

  public constructor(databasePath: string) {
    this.database = new DatabaseSync(databasePath);
    this.database.exec("PRAGMA foreign_keys = ON");
  }

  public exec(sql: string): Promise<void> {
    return this.queue.enqueue(async () => {
      this.database.exec(sql);
    });
  }

  public run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    return this.queue.enqueue(async () =>
      runStatement(this.database, sql, parameters));
  }

  public getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    return this.queue.enqueue(async () =>
      getFirst<T>(this.database, sql, parameters));
  }

  public getAll<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<readonly T[]> {
    return this.queue.enqueue(async () =>
      getAll<T>(this.database, sql, parameters));
  }

  public withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    return this.queue.enqueue(async () => {
      this.database.exec("BEGIN IMMEDIATE");
      const transaction = new NodeSqliteTransactionConnection(this.database);
      try {
        const result = await operation(transaction);
        this.database.exec("COMMIT");
        return result;
      } catch (error) {
        this.database.exec("ROLLBACK");
        throw error;
      }
    });
  }

  public close(): Promise<void> {
    return this.queue.enqueue(async () => {
      this.database.close();
    });
  }
}

class NodeSqliteTransactionConnection implements SqliteConnectionPort {
  public constructor(private readonly database: DatabaseSync) {}

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    return runStatement(this.database, sql, parameters);
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    return getFirst<T>(this.database, sql, parameters);
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return getAll<T>(this.database, sql, parameters);
  }

  public withExclusiveTransaction<T>(): Promise<T> {
    return Promise.reject(new Error("Nested test transaction."));
  }

  public close(): Promise<void> {
    return Promise.reject(new Error("Transaction connection cannot close the database."));
  }
}

class AsyncSerialQueue {
  private tail: Promise<void> = Promise.resolve();

  public enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.tail.then(operation, operation);
    this.tail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }
}

function payment(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-1",
    idempotencyKey: "idempotency-1",
    orderGuid: "order-1",
    provider: "square",
    operation: "purchase",
    amount: { currency: "AUD", cents: 500 },
    state: "Created",
    references: { checkoutId: null, paymentId: null, sessionId: null, txnRef: null, rfn: null, voucherReservationToken: null },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:00:00.000Z",
    lastErrorCode: null,
    ...overrides,
  };
}

test("真实 SQLite：支付尝试保留身份、Approved 仅匹配正确 tender 解锁，并且重开连接后仍然成立", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-payment-db-"));
  const databasePath = join(folder, "payment.db");
  try {
    const connection = new SystemSqliteConnection(databasePath);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await insertDraftOrder(connection, "order-1", 1, 500);
    const encryptor = {
      async encrypt(plaintext: string) { return new TextEncoder().encode(plaintext); },
      async decrypt(ciphertext: Uint8Array) { return new TextDecoder().decode(ciphertext); },
    };
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-1",
      encryptor,
    });

    const created = payment();
    const [firstResult, secondResult] = await Promise.all([
      repositories.payments.insertIfUnblocked(created),
      repositories.payments.insertIfUnblocked(payment({ attemptId: "attempt-2", idempotencyKey: "idempotency-2" })),
    ]);
    assert.equal(firstResult, null);
    assert.equal(secondResult?.attemptId, "attempt-1");

    assert.equal(
      await repositories.payments.compareAndUpdate(
        created,
        payment({ orderGuid: "another-order", state: "Submitted", updatedAtIso: "2026-07-28T00:00:30.000Z" }),
      ),
      false,
    );
    assert.equal((await repositories.payments.get("attempt-1"))?.orderGuid, "order-1");

    const approved = payment({ state: "Approved", updatedAtIso: "2026-07-28T00:01:00.000Z", receiptText: "CARD RECEIPT", responseCode: "APPROVED" });
    assert.equal(await repositories.payments.compareAndUpdate(created, approved), true);
    assert.equal((await repositories.payments.findBlocking("order-1"))?.state, "Approved");

    // 重开连接模拟批准回执已落库、应用在写 tender 前被杀；金额不同仍不能解除阻塞。
    const reopened = new SystemSqliteConnection(databasePath);
    await connection.run(
      "INSERT INTO order_tenders (tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso) VALUES (?, ?, ?, ?, ?, ?)",
      ["wrong-amount", "order-1", "card", 499, "attempt-1", "2026-07-28T00:01:00.000Z"],
    );
    const reopenedRepositories = createSqliteRepositories(reopened, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-1",
      encryptor,
    });
    assert.equal((await reopenedRepositories.payments.findBlocking("order-1"))?.attemptId, "attempt-1");
    assert.deepEqual(await reopenedRepositories.payments.get("attempt-1"), approved);

    await insertDraftOrder(reopened, "order-method", 2, 500);
    const methodAttempt = payment({ attemptId: "attempt-method", idempotencyKey: "idempotency-method", orderGuid: "order-method", provider: "voucher" });
    assert.equal(await reopenedRepositories.payments.insertIfUnblocked(methodAttempt), null);
    assert.equal(await reopenedRepositories.payments.compareAndUpdate(methodAttempt, { ...methodAttempt, state: "Approved", updatedAtIso: "2026-07-28T00:01:00.000Z" }), true);
    await reopened.run("INSERT INTO order_tenders (tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso) VALUES (?, ?, ?, ?, ?, ?)", ["wrong-method", "order-method", "card", 500, "attempt-method", "2026-07-28T00:01:00.000Z"]);
    assert.equal((await reopenedRepositories.payments.findBlocking("order-method"))?.attemptId, "attempt-method");

    await insertDraftOrder(reopened, "order-mismatch", 3, 500);
    await insertDraftOrder(reopened, "another-order", 4, 500);
    const mismatchAttempt = payment({ attemptId: "attempt-mismatch", idempotencyKey: "idempotency-mismatch", orderGuid: "order-mismatch" });
    assert.equal(await reopenedRepositories.payments.insertIfUnblocked(mismatchAttempt), null);
    assert.equal(await reopenedRepositories.payments.compareAndUpdate(mismatchAttempt, { ...mismatchAttempt, state: "Approved", updatedAtIso: "2026-07-28T00:01:00.000Z" }), true);
    await reopened.run("INSERT INTO order_tenders (tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso) VALUES (?, ?, ?, ?, ?, ?)", ["wrong-order", "another-order", "card", 500, "attempt-mismatch", "2026-07-28T00:01:00.000Z"]);
    assert.equal((await reopenedRepositories.payments.findBlocking("order-mismatch"))?.attemptId, "attempt-mismatch");

    await insertDraftOrder(reopened, "order-correct", 5, 500);
    const correctAttempt = payment({ attemptId: "attempt-correct", idempotencyKey: "idempotency-correct", orderGuid: "order-correct", provider: "linkly-cloud" });
    assert.equal(await reopenedRepositories.payments.insertIfUnblocked(correctAttempt), null);
    assert.equal(await reopenedRepositories.payments.compareAndUpdate(correctAttempt, { ...correctAttempt, state: "Approved", updatedAtIso: "2026-07-28T00:01:00.000Z" }), true);
    await reopened.run("INSERT INTO order_tenders (tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso) VALUES (?, ?, ?, ?, ?, ?)", ["correct", "order-correct", "card", 500, "attempt-correct", "2026-07-28T00:01:00.000Z"]);
    assert.equal(await reopenedRepositories.payments.findBlocking("order-correct"), null);
    await assert.rejects(
      () => reopened.run("INSERT INTO order_tenders (tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso) VALUES (?, ?, ?, ?, ?, ?)", ["duplicate", "order-correct", "card", 500, "attempt-correct", "2026-07-28T00:01:00.000Z"]),
      /UNIQUE constraint failed: order_tenders\.payment_attempt_id/,
    );

    const stale = payment({ state: "Submitted", updatedAtIso: "2026-07-28T00:02:00.000Z" });
    assert.equal(await repositories.payments.compareAndUpdate(created, stale), false);
    assert.equal((await repositories.payments.get("attempt-1"))?.state, "Approved");
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});

test("真实 SQLite：Created attempt 仅允许绑定 Draft/Completing 订单，缺单和所有完成态均失败关闭", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-payment-order-gate-"));
  const databasePath = join(folder, "payment-order-gate.db");
  const connection = new SystemSqliteConnection(databasePath);
  try {
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-gate",
      encryptor: {
        async encrypt(plaintext) { return new TextEncoder().encode(plaintext); },
        async decrypt(ciphertext) { return new TextDecoder().decode(ciphertext); },
      },
    });
    const cases = [
      ["Draft", true],
      ["Completing", true],
      ["CompletedLocal", false],
      ["PendingSync", false],
      ["Syncing", false],
      ["Synced", false],
      ["Blocked403", false],
      ["Rejected", false],
    ] as const satisfies readonly (readonly [LocalOrderState, boolean])[];

    for (const [index, [state, allowed]] of cases.entries()) {
      const orderGuid = `order-gate-${state}`;
      const attemptId = `attempt-gate-${state}`;
      await insertDraftOrder(connection, orderGuid, index + 100, 500, state);
      const candidate = payment({
        attemptId,
        idempotencyKey: `idempotency-gate-${state}`,
        orderGuid,
      });

      if (allowed) {
        assert.equal(await repositories.payments.insertIfUnblocked(candidate), null);
      } else {
        await assert.rejects(
          () => repositories.payments.insertIfUnblocked(candidate),
          /Draft or Completing local order/,
        );
      }
      assert.equal(
        await countAttempts(connection, attemptId),
        allowed ? 1 : 0,
        `${state} must ${allowed ? "" : "not "}persist a Created attempt`,
      );
    }

    const missing = payment({
      attemptId: "attempt-gate-missing",
      idempotencyKey: "idempotency-gate-missing",
      orderGuid: "order-gate-missing",
    });
    await assert.rejects(
      () => repositories.payments.insertIfUnblocked(missing),
      /Draft or Completing local order/,
    );
    assert.equal(await countAttempts(connection, missing.attemptId), 0);
  } finally {
    await connection.close();
    rmSync(folder, { recursive: true, force: true });
  }
});

test("真实 SQLite 竞态：订单完成先提交时，Created attempt 持久门拒绝且外部支付调用保持为零", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-payment-race-"));
  const databasePath = join(folder, "payment-race.db");
  const connection = new SystemSqliteConnection(databasePath);
  let releaseEncryption!: () => void;
  let reportEncryptionStarted!: () => void;
  const encryptionStarted = new Promise<void>((resolve) => {
    reportEncryptionStarted = resolve;
  });
  const encryptionMayFinish = new Promise<void>((resolve) => {
    releaseEncryption = resolve;
  });

  try {
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await insertDraftOrder(connection, "order-race", 200, 500, "Draft");
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-race",
      encryptor: {
        async encrypt(plaintext) {
          reportEncryptionStarted();
          await encryptionMayFinish;
          return new TextEncoder().encode(plaintext);
        },
        async decrypt(ciphertext) { return new TextDecoder().decode(ciphertext); },
      },
    });
    let externalPaymentCalls = 0;
    const startPayment = async (): Promise<void> => {
      await repositories.payments.insertIfUnblocked(payment({
        attemptId: "attempt-race",
        idempotencyKey: "idempotency-race",
        orderGuid: "order-race",
        provider: "voucher",
        references: {
          checkoutId: null,
          paymentId: null,
          sessionId: null,
          txnRef: null,
          rfn: null,
          voucherReservationToken: "reservation-race",
        },
      }));
      externalPaymentCalls += 1;
    };

    const paymentStart = startPayment();
    await encryptionStarted;
    await connection.withExclusiveTransaction(async (transaction) => {
      const completing = await transaction.run(
        "UPDATE local_orders SET state = 'Completing' WHERE order_guid = ? AND state = 'Draft'",
        ["order-race"],
      );
      const completed = await transaction.run(
        "UPDATE local_orders SET state = 'PendingSync' WHERE order_guid = ? AND state = 'Completing'",
        ["order-race"],
      );
      assert.equal(completing.changes, 1);
      assert.equal(completed.changes, 1);
    });
    releaseEncryption();

    await assert.rejects(paymentStart, /Draft or Completing local order/);
    assert.equal(await countAttempts(connection, "attempt-race"), 0);
    assert.equal(externalPaymentCalls, 0);
  } finally {
    releaseEncryption();
    await connection.close();
    rmSync(folder, { recursive: true, force: true });
  }
});

test("真实 SQLite：订单读取只从同订单 Approved attempt 恢复可证明的支付引用", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-order-tender-db-"));
  const databasePath = join(folder, "order-tender.db");
  try {
    const connection = new SystemSqliteConnection(databasePath);
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    const encryptor = {
      async encrypt(plaintext: string) { return new TextEncoder().encode(plaintext); },
      async decrypt(ciphertext: Uint8Array) { return new TextDecoder().decode(ciphertext); },
    };
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-tender",
      encryptor,
    });

    await insertDraftOrder(connection, "order-square-sale", 10, 500);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-square-sale",
      orderGuid: "order-square-sale",
      provider: "square",
      operation: "purchase",
      amountCents: 500,
      state: "Approved",
      paymentId: "payment-stable-1",
      receiptCiphertext: new TextEncoder().encode("PAN 4111111111111111"),
    });
    await insertTender(connection, "tender-square-sale", "order-square-sale", "card", 500, "attempt-square-sale");
    assert.equal(
      (await repositories.orders.getByGuid("order-square-sale"))?.tenders[0]?.reference,
      "SQ:payment-stable-1",
    );

    await insertDraftOrder(connection, "order-square-refund", 11, -500);
    // DB/committer 的退款账本使用负分币；PaymentAttemptService/Provider 尚待上游统一
    // operation-aware 符号与 provider 绝对值，本用例只证明落库后的引用恢复，不代表端到端退款已开放。
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-square-refund",
      orderGuid: "order-square-refund",
      provider: "square",
      operation: "refund",
      amountCents: -500,
      state: "Approved",
      paymentId: "payment-original",
      responseCode: "refund-1",
    });
    await insertTender(connection, "tender-square-refund", "order-square-refund", "card", -500, "attempt-square-refund");
    assert.equal(
      (await repositories.orders.getByGuid("order-square-refund"))?.tenders[0]?.reference,
      "CARD_REFUND|refund=SQRF%3Arefund-1|original=SQ%3Apayment-original",
    );

    await insertDraftOrder(connection, "order-missing-reference", 12, 500);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-missing-reference",
      orderGuid: "order-missing-reference",
      provider: "square",
      operation: "purchase",
      amountCents: 500,
      state: "Approved",
      paymentId: null,
      receiptCiphertext: new TextEncoder().encode("receipt must never become a reference"),
    });
    await insertTender(connection, "tender-missing-reference", "order-missing-reference", "card", 500, "attempt-missing-reference");
    assert.equal(
      (await repositories.orders.getByGuid("order-missing-reference"))?.tenders[0]?.reference,
      null,
    );

    await insertDraftOrder(connection, "order-pending-attempt", 13, 500);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-pending",
      orderGuid: "order-pending-attempt",
      provider: "square",
      operation: "purchase",
      amountCents: 500,
      state: "Pending",
      paymentId: "payment-must-not-hydrate",
    });
    await insertTender(connection, "tender-pending", "order-pending-attempt", "card", 500, "attempt-pending");
    assert.equal(
      (await repositories.orders.getByGuid("order-pending-attempt"))?.tenders[0]?.reference,
      null,
    );

    await insertDraftOrder(connection, "order-attempt-owner", 14, 500);
    await insertDraftOrder(connection, "order-cross-binding", 15, 500);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-cross-order",
      orderGuid: "order-attempt-owner",
      provider: "square",
      operation: "purchase",
      amountCents: 500,
      state: "Approved",
      paymentId: "payment-cross-order",
    });
    await insertTender(connection, "tender-cross-order", "order-cross-binding", "card", 500, "attempt-cross-order");
    assert.equal(
      (await repositories.orders.getByGuid("order-cross-binding"))?.tenders[0]?.reference,
      null,
    );
    await assert.rejects(
      () => insertTender(connection, "tender-duplicate-binding", "order-attempt-owner", "card", 500, "attempt-cross-order"),
      /UNIQUE constraint failed: order_tenders\.payment_attempt_id/,
    );

    await insertDraftOrder(connection, "order-linkly-gated", 16, 500);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-linkly-gated",
      orderGuid: "order-linkly-gated",
      provider: "linkly-cloud",
      operation: "purchase",
      amountCents: 500,
      state: "Approved",
      sessionId: "session-1",
      txnRef: "txn-1",
      rfn: "rfn-1",
    });
    await insertTender(connection, "tender-linkly-gated", "order-linkly-gated", "card", 500, "attempt-linkly-gated");
    assert.equal(
      (await repositories.orders.getByGuid("order-linkly-gated"))?.tenders[0]?.reference,
      null,
    );

    await insertDraftOrder(connection, "order-voucher-gated", 17, 500);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-voucher-gated",
      orderGuid: "order-voucher-gated",
      provider: "voucher",
      operation: "purchase",
      amountCents: 500,
      state: "Approved",
      providerPayloadCiphertext: new TextEncoder().encode(JSON.stringify({
        voucherReservationToken: "vpr_attempt_voucher_gated",
      })),
    });
    await insertTender(connection, "tender-voucher-gated", "order-voucher-gated", "voucher", 500, "attempt-voucher-gated");
    assert.deepEqual(
      (await repositories.orders.getByGuid("order-voucher-gated"))?.tenders[0],
      {
        tenderGuid: "tender-voucher-gated",
        method: "voucher",
        amount: { currency: "AUD", cents: 500 },
        reference: null,
        reservationToken: null,
      },
    );
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});

test("真实 SQLite：支付 CAS 原子合并卡同步证据并保持公开 PaymentAttempt 脱敏", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-payment-protected-cas-"));
  const databasePath = join(folder, "payment-protected-cas.db");
  const connection = new SystemSqliteConnection(databasePath);
  const encryptor = {
    async encrypt(plaintext: string) {
      return new TextEncoder().encode(plaintext);
    },
    async decrypt(ciphertext: Uint8Array) {
      return new TextDecoder().decode(ciphertext);
    },
  };
  try {
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await insertDraftOrder(connection, "order-protected", 300, 500);
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-protected",
      encryptor,
    });
    const reader = new SqlitePaymentProtectedMaterialReader(
      connection,
      encryptor,
    );
    const created = payment({
      attemptId: "attempt-protected",
      idempotencyKey: "idempotency-protected",
      orderGuid: "order-protected",
      references: {
        checkoutId: null,
        paymentId: null,
        sessionId: null,
        txnRef: null,
        rfn: null,
        voucherReservationToken: "reservation-protected",
      },
    });
    assert.equal(await repositories.payments.insertIfUnblocked(created), null);
    assert.deepEqual(
      await readProtectedPayload(connection, encryptor, created.attemptId),
      {
        version: 1,
        voucherReservationToken: "reservation-protected",
        cardSyncEvidence: null,
      },
    );

    const evidence = cardSyncEvidence({
      txnRef: "sync-txn-protected",
      authCode: "AUTH-PROTECTED",
      maskedCardNumber: "411111******1111",
    });
    const approved = payment({
      ...created,
      state: "Approved",
      updatedAtIso: "2026-07-28T00:01:00.000Z",
      references: {
        ...created.references,
        paymentId: "square-payment-protected",
      },
    });
    assert.equal(
      await repositories.payments.compareAndUpdate(
        created,
        approved,
        evidence,
      ),
      true,
    );
    assert.deepEqual(
      await readProtectedPayload(connection, encryptor, created.attemptId),
      {
        version: 1,
        voucherReservationToken: "reservation-protected",
        cardSyncEvidence: evidence,
      },
    );

    const publicAttempt = await repositories.payments.get(created.attemptId);
    assert.equal(publicAttempt?.references.voucherReservationToken, "reservation-protected");
    assert.equal(publicAttempt === null ? false : "cardSyncEvidence" in publicAttempt, false);
    assert.equal(JSON.stringify(publicAttempt).includes("AUTH-PROTECTED"), false);
    const publicBlockingAttempt = await repositories.payments.findBlocking(
      created.orderGuid,
    );
    assert.equal(
      publicBlockingAttempt === null
        ? false
        : "cardSyncEvidence" in publicBlockingAttempt,
      false,
    );
    assert.equal(
      JSON.stringify(publicBlockingAttempt).includes("AUTH-PROTECTED"),
      false,
    );
    assert.deepEqual(
      await reader.read({
        attemptId: created.attemptId,
        orderGuid: created.orderGuid,
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      evidence,
    );

    const preserved = {
      ...approved,
      updatedAtIso: "2026-07-28T00:02:00.000Z",
    };
    assert.equal(
      await repositories.payments.compareAndUpdate(approved, preserved),
      true,
    );
    assert.deepEqual(
      await reader.read({
        attemptId: created.attemptId,
        orderGuid: created.orderGuid,
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      evidence,
    );

    const replacement = cardSyncEvidence({
      txnRef: "sync-txn-must-not-win",
      authCode: "AUTH-MUST-NOT-WIN",
    });
    assert.equal(
      await repositories.payments.compareAndUpdate(
        approved,
        {
          ...approved,
          updatedAtIso: "2026-07-28T00:03:00.000Z",
        },
        replacement,
      ),
      false,
    );
    assert.deepEqual(
      await reader.read({
        attemptId: created.attemptId,
        orderGuid: created.orderGuid,
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      evidence,
    );

    await insertDraftOrder(connection, "order-stale-evidence", 301, 500);
    const noEvidence = payment({
      attemptId: "attempt-stale-evidence",
      idempotencyKey: "idempotency-stale-evidence",
      orderGuid: "order-stale-evidence",
    });
    assert.equal(await repositories.payments.insertIfUnblocked(noEvidence), null);
    assert.equal(
      await repositories.payments.compareAndUpdate(
        { ...noEvidence, updatedAtIso: "2026-07-28T00:00:01.000Z" },
        {
          ...noEvidence,
          state: "Approved",
          updatedAtIso: "2026-07-28T00:01:00.000Z",
        },
        evidence,
      ),
      false,
    );
    assert.equal(
      await reader.read({
        attemptId: noEvidence.attemptId,
        orderGuid: noEvidence.orderGuid,
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      null,
    );

    const forgedExpected = {
      ...noEvidence,
      orderGuid: "order-forged-caller",
    };
    assert.equal(
      await repositories.payments.compareAndUpdate(
        forgedExpected,
        {
          ...forgedExpected,
          state: "Approved",
          updatedAtIso: "2026-07-28T00:03:00.000Z",
        },
        evidence,
      ),
      false,
    );
    assert.equal(
      await reader.read({
        attemptId: noEvidence.attemptId,
        orderGuid: noEvidence.orderGuid,
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      null,
    );
    assert.equal(
      await reader.read({
        attemptId: "attempt-does-not-exist",
        orderGuid: "order-does-not-exist",
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      null,
    );

    const approvedWithoutEvidence = {
      ...noEvidence,
      state: "Approved" as const,
      updatedAtIso: "2026-07-28T00:04:00.000Z",
    };
    await assert.rejects(
      () => repositories.payments.compareAndUpdate(
        noEvidence,
        approvedWithoutEvidence,
        {
          ...evidence,
          pan: "4111111111111111",
        } as CardSyncEvidenceV1,
      ),
      /unsupported field: pan/,
    );
    await assert.rejects(
      () => repositories.payments.compareAndUpdate(
        noEvidence,
        approvedWithoutEvidence,
        {
          ...evidence,
          rawPayload: { approved: true },
        } as CardSyncEvidenceV1,
      ),
      /unsupported field: rawPayload/,
    );
    await assert.rejects(
      () => repositories.payments.compareAndUpdate(
        noEvidence,
        approvedWithoutEvidence,
        {
          ...evidence,
          responseText: "{\"approved\":true,\"pan\":\"4111111111111111\"}",
        },
      ),
      /unmasked PAN/,
    );
    await assert.rejects(
      () => repositories.payments.compareAndUpdate(
        noEvidence,
        approvedWithoutEvidence,
        {
          ...evidence,
          maskedCardNumber: "4111111111111111",
        },
      ),
      /masked card number is invalid/,
    );
    assert.equal(
      await reader.read({
        attemptId: noEvidence.attemptId,
        orderGuid: noEvidence.orderGuid,
        provider: "square",
        operation: "purchase",
        amountCents: 500,
      }),
      null,
    );
  } finally {
    await connection.close();
    rmSync(folder, { recursive: true, force: true });
  }
});

test("真实 SQLite：legacy 券 token 可读，并在下一次 CAS 升级为 voucher 与卡证据并存的 v1 envelope", async () => {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-payment-protected-legacy-"));
  const databasePath = join(folder, "payment-protected-legacy.db");
  const connection = new SystemSqliteConnection(databasePath);
  const encryptor = {
    async encrypt(plaintext: string) {
      return new TextEncoder().encode(plaintext);
    },
    async decrypt(ciphertext: Uint8Array) {
      return new TextDecoder().decode(ciphertext);
    },
  };
  try {
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await insertDraftOrder(connection, "order-legacy-protected", 302, -700);
    await insertPaymentAttemptRow(connection, {
      attemptId: "attempt-legacy-protected",
      orderGuid: "order-legacy-protected",
      provider: "square",
      operation: "refund",
      amountCents: -700,
      state: "Created",
      providerPayloadCiphertext: new TextEncoder().encode(JSON.stringify({
        voucherReservationToken: "reservation-legacy",
      })),
    });
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => "2026-07-28T00:00:00.000Z",
      createLeaseId: () => "lease-legacy",
      encryptor,
    });
    const reader = new SqlitePaymentProtectedMaterialReader(
      connection,
      encryptor,
    );
    const legacy = await repositories.payments.get("attempt-legacy-protected");
    assert.ok(legacy);
    assert.equal(legacy.references.voucherReservationToken, "reservation-legacy");
    assert.equal(
      await reader.read({
        attemptId: legacy.attemptId,
        orderGuid: legacy.orderGuid,
        provider: "square",
        operation: "refund",
        amountCents: -700,
      }),
      null,
    );

    const evidence = cardSyncEvidence({
      operation: "refund",
      amountCents: 700,
      txnRef: "legacy-refund-txn",
      refundReference: "legacy-refund-reference",
    });
    const approved = {
      ...legacy,
      state: "Approved" as const,
      updatedAtIso: "2026-07-28T00:02:00.000Z",
    };
    assert.equal(
      await repositories.payments.compareAndUpdate(
        legacy,
        approved,
        evidence,
      ),
      true,
    );
    assert.deepEqual(
      await readProtectedPayload(
        connection,
        encryptor,
        legacy.attemptId,
      ),
      {
        version: 1,
        voucherReservationToken: "reservation-legacy",
        cardSyncEvidence: evidence,
      },
    );
    assert.deepEqual(
      await reader.read({
        attemptId: legacy.attemptId,
        orderGuid: legacy.orderGuid,
        provider: "square",
        operation: "refund",
        amountCents: -700,
      }),
      evidence,
    );
  } finally {
    await connection.close();
    rmSync(folder, { recursive: true, force: true });
  }
});

async function insertDraftOrder(
  connection: SqliteConnectionPort,
  orderGuid: string,
  sequence: number,
  amountCents: number,
  state: LocalOrderState = "Draft",
): Promise<void> {
  await connection.run(
    "INSERT INTO local_orders (order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name, sold_at_iso, state, total_cents, discount_cents, actual_amount_cents, original_order_guid, created_at_iso, updated_at_iso) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
    [orderGuid, sequence, "S1", "IPAD1", "cashier-1", "Cashier", "2026-07-28T00:00:00.000Z", state, amountCents, 0, amountCents, null, "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z"],
  );
}

async function countAttempts(
  connection: SqliteConnectionPort,
  attemptId: string,
): Promise<number> {
  const row = await connection.getFirst<{ count: number }>(
    "SELECT COUNT(*) AS count FROM payment_attempts WHERE attempt_id = ?",
    [attemptId],
  );
  return Number(row?.count ?? 0);
}

async function insertPaymentAttemptRow(
  connection: SqliteConnectionPort,
  input: Readonly<{
    attemptId: string;
    orderGuid: string;
    provider: PaymentAttempt["provider"];
    operation: PaymentAttempt["operation"];
    amountCents: number;
    state: PaymentAttempt["state"];
    paymentId?: string | null;
    sessionId?: string | null;
    txnRef?: string | null;
    rfn?: string | null;
    providerPayloadCiphertext?: Uint8Array | null;
    receiptCiphertext?: Uint8Array | null;
    responseCode?: string | null;
  }>,
): Promise<void> {
  await connection.run(
    `INSERT INTO payment_attempts (
      attempt_id, idempotency_key, order_guid, provider, operation, amount_cents,
      state, checkout_id, payment_id, session_id, txn_ref, rfn,
      provider_payload_ciphertext, provider_receipt_ciphertext,
      provider_response_code, created_at_iso, updated_at_iso, last_error_code
    ) VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL)`,
    [
      input.attemptId,
      `idempotency-${input.attemptId}`,
      input.orderGuid,
      input.provider,
      input.operation,
      input.amountCents,
      input.state,
      input.paymentId ?? null,
      input.sessionId ?? null,
      input.txnRef ?? null,
      input.rfn ?? null,
      input.providerPayloadCiphertext ?? null,
      input.receiptCiphertext ?? null,
      input.responseCode ?? null,
      "2026-07-28T00:00:00.000Z",
      "2026-07-28T00:01:00.000Z",
    ],
  );
}

async function insertTender(
  connection: SqliteConnectionPort,
  tenderGuid: string,
  orderGuid: string,
  method: "cash" | "card" | "voucher",
  amountCents: number,
  attemptId: string | null,
): Promise<void> {
  await connection.run(
    "INSERT INTO order_tenders (tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso) VALUES (?, ?, ?, ?, ?, ?)",
    [tenderGuid, orderGuid, method, amountCents, attemptId, "2026-07-28T00:02:00.000Z"],
  );
}

async function readProtectedPayload(
  connection: SqliteConnectionPort,
  encryptor: Readonly<{
    decrypt(ciphertext: Uint8Array): Promise<string>;
  }>,
  attemptId: string,
): Promise<unknown> {
  const row = await connection.getFirst<{ provider_payload_ciphertext: unknown }>(
    "SELECT provider_payload_ciphertext FROM payment_attempts WHERE attempt_id = ?",
    [attemptId],
  );
  assert.ok(row?.provider_payload_ciphertext instanceof Uint8Array);
  return JSON.parse(
    await encryptor.decrypt(row.provider_payload_ciphertext),
  ) as unknown;
}

function cardSyncEvidence(
  overrides: Partial<CardSyncEvidenceV1> = {},
): CardSyncEvidenceV1 {
  return {
    version: 1,
    provider: "square",
    operation: "purchase",
    processor: "Square",
    txnRef: "sync-txn-1",
    authCode: null,
    cardType: "VISA",
    cardBin: 411111,
    maskedCardNumber: "411111******1111",
    merchantId: "merchant-1",
    responseCode: "00",
    responseText: "APPROVED",
    stan: "123456",
    bankDateTimeIso: "2026-07-28T00:00:00.000Z",
    amountCents: 500,
    refundReference: null,
    ...overrides,
  };
}

function runStatement(
  database: DatabaseSync,
  sql: string,
  parameters: readonly SqlValue[],
): SqlRunResult {
  const result = database
    .prepare(sql)
    .run(...parameters as readonly SQLInputValue[]);
  return {
    changes: Number(result.changes),
    lastInsertRowId: Number(result.lastInsertRowid),
  };
}

function getFirst<T extends object>(
  database: DatabaseSync,
  sql: string,
  parameters: readonly SqlValue[],
): T | null {
  const row = database
    .prepare(sql)
    .get(...parameters as readonly SQLInputValue[]);
  return row === undefined ? null : row as unknown as T;
}

function getAll<T extends object>(
  database: DatabaseSync,
  sql: string,
  parameters: readonly SqlValue[],
): readonly T[] {
  return database
    .prepare(sql)
    .all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[];
}
