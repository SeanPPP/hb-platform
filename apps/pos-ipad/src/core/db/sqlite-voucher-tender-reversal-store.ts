import {
  createAud,
  type LocalOrder,
  type Money,
  type OrderTender,
} from "../contracts";

import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import { SqliteVoucherProtectedTokenStore } from "./sqlite-voucher-protected-token-store";
import type { SqliteConnectionPort } from "./types";

import type {
  MixedPaymentOrderTruth,
  MixedTenderReversalLink,
} from "@/features/payments/mixed";

export type VoucherTenderReversalReason =
  | "SALE"
  | "CARD_FAILURE_AUTO_RELEASE";

export type VoucherTenderReversalState =
  | "Prepared"
  | "Submitted"
  | "Unknown"
  | "Reversed"
  | "Blocked";

export type VoucherTenderReversalCommand = Readonly<{
  actionId: string;
  orderGuid: string;
  sourceTenderGuid: string;
  reason: VoucherTenderReversalReason;
}>;

export type VoucherTenderReversalRecord = Readonly<{
  actionId: string;
  orderGuid: string;
  sourceTenderGuid: string;
  sourceAttemptId: string;
  amount: Money;
  reason: VoucherTenderReversalReason;
  state: VoucherTenderReversalState;
  attemptCount: number;
  lastErrorCode: string | null;
  reversalTenderGuid: string | null;
  truth: MixedPaymentOrderTruth;
}>;

export type VoucherTenderReversalPersistenceIds = Readonly<{
  createReversalTenderGuid(): string;
  createAuditEventId(): string;
}>;

export type VoucherReleaseProof = Readonly<{
  state: "Cancelled";
  responseCode: "VOUCHER_RELEASED";
}>;

export type VoucherTenderReversalRecoveryScope = Readonly<{
  storeCode: string;
  deviceCode: string;
}>;

export interface VoucherTenderReversalStorePort {
  prepareOrLoad(
    command: VoucherTenderReversalCommand,
  ): Promise<VoucherTenderReversalRecord>;
  markSubmitted(
    record: VoucherTenderReversalRecord,
  ): Promise<VoucherTenderReversalRecord>;
  markUnknown(
    record: VoucherTenderReversalRecord,
    errorCode: string,
  ): Promise<VoucherTenderReversalRecord>;
  markBlocked(
    record: VoucherTenderReversalRecord,
    errorCode: string,
  ): Promise<VoucherTenderReversalRecord>;
  commitReleased(
    record: VoucherTenderReversalRecord,
    proof: VoucherReleaseProof,
  ): Promise<VoucherTenderReversalRecord>;
}

export interface VoucherTenderReversalRecoveryStorePort {
  findBlocking(
    scope: VoucherTenderReversalRecoveryScope,
  ): Promise<VoucherTenderReversalRecord | null>;
}

type ActionRow = Readonly<{
  action_id: unknown;
  order_guid: unknown;
  source_tender_guid: unknown;
  source_attempt_id: unknown;
  amount_cents: unknown;
  reason: unknown;
  state: unknown;
  attempt_count: unknown;
  last_error_code: unknown;
  reversal_tender_guid: unknown;
}>;

type SourceRow = Readonly<{
  order_guid: unknown;
  order_state: unknown;
  store_code: unknown;
  cashier_id: unknown;
  tender_guid: unknown;
  tender_method: unknown;
  tender_amount_cents: unknown;
  payment_attempt_id: unknown;
  attempt_id: unknown;
  idempotency_key: unknown;
  attempt_order_guid: unknown;
  provider: unknown;
  operation: unknown;
  attempt_amount_cents: unknown;
  attempt_state: unknown;
  protected_attempt_id: unknown;
  protected_idempotency_key: unknown;
  protected_order_guid: unknown;
}>;

type TruthOrderRow = Readonly<{
  order_guid: unknown;
  state: unknown;
  actual_amount_cents: unknown;
}>;

type TruthTenderRow = Readonly<{
  tender_guid: unknown;
  method: unknown;
  amount_cents: unknown;
}>;

type TruthLinkRow = Readonly<{
  action_id: unknown;
  source_tender_guid: unknown;
  reversal_tender_guid: unknown;
}>;

/**
 * Voucher tender removal is an append-only local ledger. The provider release
 * is recorded separately in protected state; this store never reads or emits
 * the voucher code or reservation token.
 */
export class SqliteVoucherTenderReversalStore
implements
  VoucherTenderReversalStorePort,
  VoucherTenderReversalRecoveryStorePort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly ids: VoucherTenderReversalPersistenceIds,
    private readonly nowIso: () => string,
  ) {}

  public findBlocking(
    scopeInput: VoucherTenderReversalRecoveryScope,
  ): Promise<VoucherTenderReversalRecord | null> {
    const scope = {
      storeCode: strictId(
        scopeInput.storeCode,
        "voucher reversal recovery store",
      ),
      deviceCode: strictId(
        scopeInput.deviceCode,
        "voucher reversal recovery device",
      ),
    };
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const rows = await transaction.getAll<ActionRow>(
        `SELECT
          action.action_id, action.order_guid,
          action.source_tender_guid, action.source_attempt_id,
          action.amount_cents, action.reason, action.state,
          action.attempt_count, action.last_error_code,
          action.reversal_tender_guid
         FROM voucher_tender_reversal_actions action
         INNER JOIN local_orders order_row
           ON order_row.order_guid = action.order_guid
         WHERE order_row.store_code = ?
           AND order_row.device_code = ?
           AND action.state IN (
             'Prepared', 'Submitted', 'Unknown', 'Blocked'
           )
         ORDER BY order_row.local_sequence DESC,
           action.created_at_iso DESC, action.action_id DESC
         LIMIT 2`,
        [scope.storeCode, scope.deviceCode],
      );
      if (rows.length > 1) {
        throw new Error(
          "Multiple unresolved voucher tender reversals require support.",
        );
      }
      const row = rows[0];
      if (!row) return null;
      const orderGuid = text(
        row.order_guid,
        "voucher reversal recovery order",
      );
      const record = recordFromRow(
        row,
        await requireTruth(transaction, orderGuid),
      );
      const source = await readSource(
        transaction,
        record.orderGuid,
        record.sourceTenderGuid,
      );
      const sourceAttemptId = assertPreparedSource(source, {
        actionId: record.actionId,
        orderGuid: record.orderGuid,
        sourceTenderGuid: record.sourceTenderGuid,
        reason: record.reason,
      });
      if (
        sourceAttemptId !== record.sourceAttemptId ||
        integer(source.tender_amount_cents, "voucher reversal amount") !==
          record.amount.cents
      ) {
        throw new Error("Voucher reversal recovery binding is invalid.");
      }
      return record;
    });
  }

  public async prepareOrLoad(
    commandInput: VoucherTenderReversalCommand,
  ): Promise<VoucherTenderReversalRecord> {
    const command = normalizeCommand(commandInput);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const existing = await readAction(transaction, command.actionId);
      if (existing) {
        assertCommandMatches(existing, command);
        return recordFromRow(
          existing,
          await requireTruth(transaction, command.orderGuid),
        );
      }

      const unresolved = await transaction.getFirst<ActionRow>(
        `SELECT action_id, order_guid, source_tender_guid, source_attempt_id,
          amount_cents, reason, state, attempt_count, last_error_code,
          reversal_tender_guid
         FROM voucher_tender_reversal_actions
         WHERE order_guid = ?
           AND state IN ('Prepared', 'Submitted', 'Unknown', 'Blocked')
         LIMIT 1`,
        [command.orderGuid],
      );
      if (unresolved) {
        throw new Error(
          "The order already has an unresolved voucher tender reversal.",
        );
      }

      const source = await readSource(
        transaction,
        command.orderGuid,
        command.sourceTenderGuid,
      );
      const sourceAttemptId = assertPreparedSource(source, command);
      const amountCents = integer(
        source.tender_amount_cents,
        "voucher reversal amount",
      );
      const now = canonicalIso(this.nowIso(), "voucher reversal time");
      await transaction.run(
        `INSERT INTO voucher_tender_reversal_actions (
          action_id, order_guid, source_tender_guid, source_attempt_id,
          amount_cents, reason, state, attempt_count, last_error_code,
          reversal_tender_guid, terminal_audit_event_id, submitted_at_iso,
          terminal_at_iso, created_at_iso, updated_at_iso
        ) VALUES (
          ?, ?, ?, ?, ?, ?, 'Prepared', 0, NULL,
          NULL, NULL, NULL, NULL, ?, ?
        )`,
        [
          command.actionId,
          command.orderGuid,
          command.sourceTenderGuid,
          sourceAttemptId,
          amountCents,
          command.reason,
          now,
          now,
        ],
      );
      const inserted = await requireAction(transaction, command.actionId);
      assertCommandMatches(inserted, command);
      return recordFromRow(
        inserted,
        await requireTruth(transaction, command.orderGuid),
      );
    });
  }

  public async markSubmitted(
    recordInput: VoucherTenderReversalRecord,
  ): Promise<VoucherTenderReversalRecord> {
    const expected = normalizeRecord(recordInput);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const current = await requireAction(transaction, expected.actionId);
      assertImmutableRecord(current, expected);

      if (isSubmittedReplay(current, expected)) {
        return recordFromRow(
          current,
          await requireTruth(transaction, expected.orderGuid),
        );
      }
      if (
        current.state === "Reversed" ||
        current.state === "Blocked"
      ) {
        return recordFromRow(
          current,
          await requireTruth(transaction, expected.orderGuid),
        );
      }
      assertCurrentRecord(current, expected);
      if (
        current.state !== "Prepared" &&
        current.state !== "Submitted" &&
        current.state !== "Unknown"
      ) {
        throw new Error("Voucher reversal cannot be submitted from this state.");
      }
      const now = canonicalIso(this.nowIso(), "voucher submission time");
      const changed = await transaction.run(
        `UPDATE voucher_tender_reversal_actions
         SET state = 'Submitted',
             attempt_count = attempt_count + 1,
             last_error_code = NULL,
             submitted_at_iso = ?,
             updated_at_iso = ?
         WHERE action_id = ?
           AND state = ?
           AND attempt_count = ?
           AND last_error_code IS ?
           AND reversal_tender_guid IS NULL`,
        [
          now,
          now,
          expected.actionId,
          reversalState(current.state),
          integer(current.attempt_count, "voucher attempt count"),
          nullableText(current.last_error_code, "voucher error code"),
        ],
      );
      if (changed.changes !== 1) {
        throw new Error("Voucher reversal submission CAS failed.");
      }
      return recordFromRow(
        await requireAction(transaction, expected.actionId),
        await requireTruth(transaction, expected.orderGuid),
      );
    });
  }

  public async markUnknown(
    recordInput: VoucherTenderReversalRecord,
    errorCodeInput: string,
  ): Promise<VoucherTenderReversalRecord> {
    const expected = normalizeRecord(recordInput);
    const errorCode = strictErrorCode(errorCodeInput);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const current = await requireAction(transaction, expected.actionId);
      assertImmutableRecord(current, expected);
      if (current.state === "Unknown") {
        if (
          nullableText(current.last_error_code, "voucher error code") !==
          errorCode
        ) {
          throw new Error(
            "Voucher reversal was replayed with a different terminal fact.",
          );
        }
        return recordFromRow(
          current,
          await requireTruth(transaction, expected.orderGuid),
        );
      }
      assertCurrentRecord(current, expected);
      if (current.state !== "Submitted") {
        throw new Error("Only a submitted voucher reversal can be unknown.");
      }
      const now = canonicalIso(this.nowIso(), "voucher unknown time");
      const changed = await transaction.run(
        `UPDATE voucher_tender_reversal_actions
         SET state = 'Unknown', last_error_code = ?, updated_at_iso = ?
         WHERE action_id = ?
           AND state = 'Submitted'
           AND attempt_count = ?
           AND last_error_code IS NULL
           AND reversal_tender_guid IS NULL`,
        [errorCode, now, expected.actionId, expected.attemptCount],
      );
      if (changed.changes !== 1) {
        throw new Error("Voucher reversal unknown CAS failed.");
      }
      return recordFromRow(
        await requireAction(transaction, expected.actionId),
        await requireTruth(transaction, expected.orderGuid),
      );
    });
  }

  public async markBlocked(
    recordInput: VoucherTenderReversalRecord,
    errorCodeInput: string,
  ): Promise<VoucherTenderReversalRecord> {
    const expected = normalizeRecord(recordInput);
    const errorCode = strictErrorCode(errorCodeInput);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const current = await requireAction(transaction, expected.actionId);
      assertImmutableRecord(current, expected);
      if (current.state === "Blocked") {
        if (
          nullableText(current.last_error_code, "voucher error code") !==
          errorCode
        ) {
          throw new Error(
            "Voucher reversal was replayed with a different terminal fact.",
          );
        }
        return recordFromRow(
          current,
          await requireTruth(transaction, expected.orderGuid),
        );
      }
      if (current.state === "Reversed") {
        throw new Error(
          "Voucher reversal was replayed with a different terminal fact.",
        );
      }
      assertCurrentRecord(current, expected);
      const auditEventId = generatedId(
        this.ids.createAuditEventId(),
        "voucher reversal audit id",
      );
      const now = canonicalIso(this.nowIso(), "voucher blocked time");
      await transaction.run(
        `INSERT INTO audit_events (
          event_id, event_type, occurred_at_iso, order_guid,
          correlation_id, payload_json, uploaded_at_iso
        ) VALUES (?, 'PAYMENT_TENDER_REMOVE', ?, ?, ?, ?, NULL)`,
        [
          auditEventId,
          now,
          expected.orderGuid,
          expected.actionId,
          safeJson({
            action: "payment-tender-remove",
            outcome: "blocked",
            reason: expected.reason,
            amountCents: expected.amount.cents,
            sourceTenderGuid: expected.sourceTenderGuid,
            sourceAttemptId: expected.sourceAttemptId,
            errorCode,
          }),
        ],
      );
      const changed = await transaction.run(
        `UPDATE voucher_tender_reversal_actions
         SET state = 'Blocked',
             last_error_code = ?,
             terminal_audit_event_id = ?,
             terminal_at_iso = ?,
             updated_at_iso = ?
         WHERE action_id = ?
           AND state = ?
           AND attempt_count = ?
           AND last_error_code IS ?
           AND reversal_tender_guid IS NULL`,
        [
          errorCode,
          auditEventId,
          now,
          now,
          expected.actionId,
          reversalState(current.state),
          expected.attemptCount,
          expected.lastErrorCode,
        ],
      );
      if (changed.changes !== 1) {
        throw new Error("Voucher reversal blocked CAS failed.");
      }
      return recordFromRow(
        await requireAction(transaction, expected.actionId),
        await requireTruth(transaction, expected.orderGuid),
      );
    });
  }

  public async commitReleased(
    recordInput: VoucherTenderReversalRecord,
    proof: VoucherReleaseProof,
  ): Promise<VoucherTenderReversalRecord> {
    const expected = normalizeRecord(recordInput);
    if (
      proof?.state !== "Cancelled" ||
      proof.responseCode !== "VOUCHER_RELEASED"
    ) {
      return Promise.reject(new TypeError("Voucher release proof is invalid."));
    }
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const current = await requireAction(transaction, expected.actionId);
      assertImmutableRecord(current, expected);
      if (current.state === "Reversed") {
        const replay = recordFromRow(
          current,
          await requireTruth(transaction, expected.orderGuid),
        );
        assertReversedTruth(replay);
        await this.assertReleasedProtectedState(transaction, replay);
        return replay;
      }
      if (current.state === "Blocked") {
        throw new Error(
          "Voucher reversal was replayed with a different terminal fact.",
        );
      }
      assertCurrentRecord(current, expected);
      if (current.state !== "Submitted" && current.state !== "Unknown") {
        throw new Error("Voucher reversal has no submitted release to commit.");
      }
      const currentRecord = recordFromRow(
        current,
        await requireTruth(transaction, expected.orderGuid),
      );
      await this.assertReleasedProtectedState(transaction, currentRecord);

      const reversalTenderGuid = generatedId(
        this.ids.createReversalTenderGuid(),
        "voucher reversal tender id",
      );
      const auditEventId = generatedId(
        this.ids.createAuditEventId(),
        "voucher reversal audit id",
      );
      const now = canonicalIso(this.nowIso(), "voucher reversal commit time");
      await transaction.run(
        `INSERT INTO order_tenders (
          tender_guid, order_guid, method, amount_cents,
          payment_attempt_id, created_at_iso
        ) VALUES (?, ?, 'voucher', ?, NULL, ?)`,
        [
          reversalTenderGuid,
          expected.orderGuid,
          -expected.amount.cents,
          now,
        ],
      );
      await transaction.run(
        `INSERT INTO payment_tender_reversal_links (
          order_guid, action_id, source_tender_guid,
          reversal_tender_guid, created_at_iso
        ) VALUES (?, ?, ?, ?, ?)`,
        [
          expected.orderGuid,
          expected.actionId,
          expected.sourceTenderGuid,
          reversalTenderGuid,
          now,
        ],
      );
      await transaction.run(
        `INSERT INTO audit_events (
          event_id, event_type, occurred_at_iso, order_guid,
          correlation_id, payload_json, uploaded_at_iso
        ) VALUES (?, 'PAYMENT_TENDER_REMOVE', ?, ?, ?, ?, NULL)`,
        [
          auditEventId,
          now,
          expected.orderGuid,
          expected.actionId,
          safeJson({
            action: "payment-tender-remove",
            outcome: "success",
            reason: expected.reason,
            amountCents: expected.amount.cents,
            sourceTenderGuid: expected.sourceTenderGuid,
            sourceAttemptId: expected.sourceAttemptId,
            reversalTenderGuid,
          }),
        ],
      );
      const changed = await transaction.run(
        `UPDATE voucher_tender_reversal_actions
         SET state = 'Reversed',
             last_error_code = NULL,
             reversal_tender_guid = ?,
             terminal_audit_event_id = ?,
             terminal_at_iso = ?,
             updated_at_iso = ?
         WHERE action_id = ?
           AND state = ?
           AND attempt_count = ?
           AND last_error_code IS ?
           AND reversal_tender_guid IS NULL`,
        [
          reversalTenderGuid,
          auditEventId,
          now,
          now,
          expected.actionId,
          current.state,
          expected.attemptCount,
          expected.lastErrorCode,
        ],
      );
      if (changed.changes !== 1) {
        throw new Error("Voucher reversal completion CAS failed.");
      }
      const completed = recordFromRow(
        await requireAction(transaction, expected.actionId),
        await requireTruth(transaction, expected.orderGuid),
      );
      assertReversedTruth(completed);
      return completed;
    });
  }

  private async assertReleasedProtectedState(
    transaction: SqliteConnectionPort,
    record: VoucherTenderReversalRecord,
  ): Promise<void> {
    const source = await readSource(
      transaction,
      record.orderGuid,
      record.sourceTenderGuid,
    );
    const sourceAttemptId = assertPreparedSource(source, {
      actionId: record.actionId,
      orderGuid: record.orderGuid,
      sourceTenderGuid: record.sourceTenderGuid,
      reason: record.reason,
    });
    if (
      sourceAttemptId !== record.sourceAttemptId ||
      integer(source.tender_amount_cents, "voucher reversal amount") !==
        record.amount.cents
    ) {
      throw new Error("Voucher release source binding changed.");
    }
    const tokens = new SqliteVoucherProtectedTokenStore(
      transaction,
      this.encryptor,
      () => {
        throw new Error("Read-only voucher release verification.");
      },
      this.nowIso,
    );
    const state = await tokens.getByAttempt(record.sourceAttemptId);
    if (
      !state ||
      state.attemptId !== record.sourceAttemptId ||
      state.orderGuid !== record.orderGuid ||
      state.operation !== "purchase" ||
      state.phase !== "released" ||
      state.storeCode !== text(source.store_code, "voucher store code") ||
      state.cashierId !== text(source.cashier_id, "voucher cashier id") ||
      state.amountCents !== record.amount.cents ||
      !state.voucherCode ||
      !state.reservationToken
    ) {
      throw new Error("Voucher released protected state is invalid.");
    }
  }
}

async function readAction(
  connection: SqliteConnectionPort,
  actionId: string,
): Promise<ActionRow | null> {
  return connection.getFirst<ActionRow>(
    `SELECT action_id, order_guid, source_tender_guid, source_attempt_id,
      amount_cents, reason, state, attempt_count, last_error_code,
      reversal_tender_guid
     FROM voucher_tender_reversal_actions
     WHERE action_id = ?`,
    [actionId],
  );
}

async function requireAction(
  connection: SqliteConnectionPort,
  actionId: string,
): Promise<ActionRow> {
  const row = await readAction(connection, actionId);
  if (!row) throw new Error("Voucher tender reversal action was not found.");
  return row;
}

async function readSource(
  connection: SqliteConnectionPort,
  orderGuid: string,
  sourceTenderGuid: string,
): Promise<SourceRow> {
  const row = await connection.getFirst<SourceRow>(
    `SELECT
      order_row.order_guid,
      order_row.state AS order_state,
      order_row.store_code,
      order_row.cashier_id,
      source.tender_guid,
      source.method AS tender_method,
      source.amount_cents AS tender_amount_cents,
      source.payment_attempt_id,
      attempt.attempt_id,
      attempt.idempotency_key,
      attempt.order_guid AS attempt_order_guid,
      attempt.provider,
      attempt.operation,
      attempt.amount_cents AS attempt_amount_cents,
      attempt.state AS attempt_state,
      protected.attempt_id AS protected_attempt_id,
      protected.idempotency_key AS protected_idempotency_key,
      protected.order_guid AS protected_order_guid
     FROM local_orders order_row
     INNER JOIN order_tenders source
       ON source.order_guid = order_row.order_guid
     INNER JOIN payment_attempts attempt
       ON attempt.attempt_id = source.payment_attempt_id
     INNER JOIN voucher_protected_attempt_states protected
       ON protected.attempt_id = attempt.attempt_id
     WHERE order_row.order_guid = ?
       AND source.tender_guid = ?`,
    [orderGuid, sourceTenderGuid],
  );
  if (!row) throw new Error("Voucher reversal source was not found.");
  return row;
}

function assertPreparedSource(
  row: SourceRow,
  command: VoucherTenderReversalCommand,
): string {
  const orderGuid = text(row.order_guid, "voucher source order");
  const sourceTenderGuid = text(row.tender_guid, "voucher source tender");
  const amountCents = integer(
    row.tender_amount_cents,
    "voucher source amount",
  );
  const attemptId = text(row.attempt_id, "voucher source attempt");
  const idempotencyKey = text(
    row.idempotency_key,
    "voucher source idempotency key",
  );
  if (
    orderGuid !== command.orderGuid ||
    row.order_state !== "Completing" ||
    sourceTenderGuid !== command.sourceTenderGuid ||
    row.tender_method !== "voucher" ||
    amountCents <= 0 ||
    text(row.payment_attempt_id, "voucher tender attempt") !== attemptId ||
    text(row.attempt_order_guid, "voucher attempt order") !== orderGuid ||
    row.provider !== "voucher" ||
    row.operation !== "purchase" ||
    integer(row.attempt_amount_cents, "voucher attempt amount") !==
      amountCents ||
    row.attempt_state !== "Approved" ||
    text(row.protected_attempt_id, "voucher protected attempt") !==
      attemptId ||
    text(
      row.protected_idempotency_key,
      "voucher protected idempotency key",
    ) !== idempotencyKey ||
    text(row.protected_order_guid, "voucher protected order") !== orderGuid
  ) {
    throw new Error("Voucher reversal source binding is invalid.");
  }
  return attemptId;
}

async function requireTruth(
  connection: SqliteConnectionPort,
  orderGuid: string,
): Promise<MixedPaymentOrderTruth> {
  const order = await connection.getFirst<TruthOrderRow>(
    `SELECT order_guid, state, actual_amount_cents
     FROM local_orders
     WHERE order_guid = ?`,
    [orderGuid],
  );
  if (!order) throw new Error("Voucher reversal order was not found.");
  const tenders = await connection.getAll<TruthTenderRow>(
    `SELECT tender_guid, method, amount_cents
     FROM order_tenders
     WHERE order_guid = ?
     ORDER BY created_at_iso ASC, tender_guid ASC`,
    [orderGuid],
  );
  const links = await connection.getAll<TruthLinkRow>(
    `SELECT action_id, source_tender_guid, reversal_tender_guid
     FROM payment_tender_reversal_links
     WHERE order_guid = ?
     ORDER BY created_at_iso ASC, action_id ASC`,
    [orderGuid],
  );
  const truth: MixedPaymentOrderTruth = {
    orderGuid: text(order.order_guid, "voucher truth order"),
    state: orderState(order.state),
    actualAmount: createAud(
      integer(order.actual_amount_cents, "voucher truth actual amount"),
    ),
    tenders: Object.freeze(tenders.map(mapTender)),
    reversalLinks: Object.freeze(links.map(mapLink)),
  };
  return Object.freeze({
    ...truth,
    actualAmount: Object.freeze({ ...truth.actualAmount }),
  });
}

function mapTender(row: TruthTenderRow): OrderTender {
  return Object.freeze({
    tenderGuid: text(row.tender_guid, "voucher truth tender"),
    method: tenderMethod(row.method),
    amount: Object.freeze(
      createAud(integer(row.amount_cents, "voucher truth tender amount")),
    ),
    reference: null,
    reservationToken: null,
  });
}

function mapLink(row: TruthLinkRow): MixedTenderReversalLink {
  return Object.freeze({
    actionId: text(row.action_id, "voucher reversal link action"),
    sourceTenderGuid: text(
      row.source_tender_guid,
      "voucher reversal link source",
    ),
    reversalTenderGuid: text(
      row.reversal_tender_guid,
      "voucher reversal link tender",
    ),
  });
}

function recordFromRow(
  row: ActionRow,
  truth: MixedPaymentOrderTruth,
): VoucherTenderReversalRecord {
  return Object.freeze({
    actionId: text(row.action_id, "voucher reversal action"),
    orderGuid: text(row.order_guid, "voucher reversal order"),
    sourceTenderGuid: text(
      row.source_tender_guid,
      "voucher reversal source tender",
    ),
    sourceAttemptId: text(
      row.source_attempt_id,
      "voucher reversal source attempt",
    ),
    amount: Object.freeze(
      createAud(integer(row.amount_cents, "voucher reversal amount")),
    ),
    reason: reversalReason(row.reason),
    state: reversalState(row.state),
    attemptCount: nonNegativeInteger(
      row.attempt_count,
      "voucher reversal attempt count",
    ),
    lastErrorCode: nullableText(
      row.last_error_code,
      "voucher reversal error code",
    ),
    reversalTenderGuid: nullableText(
      row.reversal_tender_guid,
      "voucher reversal tender",
    ),
    truth,
  });
}

function normalizeCommand(
  command: VoucherTenderReversalCommand,
): VoucherTenderReversalCommand {
  return Object.freeze({
    actionId: strictId(command.actionId, "voucher reversal action"),
    orderGuid: strictId(command.orderGuid, "voucher reversal order"),
    sourceTenderGuid: strictId(
      command.sourceTenderGuid,
      "voucher reversal source tender",
    ),
    reason: reversalReason(command.reason),
  });
}

function normalizeRecord(
  record: VoucherTenderReversalRecord,
): VoucherTenderReversalRecord {
  if (!record || typeof record !== "object") {
    throw new TypeError("Voucher reversal record is required.");
  }
  const normalized = Object.freeze({
    actionId: strictId(record.actionId, "voucher reversal action"),
    orderGuid: strictId(record.orderGuid, "voucher reversal order"),
    sourceTenderGuid: strictId(
      record.sourceTenderGuid,
      "voucher reversal source tender",
    ),
    sourceAttemptId: strictId(
      record.sourceAttemptId,
      "voucher reversal source attempt",
    ),
    amount: Object.freeze(createAud(positiveAud(record.amount))),
    reason: reversalReason(record.reason),
    state: reversalState(record.state),
    attemptCount: nonNegativeInteger(
      record.attemptCount,
      "voucher reversal attempt count",
    ),
    lastErrorCode:
      record.lastErrorCode === null
        ? null
        : strictErrorCode(record.lastErrorCode),
    reversalTenderGuid:
      record.reversalTenderGuid === null
        ? null
        : strictId(
          record.reversalTenderGuid,
          "voucher reversal tender",
        ),
    truth: record.truth,
  });
  return normalized;
}

function assertCommandMatches(
  row: ActionRow,
  command: VoucherTenderReversalCommand,
): void {
  if (
    text(row.action_id, "voucher reversal action") !== command.actionId ||
    text(row.order_guid, "voucher reversal order") !== command.orderGuid ||
    text(row.source_tender_guid, "voucher reversal source tender") !==
      command.sourceTenderGuid ||
    reversalReason(row.reason) !== command.reason
  ) {
    throw new Error(
      "Voucher reversal action was replayed with different immutable content.",
    );
  }
}

function assertImmutableRecord(
  row: ActionRow,
  record: VoucherTenderReversalRecord,
): void {
  if (
    text(row.action_id, "voucher reversal action") !== record.actionId ||
    text(row.order_guid, "voucher reversal order") !== record.orderGuid ||
    text(row.source_tender_guid, "voucher reversal source tender") !==
      record.sourceTenderGuid ||
    text(row.source_attempt_id, "voucher reversal source attempt") !==
      record.sourceAttemptId ||
    integer(row.amount_cents, "voucher reversal amount") !==
      record.amount.cents ||
    reversalReason(row.reason) !== record.reason
  ) {
    throw new Error(
      "Voucher reversal record has different immutable content.",
    );
  }
}

function assertCurrentRecord(
  row: ActionRow,
  record: VoucherTenderReversalRecord,
): void {
  if (
    reversalState(row.state) !== record.state ||
    nonNegativeInteger(row.attempt_count, "voucher reversal attempt count") !==
      record.attemptCount ||
    nullableText(row.last_error_code, "voucher reversal error code") !==
      record.lastErrorCode ||
    nullableText(row.reversal_tender_guid, "voucher reversal tender") !==
      record.reversalTenderGuid
  ) {
    throw new Error("Voucher reversal record no longer matches persisted state.");
  }
}

function isSubmittedReplay(
  row: ActionRow,
  record: VoucherTenderReversalRecord,
): boolean {
  const state = reversalState(row.state);
  const attemptCount = nonNegativeInteger(
    row.attempt_count,
    "voucher reversal attempt count",
  );
  return state === "Submitted" &&
    (record.state === "Prepared" || record.state === "Unknown") &&
    attemptCount === record.attemptCount + 1;
}

function assertReversedTruth(record: VoucherTenderReversalRecord): void {
  if (
    record.state !== "Reversed" ||
    !record.reversalTenderGuid ||
    !record.truth.reversalLinks.some(
      (link) =>
        link.actionId === record.actionId &&
        link.sourceTenderGuid === record.sourceTenderGuid &&
        link.reversalTenderGuid === record.reversalTenderGuid,
    )
  ) {
    throw new Error("Voucher reversal terminal truth is invalid.");
  }
  const source = record.truth.tenders.find(
    (tender) => tender.tenderGuid === record.sourceTenderGuid,
  );
  const reversal = record.truth.tenders.find(
    (tender) => tender.tenderGuid === record.reversalTenderGuid,
  );
  if (
    !source ||
    source.method !== "voucher" ||
    source.amount.cents !== record.amount.cents ||
    !reversal ||
    reversal.method !== "voucher" ||
    reversal.amount.cents !== -record.amount.cents
  ) {
    throw new Error("Voucher reversal terminal tender truth is invalid.");
  }
}

function safeJson(value: Readonly<Record<string, unknown>>): string {
  const encoded = JSON.stringify(value);
  if (
    !encoded ||
    /voucherCode|reservationToken|protectedReference|authorization|pan/iu.test(
      encoded,
    )
  ) {
    throw new Error("Voucher reversal audit payload is unsafe.");
  }
  return encoded;
}

function strictId(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 128 ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}

function generatedId(value: unknown, label: string): string {
  return strictId(value, label);
}

function strictErrorCode(value: unknown): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 128 ||
    value.trim() !== value ||
    !/^[A-Z0-9_:-]+$/u.test(value)
  ) {
    throw new TypeError("Voucher reversal error code is invalid.");
  }
  return value;
}

function text(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error(`Invalid persisted ${label}.`);
  }
  return value;
}

function nullableText(value: unknown, label: string): string | null {
  return value === null || value === undefined
    ? null
    : text(value, label);
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error(`Invalid persisted ${label}.`);
  }
  return parsed;
}

function nonNegativeInteger(value: unknown, label: string): number {
  const parsed = integer(value, label);
  if (parsed < 0) throw new Error(`Invalid persisted ${label}.`);
  return parsed;
}

function positiveAud(value: Money): number {
  if (
    !value ||
    value.currency !== "AUD" ||
    !Number.isSafeInteger(value.cents) ||
    value.cents <= 0
  ) {
    throw new TypeError("Voucher reversal amount must be positive AUD.");
  }
  return value.cents;
}

function reversalReason(value: unknown): VoucherTenderReversalReason {
  if (value === "SALE" || value === "CARD_FAILURE_AUTO_RELEASE") return value;
  throw new TypeError("Voucher reversal reason is invalid.");
}

function reversalState(value: unknown): VoucherTenderReversalState {
  if (
    value === "Prepared" ||
    value === "Submitted" ||
    value === "Unknown" ||
    value === "Reversed" ||
    value === "Blocked"
  ) {
    return value;
  }
  throw new Error("Invalid persisted voucher reversal state.");
}

function orderState(value: unknown): LocalOrder["state"] {
  if (
    value === "Draft" ||
    value === "Completing" ||
    value === "CompletedLocal" ||
    value === "PendingSync" ||
    value === "Syncing" ||
    value === "Synced" ||
    value === "Blocked403" ||
    value === "Rejected"
  ) {
    return value;
  }
  throw new Error("Invalid persisted voucher reversal order state.");
}

function tenderMethod(value: unknown): OrderTender["method"] {
  if (value === "cash" || value === "card" || value === "voucher") {
    return value;
  }
  throw new Error("Invalid persisted voucher reversal tender method.");
}

function canonicalIso(value: unknown, label: string): string {
  if (typeof value !== "string") {
    throw new TypeError(`${label} is invalid.`);
  }
  const date = new Date(value);
  if (Number.isNaN(date.valueOf()) || date.toISOString() !== value) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}
