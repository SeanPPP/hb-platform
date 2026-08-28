import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type {
  VoucherPreparationStorePort,
  VoucherPreparedAttemptBinding,
  VoucherPreparedContext,
  VoucherPreparedContextDraft,
} from "@/features/payments/runtime/voucher-preparation";

type PreparedContextRow = Readonly<{
  protected_reference: unknown;
  order_guid: unknown;
  action_id: unknown;
  operation: unknown;
  attempt_id: unknown;
  idempotency_key: unknown;
  context_ciphertext: unknown;
  created_at_iso: unknown;
  bound_at_iso: unknown;
}>;

type PreparedOrderRow = Readonly<{
  state: unknown;
  actual_amount_cents: unknown;
  store_code: unknown;
  cashier_id: unknown;
}>;

type PreparedAttemptJoinRow = PreparedContextRow &
  Readonly<{
    persisted_attempt_id: unknown;
    persisted_idempotency_key: unknown;
    persisted_order_guid: unknown;
    persisted_provider: unknown;
    persisted_operation: unknown;
  }>;

type PersistedPreparedContextV1 = Readonly<{
  version: 1;
  context: VoucherPreparedContextDraft;
}>;

/**
 * 页面先按 order/action 保存券码或退款原因，PaymentAttemptService 建立不可变
 * action binding 后再按该 binding 绑定 attempt。明文列不含券码或原因。
 */
export class SqliteVoucherPreparationStore
implements VoucherPreparationStorePort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly createProtectedReference: () => string,
    private readonly nowIso: () => string,
  ) {}

  public prepare(input: VoucherPreparedContextDraft): Promise<string> {
    const context = validatePreparedContext(input);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      await assertPreparedOrder(transaction, context);
      const existing = await transaction.getFirst<PreparedContextRow>(
        `SELECT protected_reference, order_guid, action_id, operation,
          attempt_id, idempotency_key, context_ciphertext,
          created_at_iso, bound_at_iso
         FROM voucher_prepared_contexts
         WHERE order_guid = ? AND action_id = ?`,
        [context.orderGuid, context.actionId],
      );
      if (existing) {
        const persisted = await decodeContext(this.encryptor, existing);
        if (!samePreparedContext(persisted, context)) {
          throw new Error(
            "Voucher preparation action was replayed with different protected content.",
          );
        }
        return opaqueReference(
          text(existing.protected_reference, "prepared reference"),
        );
      }

      const protectedReference = opaqueReference(
        this.createProtectedReference(),
      );
      const ciphertext = await encryptContext(this.encryptor, context);
      const now = canonicalIso(this.nowIso(), "voucher preparation time");
      await transaction.run(
        `INSERT INTO voucher_prepared_contexts (
          protected_reference, order_guid, action_id, operation,
          attempt_id, idempotency_key, context_ciphertext,
          created_at_iso, bound_at_iso
        ) VALUES (?, ?, ?, ?, NULL, NULL, ?, ?, NULL)`,
        [
          protectedReference,
          context.orderGuid,
          context.actionId,
          context.operation,
          ciphertext,
          now,
        ],
      );
      return protectedReference;
    });
  }

  public bindToAttempt(
    input: VoucherPreparedAttemptBinding,
  ): Promise<VoucherPreparedContext | null> {
    const binding = validateAttemptBinding(input);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const row = await transaction.getFirst<PreparedAttemptJoinRow>(
        `SELECT c.protected_reference, c.order_guid, c.action_id,
          c.operation, c.attempt_id, c.idempotency_key,
          c.context_ciphertext, c.created_at_iso, c.bound_at_iso,
          p.attempt_id AS persisted_attempt_id,
          p.idempotency_key AS persisted_idempotency_key,
          p.order_guid AS persisted_order_guid,
          p.provider AS persisted_provider,
          p.operation AS persisted_operation
         FROM payment_action_bindings b
         INNER JOIN voucher_prepared_contexts c
           ON c.order_guid = b.order_guid
          AND c.action_id = b.action_id
         INNER JOIN payment_attempts p
           ON p.attempt_id = b.attempt_id
          AND p.idempotency_key = b.idempotency_key
          AND p.order_guid = b.order_guid
         WHERE b.attempt_id = ?
           AND b.idempotency_key = ?
           AND b.order_guid = ?`,
        [
          binding.attemptId,
          binding.idempotencyKey,
          binding.orderGuid,
        ],
      );
      if (!row) return null;
      if (
        text(row.persisted_attempt_id, "prepared attempt id") !==
          binding.attemptId ||
        text(
          row.persisted_idempotency_key,
          "prepared idempotency key",
        ) !== binding.idempotencyKey ||
        text(row.persisted_order_guid, "prepared order guid") !==
          binding.orderGuid ||
        text(row.persisted_provider, "prepared provider") !== "voucher" ||
        text(row.persisted_operation, "prepared operation") !==
          binding.operation ||
        text(row.operation, "prepared context operation") !== binding.operation
      ) {
        throw new Error(
          "Voucher prepared context does not match the persisted attempt.",
        );
      }

      const currentAttemptId = nullableText(row.attempt_id);
      const currentIdempotencyKey = nullableText(row.idempotency_key);
      if (currentAttemptId || currentIdempotencyKey) {
        if (
          currentAttemptId !== binding.attemptId ||
          currentIdempotencyKey !== binding.idempotencyKey
        ) {
          throw new Error(
            "Voucher prepared context is already bound to another attempt.",
          );
        }
      } else {
        const now = canonicalIso(this.nowIso(), "voucher binding time");
        const changed = await transaction.run(
          `UPDATE voucher_prepared_contexts
           SET attempt_id = ?, idempotency_key = ?, bound_at_iso = ?
           WHERE protected_reference = ?
             AND order_guid = ?
             AND action_id = ?
             AND attempt_id IS NULL
             AND idempotency_key IS NULL
             AND bound_at_iso IS NULL`,
          [
            binding.attemptId,
            binding.idempotencyKey,
            now,
            text(row.protected_reference, "prepared reference"),
            binding.orderGuid,
            text(row.action_id, "prepared action id"),
          ],
        );
        if (changed.changes !== 1) {
          throw new Error("Voucher prepared context binding CAS failed.");
        }
      }
      const context = await decodeContext(this.encryptor, row);
      if (
        context.orderGuid !== binding.orderGuid ||
        context.operation !== binding.operation
      ) {
        throw new Error("Voucher prepared ciphertext binding is corrupted.");
      }
      return {
        ...context,
        protectedReference: opaqueReference(
          text(row.protected_reference, "prepared reference"),
        ),
        attemptId: binding.attemptId,
        idempotencyKey: binding.idempotencyKey,
      };
    });
  }
}

async function assertPreparedOrder(
  transaction: SqliteConnectionPort,
  context: VoucherPreparedContextDraft,
): Promise<void> {
  const order = await transaction.getFirst<PreparedOrderRow>(
    `SELECT state, actual_amount_cents, store_code, cashier_id
     FROM local_orders
     WHERE order_guid = ?`,
    [context.orderGuid],
  );
  const amountCents = integer(
    order?.actual_amount_cents,
    "prepared order amount",
  );
  if (
    !order ||
    (text(order.state, "prepared order state") !== "Draft" &&
      text(order.state, "prepared order state") !== "Completing") ||
    text(order.store_code, "prepared store code") !== context.storeCode ||
    text(order.cashier_id, "prepared cashier id") !== context.cashierId ||
    (context.operation === "purchase" && amountCents <= 0) ||
    (context.operation === "refund" && amountCents >= 0)
  ) {
    throw new Error(
      "Voucher preparation does not match an active persisted order.",
    );
  }
}

function validatePreparedContext(
  input: VoucherPreparedContextDraft,
): VoucherPreparedContextDraft {
  if (!input || typeof input !== "object") {
    throw new TypeError("Voucher prepared context is required.");
  }
  const operation =
    input.operation === "purchase" || input.operation === "refund"
      ? input.operation
      : invalid<never>("Voucher prepared operation is invalid.");
  const voucherCode = optionalSecret(
    input.voucherCode,
    "prepared voucher code",
    512,
  );
  const refundReason = optionalSecret(
    input.refundReason,
    "prepared refund reason",
    1024,
  );
  if (
    (operation === "purchase" &&
      (!voucherCode || refundReason !== null)) ||
    (operation === "refund" &&
      (!refundReason || voucherCode !== null))
  ) {
    throw new TypeError("Voucher prepared protected fields are invalid.");
  }
  return Object.freeze({
    actionId: strictId(input.actionId, "voucher preparation action id"),
    orderGuid: strictId(input.orderGuid, "voucher preparation order guid"),
    operation,
    storeCode: strictText(
      input.storeCode,
      "voucher preparation store code",
      64,
    ),
    cashierId: strictId(
      input.cashierId,
      "voucher preparation cashier id",
    ),
    voucherCode,
    refundReason,
  });
}

function validateAttemptBinding(
  input: VoucherPreparedAttemptBinding,
): VoucherPreparedAttemptBinding {
  if (!input || typeof input !== "object") {
    throw new TypeError("Voucher attempt binding is required.");
  }
  return {
    orderGuid: strictId(input.orderGuid, "voucher binding order guid"),
    operation:
      input.operation === "purchase" || input.operation === "refund"
        ? input.operation
        : invalid<never>("Voucher binding operation is invalid."),
    attemptId: strictId(input.attemptId, "voucher binding attempt id"),
    idempotencyKey: strictText(
      input.idempotencyKey,
      "voucher binding idempotency key",
      256,
    ),
  };
}

async function encryptContext(
  encryptor: SensitivePayloadEncryptor,
  context: VoucherPreparedContextDraft,
): Promise<Uint8Array> {
  const ciphertext = await encryptor.encrypt(
    JSON.stringify({
      version: 1,
      context,
    } satisfies PersistedPreparedContextV1),
  );
  if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
    throw new Error("Voucher preparation encryption failed.");
  }
  return ciphertext;
}

async function decodeContext(
  encryptor: SensitivePayloadEncryptor,
  row: PreparedContextRow,
): Promise<VoucherPreparedContextDraft> {
  const raw = await encryptor.decrypt(
    bytes(row.context_ciphertext, "voucher preparation ciphertext"),
  );
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error("Voucher preparation ciphertext is invalid.");
  }
  if (
    !parsed ||
    typeof parsed !== "object" ||
    (parsed as { version?: unknown }).version !== 1
  ) {
    throw new Error("Voucher preparation version is invalid.");
  }
  const context = validatePreparedContext(
    (parsed as { context?: VoucherPreparedContextDraft }).context as
      VoucherPreparedContextDraft,
  );
  if (
    context.orderGuid !== text(row.order_guid, "prepared order guid") ||
    context.actionId !== text(row.action_id, "prepared action id") ||
    context.operation !== text(row.operation, "prepared operation")
  ) {
    throw new Error("Voucher preparation plaintext binding is corrupted.");
  }
  return context;
}

function samePreparedContext(
  left: VoucherPreparedContextDraft,
  right: VoucherPreparedContextDraft,
): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function opaqueReference(value: string): string {
  const normalized = value.trim();
  if (
    !normalized.startsWith("vpc_") ||
    normalized.length < 20 ||
    normalized.length > 128 ||
    !/^[A-Za-z0-9_-]+$/u.test(normalized)
  ) {
    throw new TypeError("Voucher preparation reference is invalid.");
  }
  return normalized;
}

function strictId(value: string, label: string): string {
  return strictText(value, label, 128);
}

function strictText(value: string, label: string, max: number): string {
  if (typeof value !== "string") throw new TypeError(`${label} is invalid.`);
  const normalized = value.trim();
  if (
    !normalized ||
    normalized.length > max ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized;
}

function optionalSecret(
  value: string | null,
  label: string,
  max: number,
): string | null {
  return value === null ? null : strictText(value, label, max);
}

function canonicalIso(value: string, label: string): string {
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString() !== value) {
    throw new TypeError(`${label} must be canonical ISO UTC.`);
  }
  return value;
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return value;
}

function nullableText(value: unknown): string | null {
  return value === null || value === undefined
    ? null
    : text(value, "nullable text");
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return parsed;
}

function bytes(value: unknown, label: string): Uint8Array {
  if (!(value instanceof Uint8Array) || value.length === 0) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return value;
}

function invalid<T>(message: string): T {
  throw new TypeError(message);
}
