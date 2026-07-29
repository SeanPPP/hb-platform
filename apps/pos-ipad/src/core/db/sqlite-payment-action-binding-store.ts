import type { SqliteConnectionPort } from "./types";

import type {
  PaymentActionBinding,
  PaymentActionBindingPort,
} from "@/features/payments";

const allowedBindingFields = new Set([
  "orderGuid",
  "actionId",
  "requestSignature",
  "attemptId",
  "idempotencyKey",
  "createdAtIso",
]);

type PaymentActionBindingRow = Readonly<{
  order_guid: unknown;
  action_id: unknown;
  request_signature: unknown;
  attempt_id: unknown;
  idempotency_key: unknown;
  created_at_iso: unknown;
}>;

/**
 * provider 调用前的耐久 action 防重绑定。
 *
 * 同一个 (orderGuid, actionId) 永远返回首次事实；attempt/idempotency 冲突直接失败，
 * 不能用 upsert 覆盖另一笔 action。
 */
export class SqlitePaymentActionBindingStore
implements PaymentActionBindingPort {
  public constructor(private readonly connection: SqliteConnectionPort) {}

  public async bindOrGet(
    proposed: PaymentActionBinding,
  ): Promise<PaymentActionBinding> {
    const normalized = validateBinding(proposed);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const existing = await findBinding(
        transaction,
        normalized.orderGuid,
        normalized.actionId,
      );
      if (existing) return existing;

      await transaction.run(
        `INSERT INTO payment_action_bindings (
          order_guid, action_id, request_signature, attempt_id,
          idempotency_key, created_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?)`,
        [
          normalized.orderGuid,
          normalized.actionId,
          normalized.requestSignature,
          normalized.attemptId,
          normalized.idempotencyKey,
          normalized.createdAtIso,
        ],
      );
      const inserted = await findBinding(
        transaction,
        normalized.orderGuid,
        normalized.actionId,
      );
      if (!inserted) {
        throw new Error("Payment action binding commit could not be observed.");
      }
      return inserted;
    });
  }
}

async function findBinding(
  connection: SqliteConnectionPort,
  orderGuid: string,
  actionId: string,
): Promise<PaymentActionBinding | null> {
  const row = await connection.getFirst<PaymentActionBindingRow>(
    `SELECT
      order_guid, action_id, request_signature, attempt_id,
      idempotency_key, created_at_iso
     FROM payment_action_bindings
     WHERE order_guid = ? AND action_id = ?`,
    [orderGuid, actionId],
  );
  return row ? mapBinding(row) : null;
}

function validateBinding(
  value: PaymentActionBinding,
): PaymentActionBinding {
  for (const field of Object.keys(value)) {
    if (!allowedBindingFields.has(field)) {
      throw new TypeError("Payment action binding contains an unexpected field.");
    }
  }
  const binding = {
    orderGuid: strictText(value.orderGuid, "orderGuid", 128),
    actionId: strictText(value.actionId, "actionId", 128),
    requestSignature: strictText(
      value.requestSignature,
      "requestSignature",
      1024,
    ),
    attemptId: strictText(value.attemptId, "attemptId", 128),
    idempotencyKey: strictText(value.idempotencyKey, "idempotencyKey", 256),
    createdAtIso: strictIso(value.createdAtIso, "createdAtIso"),
  };
  if (containsCredentialAssignment(binding.requestSignature)) {
    throw new TypeError(
      "Payment action requestSignature must not contain credentials or references.",
    );
  }
  return binding;
}

function mapBinding(row: PaymentActionBindingRow): PaymentActionBinding {
  return {
    orderGuid: strictText(row.order_guid, "persisted orderGuid", 128),
    actionId: strictText(row.action_id, "persisted actionId", 128),
    requestSignature: strictText(
      row.request_signature,
      "persisted requestSignature",
      1024,
    ),
    attemptId: strictText(row.attempt_id, "persisted attemptId", 128),
    idempotencyKey: strictText(
      row.idempotency_key,
      "persisted idempotencyKey",
      256,
    ),
    createdAtIso: strictIso(row.created_at_iso, "persisted createdAtIso"),
  };
}

function strictText(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value !== value.trim() ||
    value.length === 0 ||
    value.length > maxLength ||
    /[\u0000-\u001f\u007f]/.test(value)
  ) {
    throw new TypeError(`Invalid payment action binding ${label}.`);
  }
  return value;
}

function strictIso(value: unknown, label: string): string {
  const text = strictText(value, label, 64);
  const timestamp = Date.parse(text);
  if (!Number.isFinite(timestamp) || new Date(timestamp).toISOString() !== text) {
    throw new TypeError(`Invalid payment action binding ${label}.`);
  }
  return text;
}

function containsCredentialAssignment(value: string): boolean {
  return /(?:authorization|token|pan|cvv|secret|reference|receipt|session|txn|rfn)\s*[:=]/i.test(
    value,
  );
}
