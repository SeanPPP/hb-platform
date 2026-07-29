import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { createAud } from "../contracts";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import { SqliteVoucherProtectedTokenStore } from "./sqlite-voucher-protected-token-store";
import {
  SqliteVoucherTenderReversalStore,
  type VoucherTenderReversalCommand,
} from "./sqlite-voucher-tender-reversal-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const T0 = "2026-07-28T00:00:00.000Z";
const T1 = "2026-07-28T00:01:00.000Z";

type SeededVoucherTenderReversalCommand =
  VoucherTenderReversalCommand &
  Readonly<{
    expectedSourceAttemptId: string;
    expectedAmountCents: number;
  }>;

const encryptor: SensitivePayloadEncryptor = {
  async encrypt(plaintext) {
    return Uint8Array.from(
      new TextEncoder().encode(plaintext),
      (value) => value ^ 0xa5,
    );
  },
  async decrypt(ciphertext) {
    return new TextDecoder().decode(
      Uint8Array.from(ciphertext, (value) => value ^ 0xa5),
    );
  },
};

test("M16 fresh/M15 升级均建立账本，并拒绝退货履约 action/order 交叉绑定", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 15),
    );
    assert.equal(await schemaVersion(connection), 15);

    await applyMigrations(
      connection,
      () => T1,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
    );
    assert.equal(await schemaVersion(connection), 17);
    assert.equal(
      await scalar(
        connection,
        `SELECT COUNT(*) AS count
         FROM sqlite_master
         WHERE type = 'table'
           AND name = 'voucher_tender_reversal_actions'`,
      ),
      1,
    );

    await seedReturnAction(connection, "return-action-a", "return-order-a", 1);
    await seedReturnAction(connection, "return-action-b", "return-order-b", 2);
    await assert.rejects(
      connection.run(
        `INSERT INTO return_fulfilment_plans (
          action_id, return_order_guid, print_job_id, drawer_event_id,
          receipt_kind, print_receipt, drawer_required,
          materialized_at_iso, created_at_iso
        ) VALUES (
          'return-action-a', 'return-order-b', NULL, NULL,
          'none', 0, 0, NULL, ?
        )`,
        [T1],
      ),
      /RETURN_FULFILMENT_PLAN_ACTION_ORDER_MISMATCH/,
    );

    await connection.run(
      `INSERT INTO return_fulfilment_plans (
        action_id, return_order_guid, print_job_id, drawer_event_id,
        receipt_kind, print_receipt, drawer_required,
        materialized_at_iso, created_at_iso
      ) VALUES (
        'return-action-a', 'return-order-a', NULL, NULL,
        'none', 0, 0, NULL, ?
      )`,
      [T1],
    );
    await assert.rejects(
      connection.run(
        `UPDATE return_fulfilment_plans
         SET return_order_guid = 'return-order-b'
         WHERE action_id = 'return-action-a'`,
      ),
      /RETURN_FULFILMENT_PLAN_(ACTION_ORDER_MISMATCH|IDENTITY_IMMUTABLE)/,
    );
  });

  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
    );
    assert.equal(await schemaVersion(connection), 17);
  });

  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 15),
    );
    await seedReturnAction(
      connection,
      "legacy-return-action-a",
      "legacy-return-order-a",
      11,
    );
    await seedReturnAction(
      connection,
      "legacy-return-action-b",
      "legacy-return-order-b",
      12,
    );
    await connection.run(
      `INSERT INTO return_fulfilment_plans (
        action_id, return_order_guid, print_job_id, drawer_event_id,
        receipt_kind, print_receipt, drawer_required,
        materialized_at_iso, created_at_iso
      ) VALUES (
        'legacy-return-action-a', 'legacy-return-order-b', NULL, NULL,
        'none', 0, 0, NULL, ?
      )`,
      [T0],
    );

    await assert.rejects(
      applyMigrations(connection, () => T1),
      /RETURN_FULFILMENT_PLAN_ACTION_ORDER_MISMATCH/,
    );
    assert.equal(await schemaVersion(connection), 15);
    assert.equal(
      await scalar(
        connection,
        `SELECT COUNT(*) AS count
         FROM sqlite_master
         WHERE type = 'table'
           AND name = 'voucher_tender_reversal_actions'`,
      ),
      0,
    );
  });
});

test("M16 金额列只接受 SQLite integer，拒绝 REAL 绕过整数分币", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(
      connection,
      "fractional-amount",
      500,
    );
    await connection.exec(
      "DROP TRIGGER trg_voucher_tender_reversal_validate_insert;",
    );

    await assert.rejects(
      connection.run(
        `INSERT INTO voucher_tender_reversal_actions (
          action_id, order_guid, source_tender_guid, source_attempt_id,
          amount_cents, reason, state, attempt_count, last_error_code,
          reversal_tender_guid, terminal_audit_event_id, submitted_at_iso,
          terminal_at_iso, created_at_iso, updated_at_iso
        ) VALUES (
          'fractional-action', ?, ?, ?, 500.5, 'SALE', 'Prepared', 0,
          NULL, NULL, NULL, NULL, NULL, ?, ?
        )`,
        [
          command.orderGuid,
          command.sourceTenderGuid,
          command.expectedSourceAttemptId,
          T0,
          T0,
        ],
      ),
      /CHECK constraint failed/,
    );
  });
});

test("重启后按门店设备恢复同一未决撤券动作，公开恢复查询不含券码或 token", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(
      connection,
      "restart-recovery",
      500,
    );
    const beforeRestart = createStore(connection);
    const submitted = await beforeRestart.markSubmitted(
      await beforeRestart.prepareOrLoad(command),
    );
    await beforeRestart.markUnknown(
      submitted,
      "VOUCHER_RELEASE_RESULT_UNRESOLVED",
    );

    const afterRestart = createStore(connection);
    const recovered = await afterRestart.findBlocking({
      storeCode: "STORE-1",
      deviceCode: "DEVICE-1",
    });
    assert.equal(recovered?.actionId, command.actionId);
    assert.equal(recovered?.orderGuid, command.orderGuid);
    assert.equal(recovered?.sourceTenderGuid, command.sourceTenderGuid);
    assert.equal(recovered?.state, "Unknown");
    assert.equal(recovered?.attemptCount, 1);
    const encoded = JSON.stringify(recovered);
    assert.equal(encoded.includes("VOUCHER-"), false);
    assert.equal(encoded.includes("reservation-"), false);

    assert.equal(
      await afterRestart.findBlocking({
        storeCode: "STORE-1",
        deviceCode: "OTHER-DEVICE",
      }),
      null,
    );
  });
});

test("同一门店设备出现多笔未决撤券时整体失败关闭，不静默挑选一笔", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const first = await seedApprovedVoucherPurchase(
      connection,
      "multiple-recovery-a",
      500,
    );
    const second = await seedApprovedVoucherPurchase(
      connection,
      "multiple-recovery-b",
      600,
    );
    const store = createStore(connection);
    await store.prepareOrLoad(first);
    await store.prepareOrLoad(second);

    await assert.rejects(
      store.findBlocking({
        storeCode: "STORE-1",
        deviceCode: "DEVICE-1",
      }),
      /Multiple unresolved voucher tender reversals require support/,
    );
  });
});

test("prepare/submit/unknown/retry/released 原子追加精确负券 tender，崩溃重放返回同一事实", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(connection, "happy", 500);
    await saveReleasedProtectedState(connection, command);
    const store = createStore(connection);

    const prepared = await store.prepareOrLoad(command);
    assert.equal(prepared.state, "Prepared");
    assert.equal(prepared.attemptCount, 0);
    assert.equal(
      prepared.sourceAttemptId,
      command.expectedSourceAttemptId,
    );
    assert.equal(prepared.amount.cents, 500);
    assert.equal(
      JSON.stringify(prepared).includes("VOUCHER-HAPPY"),
      false,
    );
    assert.equal(JSON.stringify(prepared).includes("reservation-"), false);
    assert.equal(Object.isFrozen(prepared), true);
    assert.equal(Object.isFrozen(prepared.amount), true);

    const submitted = await store.markSubmitted(prepared);
    assert.equal(submitted.state, "Submitted");
    assert.equal(submitted.attemptCount, 1);
    const submittedReplay = await store.markSubmitted(prepared);
    assert.deepEqual(submittedReplay, submitted);

    const unknown = await store.markUnknown(
      submitted,
      "VOUCHER_RELEASE_RESULT_UNRESOLVED",
    );
    assert.equal(unknown.state, "Unknown");
    assert.equal(unknown.reversalTenderGuid, null);
    assert.equal(
      await negativeTenderCount(connection, command.orderGuid),
      0,
    );

    const retry = await store.markSubmitted(unknown);
    assert.equal(retry.state, "Submitted");
    assert.equal(retry.attemptCount, 2);
    const resubmitted = await store.markSubmitted(retry);
    assert.equal(resubmitted.state, "Submitted");
    assert.equal(resubmitted.attemptCount, 3);
    const reversed = await store.commitReleased(resubmitted, {
      state: "Cancelled",
      responseCode: "VOUCHER_RELEASED",
    });
    assert.equal(reversed.state, "Reversed");
    assert.equal(reversed.reversalTenderGuid, "voucher-reversal-1");
    assert.equal(reversed.truth.tenders.length, 2);
    assert.equal(
      reversed.truth.tenders.find(
        (tender) => tender.tenderGuid === "voucher-reversal-1",
      )?.amount.cents,
      -500,
    );

    const reversal = await connection.getFirst<{
      amount_cents: unknown;
      payment_attempt_id: unknown;
    }>(
      `SELECT amount_cents, payment_attempt_id
       FROM order_tenders
       WHERE tender_guid = 'voucher-reversal-1'`,
    );
    assert.equal(Number(reversal?.amount_cents), -500);
    assert.equal(reversal?.payment_attempt_id, null);
    assert.equal(
      await scalar(
        connection,
        `SELECT COUNT(*) AS count
         FROM payment_tender_reversal_links
         WHERE order_guid = ? AND action_id = ?`,
        [command.orderGuid, command.actionId],
      ),
      1,
    );
    const audit = await connection.getFirst<{
      event_type: unknown;
      payload_json: unknown;
    }>(
      `SELECT event_type, payload_json
       FROM audit_events
       WHERE correlation_id = ?`,
      [command.actionId],
    );
    assert.equal(audit?.event_type, "PAYMENT_TENDER_REMOVE");
    assert.deepEqual(JSON.parse(String(audit?.payload_json)), {
      action: "payment-tender-remove",
      outcome: "success",
      reason: command.reason,
      amountCents: 500,
      sourceTenderGuid: command.sourceTenderGuid,
      sourceAttemptId: command.expectedSourceAttemptId,
      reversalTenderGuid: "voucher-reversal-1",
    });

    const replay = await store.commitReleased(retry, {
      state: "Cancelled",
      responseCode: "VOUCHER_RELEASED",
    });
    assert.deepEqual(replay, reversed);
    assert.equal(
      await negativeTenderCount(connection, command.orderGuid),
      1,
    );
  });
});

test("M16 终态 audit 身份冻结，但 uploaded_at_iso 只允许从 NULL 推进一次", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(
      connection,
      "audit-upload",
      500,
    );
    await saveReleasedProtectedState(connection, command);
    const store = createStore(connection);
    const submitted = await store.markSubmitted(
      await store.prepareOrLoad(command),
    );
    await store.commitReleased(submitted, {
      state: "Cancelled",
      responseCode: "VOUCHER_RELEASED",
    });
    const event = await connection.getFirst<{
      event_id: unknown;
      uploaded_at_iso: unknown;
    }>(
      `SELECT event_id, uploaded_at_iso
       FROM audit_events
       WHERE correlation_id = ?`,
      [command.actionId],
    );
    const eventId = String(event?.event_id);
    assert.equal(event?.uploaded_at_iso, null);

    // SQLite IS 把 NULL IS NULL 视为真；没有事实变化的 UPDATE 不应误报。
    await connection.run(
      `UPDATE audit_events
       SET event_type = event_type,
         uploaded_at_iso = uploaded_at_iso
       WHERE event_id = ?`,
      [eventId],
    );
    await connection.run(
      `UPDATE audit_events
       SET uploaded_at_iso = '2026-07-28T00:02:00.000Z'
       WHERE event_id = ?`,
      [eventId],
    );
    assert.equal(
      (
        await connection.getFirst<{ uploaded_at_iso: unknown }>(
          "SELECT uploaded_at_iso FROM audit_events WHERE event_id = ?",
          [eventId],
        )
      )?.uploaded_at_iso,
      "2026-07-28T00:02:00.000Z",
    );
    await connection.run(
      `UPDATE audit_events
       SET uploaded_at_iso = '2026-07-28T00:02:00.000Z'
       WHERE event_id = ?`,
      [eventId],
    );

    for (const mutation of [
      "event_id = 'changed-event-id'",
      "event_type = 'CHANGED_EVENT_TYPE'",
      "occurred_at_iso = '2026-07-28T00:03:00.000Z'",
      "order_guid = NULL",
      "correlation_id = 'changed-correlation'",
      "payload_json = '{}'",
      "uploaded_at_iso = '2026-07-28T00:03:00.000Z'",
      "uploaded_at_iso = NULL",
    ]) {
      await assert.rejects(
        connection.run(
          `UPDATE audit_events SET ${mutation} WHERE event_id = ?`,
          [eventId],
        ),
        /VOUCHER_TENDER_REVERSAL_AUDIT_IMMUTABLE/,
      );
    }
  });
});

test("所有重放必须保持 action/order/tender/attempt/reason/amount 一致，单订单只容纳一个未解决动作", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(connection, "identity", 600);
    const second = await seedApprovedVoucherPurchase(
      connection,
      "identity-second",
      400,
      command.orderGuid,
    );
    const store = createStore(connection);
    const prepared = await store.prepareOrLoad(command);

    await assert.rejects(
      store.prepareOrLoad({
        ...command,
        reason: "CARD_FAILURE_AUTO_RELEASE",
      }),
      /different immutable content/,
    );
    await assert.rejects(
      store.prepareOrLoad({
        ...command,
        reason: "INVALID" as "SALE",
      }),
      /reason/i,
    );
    await assert.rejects(
      store.markSubmitted({
        ...prepared,
        amount: createAud(601),
      }),
      /different immutable content|record/i,
    );

    await assert.rejects(
      store.prepareOrLoad(second),
      /unresolved voucher tender reversal/i,
    );

    await assert.rejects(
      connection.run(
        "UPDATE local_orders SET state = 'PendingSync' WHERE order_guid = ?",
        [command.orderGuid],
      ),
      /VOUCHER_TENDER_REVERSAL_ORDER_UNRESOLVED/,
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO order_tenders (
          tender_guid, order_guid, method, amount_cents,
          payment_attempt_id, created_at_iso
        ) VALUES ('blocked-positive-tender', ?, 'cash', 1, NULL, ?)`,
        [command.orderGuid, T1],
      ),
      /VOUCHER_TENDER_REVERSAL_ORDER_UNRESOLVED/,
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO payment_action_bindings (
          order_guid, action_id, request_signature,
          attempt_id, idempotency_key, created_at_iso
        ) VALUES (?, 'blocked-binding', '[]', ?, ?, ?)`,
        [
          command.orderGuid,
          command.expectedSourceAttemptId,
          command.expectedSourceAttemptId.replace(
            "voucher-attempt-",
            "voucher-idempotency-",
          ),
          T1,
        ],
      ),
      /VOUCHER_TENDER_REVERSAL_ORDER_UNRESOLVED/,
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO payment_attempts (
          attempt_id, idempotency_key, order_guid, provider, operation,
          amount_cents, state, created_at_iso, updated_at_iso
        ) VALUES (
          'blocked-attempt', 'blocked-idempotency', ?,
          'voucher', 'purchase', 1, 'Created', ?, ?
        )`,
        [command.orderGuid, T1, T1],
      ),
      /VOUCHER_TENDER_REVERSAL_ORDER_UNRESOLVED/,
    );
  });
});

test("Blocked 写终态 audit 但不写负 tender；终态 action/source/attempt/audit 禁止改删", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(connection, "blocked", 700);
    const second = await seedApprovedVoucherPurchase(
      connection,
      "blocked-second",
      300,
      command.orderGuid,
    );
    const store = createStore(connection);
    const prepared = await store.prepareOrLoad(command);
    const blocked = await store.markBlocked(
      prepared,
      "VOUCHER_RELEASE_REJECTED",
    );
    assert.equal(blocked.state, "Blocked");
    assert.equal(blocked.reversalTenderGuid, null);
    assert.equal(
      await negativeTenderCount(connection, command.orderGuid),
      0,
    );
    assert.deepEqual(
      await store.markBlocked(prepared, "VOUCHER_RELEASE_REJECTED"),
      blocked,
    );
    await assert.rejects(
      store.markBlocked(prepared, "DIFFERENT_CODE"),
      /different terminal fact/,
    );
    await assert.rejects(
      store.prepareOrLoad(second),
      /unresolved voucher tender reversal/i,
    );

    await assert.rejects(
      connection.run(
        `UPDATE voucher_tender_reversal_actions
         SET reason = 'changed'
         WHERE action_id = ?`,
        [command.actionId],
      ),
      /VOUCHER_TENDER_REVERSAL_IDENTITY_IMMUTABLE/,
    );
    await assert.rejects(
      connection.run(
        "DELETE FROM order_tenders WHERE tender_guid = ?",
        [command.sourceTenderGuid],
      ),
      /VOUCHER_TENDER_REVERSAL_TENDER_(DELETE_FORBIDDEN|IMMUTABLE)|FOREIGN KEY/,
    );
    await assert.rejects(
      connection.run(
        "UPDATE payment_attempts SET state = 'Cancelled' WHERE attempt_id = ?",
        [command.expectedSourceAttemptId],
      ),
      /VOUCHER_TENDER_REVERSAL_ATTEMPT_IMMUTABLE/,
    );
    await assert.rejects(
      connection.run(
        "DELETE FROM audit_events WHERE correlation_id = ?",
        [command.actionId],
      ),
      /VOUCHER_TENDER_REVERSAL_AUDIT_(DELETE_FORBIDDEN|IMMUTABLE)|FOREIGN KEY/,
    );
    await assert.rejects(
      connection.run(
        `UPDATE voucher_protected_attempt_states
         SET state_ciphertext = X'01'
         WHERE attempt_id = ?`,
        [command.expectedSourceAttemptId],
      ),
      /VOUCHER_TENDER_REVERSAL_PROTECTED_STATE_IMMUTABLE/,
    );
    await assert.rejects(
      connection.run(
        "DELETE FROM voucher_protected_attempt_states WHERE attempt_id = ?",
        [command.expectedSourceAttemptId],
      ),
      /VOUCHER_(TENDER_REVERSAL_PROTECTED_STATE_DELETE_FORBIDDEN|PROTECTED_STATE_DELETE_FORBIDDEN)/,
    );
  });
});

test("commitReleased 最终事务任一步失败会回滚 tender/link/audit/action，随后可安全重放", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const command = await seedApprovedVoucherPurchase(connection, "rollback", 800);
    await saveReleasedProtectedState(connection, command);
    const store = createStore(connection);
    const submitted = await store.markSubmitted(
      await store.prepareOrLoad(command),
    );
    await connection.exec(`
      CREATE TRIGGER test_fail_voucher_reversal_final_update
      BEFORE UPDATE OF state ON voucher_tender_reversal_actions
      FOR EACH ROW
      WHEN NEW.state = 'Reversed'
      BEGIN
        SELECT RAISE(ABORT, 'TEST_FINAL_VOUCHER_REVERSAL_FAILURE');
      END;
    `);

    await assert.rejects(
      store.commitReleased(submitted, {
        state: "Cancelled",
        responseCode: "VOUCHER_RELEASED",
      }),
      /TEST_FINAL_VOUCHER_REVERSAL_FAILURE/,
    );
    assert.equal(
      await negativeTenderCount(connection, command.orderGuid),
      0,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM payment_tender_reversal_links WHERE order_guid = ?",
        [command.orderGuid],
      ),
      0,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM audit_events WHERE correlation_id = ?",
        [command.actionId],
      ),
      0,
    );
    assert.equal(
      (
        await connection.getFirst<{ state: unknown }>(
          "SELECT state FROM voucher_tender_reversal_actions WHERE action_id = ?",
          [command.actionId],
        )
      )?.state,
      "Submitted",
    );

    await connection.exec(
      "DROP TRIGGER test_fail_voucher_reversal_final_update;",
    );
    const reversed = await store.commitReleased(submitted, {
      state: "Cancelled",
      responseCode: "VOUCHER_RELEASED",
    });
    assert.equal(reversed.state, "Reversed");
    assert.equal(
      await negativeTenderCount(connection, command.orderGuid),
      1,
    );
  });
});

async function seedApprovedVoucherPurchase(
  connection: SqliteConnectionPort,
  suffix: string,
  amountCents: number,
  existingOrderGuid?: string,
): Promise<SeededVoucherTenderReversalCommand> {
  const orderGuid = existingOrderGuid ?? `order-${suffix}`;
  if (!existingOrderGuid) {
    await insertOrder(connection, orderGuid, sequenceFor(suffix), amountCents);
  }
  const sourceAttemptId = `voucher-attempt-${suffix}`;
  const sourceTenderGuid = `voucher-tender-${suffix}`;
  await connection.run(
    `INSERT INTO payment_attempts (
      attempt_id, idempotency_key, order_guid, provider, operation,
      amount_cents, state, checkout_id, payment_id, session_id,
      txn_ref, rfn, provider_payload_ciphertext, provider_receipt_ciphertext,
      provider_response_code, created_at_iso, updated_at_iso, last_error_code
    ) VALUES (?, ?, ?, 'voucher', 'purchase', ?, 'Approved',
      NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'APPROVED', ?, ?, NULL)`,
    [
      sourceAttemptId,
      `voucher-idempotency-${suffix}`,
      orderGuid,
      amountCents,
      T0,
      T0,
    ],
  );
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents,
      payment_attempt_id, created_at_iso
    ) VALUES (?, ?, 'voucher', ?, ?, ?)`,
    [sourceTenderGuid, orderGuid, amountCents, sourceAttemptId, T0],
  );
  const command: SeededVoucherTenderReversalCommand = {
    actionId: `voucher-reversal-action-${suffix}`,
    orderGuid,
    sourceTenderGuid,
    reason: "SALE",
    expectedSourceAttemptId: sourceAttemptId,
    expectedAmountCents: amountCents,
  };
  await saveApprovedProtectedState(connection, command);
  return command;
}

async function saveApprovedProtectedState(
  connection: SqliteConnectionPort,
  command: SeededVoucherTenderReversalCommand,
): Promise<void> {
  const tokens = protectedTokenStore(connection, command);
  await tokens.save({
    ...protectedStateBase(command),
    phase: "approved",
  });
}

async function saveReleasedProtectedState(
  connection: SqliteConnectionPort,
  command: SeededVoucherTenderReversalCommand,
): Promise<void> {
  const tokens = protectedTokenStore(connection, command);
  const base = protectedStateBase(command);
  await tokens.save({ ...base, phase: "approved" });
  await tokens.save({ ...base, phase: "release-submitted" });
  await tokens.save({ ...base, phase: "released" });
}

function protectedStateBase(
  command: SeededVoucherTenderReversalCommand,
) {
  return {
    attemptId: command.expectedSourceAttemptId,
    idempotencyKey: command.expectedSourceAttemptId.replace(
      "voucher-attempt-",
      "voucher-idempotency-",
    ),
    orderGuid: command.orderGuid,
    operation: "purchase" as const,
    storeCode: "STORE-1",
    cashierId: "cashier-1",
    voucherCode: `VOUCHER-${command.actionId.toUpperCase()}`,
    reservationToken: `reservation-${command.actionId}`,
    amountCents: command.expectedAmountCents,
    expiresAtIso: "2026-07-29T00:00:00.000Z",
    reason: null,
  };
}

function protectedTokenStore(
  connection: SqliteConnectionPort,
  command: SeededVoucherTenderReversalCommand,
): SqliteVoucherProtectedTokenStore {
  return new SqliteVoucherProtectedTokenStore(
    connection,
    encryptor,
    () => `vpr_${command.expectedSourceAttemptId.replaceAll("-", "_")}`,
    () => T1,
  );
}

function createStore(
  connection: SqliteConnectionPort,
): SqliteVoucherTenderReversalStore {
  let tenderId = 0;
  let auditId = 0;
  return new SqliteVoucherTenderReversalStore(
    connection,
    encryptor,
    {
      createReversalTenderGuid: () => `voucher-reversal-${++tenderId}`,
      createAuditEventId: () => `voucher-reversal-audit-${++auditId}`,
    },
    () => T1,
  );
}

async function insertOrder(
  connection: SqliteConnectionPort,
  orderGuid: string,
  sequence: number,
  amountCents: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, ?, 'STORE-1', 'DEVICE-1', 'cashier-1', 'Cashier',
      ?, 'Completing', ?, 0, ?, NULL, ?, ?)`,
    [orderGuid, sequence, T0, amountCents, amountCents, T0, T0],
  );
  await connection.run(
    `INSERT INTO local_order_lines (
      line_id, order_guid, line_sequence, product_code, item_number,
      lookup_code, display_name, quantity, unit_price_cents,
      discount_cents, actual_amount_cents, price_source, line_kind,
      return_source_key, original_order_guid, original_order_detail_guid,
      reference_code, sync_price_source
    ) VALUES (?, ?, 1, 'P', NULL, 'P', 'Product', '1', ?, 0, ?,
      'catalog', 'sale', NULL, NULL, NULL, NULL, 0)`,
    [`line-${orderGuid}`, orderGuid, amountCents, amountCents],
  );
}

async function seedReturnAction(
  connection: SqliteConnectionPort,
  actionId: string,
  orderGuid: string,
  sequence: number,
): Promise<void> {
  await insertOrder(connection, orderGuid, 10_000 + sequence, -100);
  await connection.run(
    `INSERT INTO return_actions (
      action_id, request_fingerprint, return_order_guid,
      action_recovery_token, source_kind, total_refund_cents, online,
      store_code, device_code, cashier_id, cashier_name, session_epoch,
      supervisor_grant_id, plan_json, state, created_at_iso,
      completed_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, ?, 'receipt', 100, 1, 'STORE-1', 'DEVICE-1',
      'cashier-1', 'Cashier', ?, NULL, '{}', 'completed', ?, ?, ?)`,
    [
      actionId,
      `fingerprint-${actionId}`,
      orderGuid,
      `recovery-${actionId}`,
      `session-${actionId}`,
      T0,
      T1,
      T1,
    ],
  );
}

function sequenceFor(value: string): number {
  let hash = 0;
  for (const character of value) {
    hash = (hash * 31 + character.charCodeAt(0)) % 9_000;
  }
  return 1_000 + hash;
}

async function negativeTenderCount(
  connection: SqliteConnectionPort,
  orderGuid: string,
): Promise<number> {
  return scalar(
    connection,
    `SELECT COUNT(*) AS count
     FROM order_tenders
     WHERE order_guid = ? AND amount_cents < 0`,
    [orderGuid],
  );
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
  parameters: readonly SqlValue[] = [],
): Promise<number> {
  return Number(
    (
      await connection.getFirst<{ count: unknown }>(sql, parameters)
    )?.count,
  );
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
    return runStatement(this.database, sql, parameters);
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    return (
      this.database.prepare(sql).get(...parameters.map(toSqlInputValue)) as
        T | undefined
    ) ?? null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database.prepare(sql).all(
      ...parameters.map(toSqlInputValue),
    ) as unknown as readonly T[];
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

function runStatement(
  database: DatabaseSync,
  sql: string,
  parameters: readonly SqlValue[],
): SqlRunResult {
  const result = database.prepare(sql).run(
    ...parameters.map(toSqlInputValue),
  );
  return {
    changes: Number(result.changes),
    lastInsertRowId: Number(result.lastInsertRowid),
  };
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}
