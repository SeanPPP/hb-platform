import { ProtectedMaterialIntegrityError } from "@hb/pos-db/core/db/protected-material-integrity-error";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type {
  VoucherProtectedAttemptState,
  VoucherProtectedAttemptStateDraft,
  VoucherProtectedPhase,
  VoucherProtectedTokenPort,
} from "@/features/payments/voucher";

type ProtectedStateRow = Readonly<{
  protected_reference: unknown;
  attempt_id: unknown;
  idempotency_key: unknown;
  order_guid: unknown;
  state_ciphertext: unknown;
  updated_at_iso: unknown;
}>;

type VoucherAttemptBindingRow = Readonly<{
  attempt_id: unknown;
  idempotency_key: unknown;
  order_guid: unknown;
  provider: unknown;
  operation: unknown;
  amount_cents: unknown;
  store_code: unknown;
  cashier_id: unknown;
}>;

type PersistedVoucherStateV1 = Readonly<{
  version: 1;
  state: VoucherProtectedAttemptStateDraft;
}>;

/**
 * voucherCode、reservationToken、phase、状态原因与完整上下文只写入
 * SQLCipher 内的二次密文。明文列仅用于不可换绑的 attempt/order 幂等索引。
 */
export class SqliteVoucherProtectedTokenStore
implements VoucherProtectedTokenPort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly createProtectedReference: () => string,
    private readonly nowIso: () => string,
  ) {}

  public save(stateInput: VoucherProtectedAttemptStateDraft): Promise<string> {
    const state = validateDraft(stateInput);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      await assertAttemptBinding(transaction, state);
      const existing = await transaction.getFirst<ProtectedStateRow>(
        `SELECT protected_reference, attempt_id, idempotency_key, order_guid,
          state_ciphertext, updated_at_iso
         FROM voucher_protected_attempt_states
         WHERE attempt_id = ?`,
        [state.attemptId],
      );
      if (!existing) {
        const protectedReference = opaqueReference(
          this.createProtectedReference(),
          "vpr_",
        );
        const ciphertext = await encryptState(this.encryptor, state);
        const now = canonicalIso(this.nowIso(), "voucher state time");
        await transaction.run(
          `INSERT INTO voucher_protected_attempt_states (
            protected_reference, attempt_id, idempotency_key, order_guid,
            state_ciphertext, created_at_iso, updated_at_iso
          ) VALUES (?, ?, ?, ?, ?, ?, ?)`,
          [
            protectedReference,
            state.attemptId,
            state.idempotencyKey,
            state.orderGuid,
            ciphertext,
            now,
            now,
          ],
        );
        return protectedReference;
      }

      assertPlainBinding(existing, state);
      const previous = await decodeRow(this.encryptor, existing);
      assertImmutableStateBinding(previous, state);
      assertVoucherPhaseTransition(previous, state);
      if (sameVoucherState(previous, state)) {
        return text(existing.protected_reference, "protected reference");
      }

      const ciphertext = await encryptState(this.encryptor, state);
      const previousCiphertext = bytes(
        existing.state_ciphertext,
        "voucher state ciphertext",
      );
      const previousUpdatedAt = canonicalIso(
        text(existing.updated_at_iso, "voucher state update time"),
        "voucher state update time",
      );
      const nextUpdatedAt = nextIso(previousUpdatedAt, this.nowIso());
      const changed = await transaction.run(
        `UPDATE voucher_protected_attempt_states
         SET state_ciphertext = ?, updated_at_iso = ?
         WHERE protected_reference = ?
           AND attempt_id = ?
           AND idempotency_key = ?
           AND order_guid = ?
           AND state_ciphertext = ?
           AND updated_at_iso = ?`,
        [
          ciphertext,
          nextUpdatedAt,
          text(existing.protected_reference, "protected reference"),
          state.attemptId,
          state.idempotencyKey,
          state.orderGuid,
          previousCiphertext,
          previousUpdatedAt,
        ],
      );
      if (changed.changes !== 1) {
        throw new Error("Voucher protected state CAS failed.");
      }
      return text(existing.protected_reference, "protected reference");
    });
  }

  public getByAttempt(
    attemptIdInput: string,
  ): Promise<VoucherProtectedAttemptState | null> {
    const attemptId = strictId(attemptIdInput, "voucher attempt id");
    return this.readOne(
      "WHERE attempt_id = ?",
      [attemptId],
    );
  }

  public resolve(
    protectedReferenceInput: string,
  ): Promise<VoucherProtectedAttemptState | null> {
    const protectedReference = opaqueReference(
      protectedReferenceInput,
      "vpr_",
    );
    return this.readOne(
      "WHERE protected_reference = ?",
      [protectedReference],
    );
  }

  private async readOne(
    where: string,
    parameters: readonly string[],
  ): Promise<VoucherProtectedAttemptState | null> {
    const row = await this.connection.getFirst<ProtectedStateRow>(
      `SELECT protected_reference, attempt_id, idempotency_key, order_guid,
        state_ciphertext, updated_at_iso
       FROM voucher_protected_attempt_states
       ${where}`,
      parameters,
    );
    if (!row) return null;
    const state = await decodeRow(this.encryptor, row);
    assertPlainBinding(row, state);
    return {
      ...state,
      protectedReference: opaqueReference(
        text(row.protected_reference, "protected reference"),
        "vpr_",
      ),
    };
  }
}

async function assertAttemptBinding(
  transaction: SqliteConnectionPort,
  state: VoucherProtectedAttemptStateDraft,
): Promise<void> {
  const row = await transaction.getFirst<VoucherAttemptBindingRow>(
    `SELECT p.attempt_id, p.idempotency_key, p.order_guid, p.provider,
      p.operation, p.amount_cents, o.store_code, o.cashier_id
     FROM payment_attempts p
     INNER JOIN local_orders o ON o.order_guid = p.order_guid
     WHERE p.attempt_id = ?`,
    [state.attemptId],
  );
  if (
    !row ||
    text(row.attempt_id, "attempt id") !== state.attemptId ||
    text(row.idempotency_key, "idempotency key") !== state.idempotencyKey ||
    text(row.order_guid, "order guid") !== state.orderGuid ||
    text(row.provider, "provider") !== "voucher" ||
    text(row.operation, "operation") !== state.operation ||
    integer(row.amount_cents, "attempt amount") !== state.amountCents ||
    text(row.store_code, "store code") !== state.storeCode ||
    text(row.cashier_id, "cashier id") !== state.cashierId
  ) {
    throw new Error(
      "Voucher protected state does not match its persisted attempt and order.",
    );
  }
}

async function encryptState(
  encryptor: SensitivePayloadEncryptor,
  state: VoucherProtectedAttemptStateDraft,
): Promise<Uint8Array> {
  const ciphertext = await encryptor.encrypt(
    JSON.stringify({
      version: 1,
      state,
    } satisfies PersistedVoucherStateV1),
  );
  if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
    throw new Error("Voucher protected state encryption failed.");
  }
  return ciphertext;
}

async function decodeRow(
  encryptor: SensitivePayloadEncryptor,
  row: ProtectedStateRow,
): Promise<VoucherProtectedAttemptStateDraft> {
  const raw = await encryptor.decrypt(
    bytes(row.state_ciphertext, "voucher state ciphertext"),
  );
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new ProtectedMaterialIntegrityError(
      "PROTECTED_MATERIAL_JSON_INVALID",
    );
  }
  if (
    !parsed ||
    typeof parsed !== "object" ||
    Array.isArray(parsed)
  ) {
    throw new ProtectedMaterialIntegrityError(
      "PROTECTED_MATERIAL_SHAPE_INVALID",
    );
  }
  if ((parsed as { version?: unknown }).version !== 1) {
    throw new ProtectedMaterialIntegrityError(
      "PROTECTED_MATERIAL_VERSION_INVALID",
    );
  }
  try {
    return validateDraft(
      (parsed as { state?: VoucherProtectedAttemptStateDraft }).state as
        VoucherProtectedAttemptStateDraft,
    );
  } catch (error) {
    // validateDraft 的 TypeError 只描述成功解密后的确定性 schema 损坏。
    if (error instanceof TypeError) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_SHAPE_INVALID",
      );
    }
    throw error;
  }
}

function validateDraft(
  input: VoucherProtectedAttemptStateDraft,
): VoucherProtectedAttemptStateDraft {
  if (!input || typeof input !== "object") {
    throw new TypeError("Voucher protected state is required.");
  }
  const operation =
    input.operation === "purchase" || input.operation === "refund"
      ? input.operation
      : invalid<never>("Voucher operation is invalid.");
  const phase = voucherPhase(input.phase);
  const amountCents = integer(input.amountCents, "voucher amount");
  if (
    (operation === "purchase" && amountCents <= 0) ||
    (operation === "refund" && amountCents >= 0)
  ) {
    throw new TypeError("Voucher amount sign does not match operation.");
  }
  const voucherCode = optionalSecret(
    input.voucherCode,
    "voucher code",
    512,
  );
  const reservationToken = optionalSecret(
    input.reservationToken,
    "voucher reservation token",
    4096,
  );
  const expiresAtIso =
    input.expiresAtIso === null
      ? null
      : canonicalIso(input.expiresAtIso, "voucher expiry");
  const reason = optionalSecret(input.reason ?? null, "voucher reason", 1024);
  const latestBalanceConfirmation = normalizeLatestBalanceConfirmation(
    input.latestBalanceConfirmation,
    operation,
    phase,
  );
  validatePhaseShape(
    operation,
    phase,
    voucherCode,
    reservationToken,
    expiresAtIso,
  );
  return Object.freeze({
    attemptId: strictId(input.attemptId, "voucher attempt id"),
    idempotencyKey: strictText(
      input.idempotencyKey,
      "voucher idempotency key",
      256,
    ),
    orderGuid: strictId(input.orderGuid, "voucher order guid"),
    operation,
    phase,
    storeCode: strictText(input.storeCode, "voucher store code", 64),
    cashierId: strictId(input.cashierId, "voucher cashier id"),
    voucherCode,
    reservationToken,
    amountCents,
    expiresAtIso,
    reason,
    ...(latestBalanceConfirmation
      ? { latestBalanceConfirmation }
      : {}),
  });
}

function validatePhaseShape(
  operation: "purchase" | "refund",
  phase: VoucherProtectedPhase,
  voucherCode: string | null,
  reservationToken: string | null,
  expiresAtIso: string | null,
): void {
  if (operation === "purchase") {
    if (
      phase === "refund-submitted" ||
      !voucherCode ||
      ((phase === "approved" ||
        phase === "release-submitted" ||
        phase === "released") &&
        (!reservationToken || !expiresAtIso))
    ) {
      throw new TypeError("Voucher purchase protected state is invalid.");
    }
    if (
      (phase === "purchase-prepared" || phase === "lock-submitted") &&
      (reservationToken !== null || expiresAtIso !== null)
    ) {
      throw new TypeError("Voucher purchase preparation contains a token.");
    }
    return;
  }
  if (
    phase !== "refund-submitted" &&
    phase !== "approved"
  ) {
    throw new TypeError("Voucher refund protected phase is invalid.");
  }
  if (reservationToken !== null) {
    throw new TypeError("Voucher refund cannot contain a reservation token.");
  }
  if (
    (phase === "refund-submitted" &&
      (voucherCode !== null || expiresAtIso !== null)) ||
    (phase === "approved" && (!voucherCode || !expiresAtIso))
  ) {
    throw new TypeError("Voucher refund protected state is invalid.");
  }
}

function assertPlainBinding(
  row: ProtectedStateRow,
  state: VoucherProtectedAttemptStateDraft,
): void {
  if (
    !matchesPlainBinding(row.attempt_id, state.attemptId) ||
    !matchesPlainBinding(row.idempotency_key, state.idempotencyKey) ||
    !matchesPlainBinding(row.order_guid, state.orderGuid)
  ) {
    throw new ProtectedMaterialIntegrityError(
      "PROTECTED_MATERIAL_BINDING_MISMATCH",
    );
  }
}

function matchesPlainBinding(value: unknown, expected: string): boolean {
  return typeof value === "string" &&
    value.trim() === value &&
    value.length > 0 &&
    value === expected;
}

function assertImmutableStateBinding(
  previous: VoucherProtectedAttemptStateDraft,
  next: VoucherProtectedAttemptStateDraft,
): void {
  if (
    previous.attemptId !== next.attemptId ||
    previous.idempotencyKey !== next.idempotencyKey ||
    previous.orderGuid !== next.orderGuid ||
    previous.operation !== next.operation ||
    previous.storeCode !== next.storeCode ||
    previous.cashierId !== next.cashierId ||
    previous.amountCents !== next.amountCents ||
    previous.reason !== next.reason
  ) {
    throw new Error("Voucher protected state cannot be rebound.");
  }
}

function assertVoucherPhaseTransition(
  previous: VoucherProtectedAttemptStateDraft,
  next: VoucherProtectedAttemptStateDraft,
): void {
  if (sameVoucherState(previous, next)) return;
  if (
    previous.operation === "purchase" &&
    previous.phase === "approved" &&
    next.phase === "approved" &&
    previous.latestBalanceConfirmation === undefined &&
    next.latestBalanceConfirmation !== undefined &&
    sameVoucherStateWithoutLatestBalance(previous, next)
  ) {
    return;
  }
  const transition = `${previous.phase}->${next.phase}`;
  if (
    transition !== "purchase-prepared->lock-submitted" &&
    transition !== "lock-submitted->approved" &&
    transition !== "approved->release-submitted" &&
    transition !== "release-submitted->released" &&
    transition !== "refund-submitted->approved"
  ) {
    throw new Error("Voucher protected state transition is invalid.");
  }
  if (
    previous.operation === "purchase" &&
    previous.voucherCode !== next.voucherCode
  ) {
    throw new Error("Voucher code cannot change during purchase.");
  }
  if (
    JSON.stringify(previous.latestBalanceConfirmation ?? null) !==
    JSON.stringify(next.latestBalanceConfirmation ?? null)
  ) {
    throw new Error("Voucher latest balance confirmation cannot be changed.");
  }
}

function sameVoucherState(
  left: VoucherProtectedAttemptStateDraft,
  right: VoucherProtectedAttemptStateDraft,
): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function sameVoucherStateWithoutLatestBalance(
  left: VoucherProtectedAttemptStateDraft,
  right: VoucherProtectedAttemptStateDraft,
): boolean {
  const { latestBalanceConfirmation: _left, ...leftBase } = left;
  const { latestBalanceConfirmation: _right, ...rightBase } = right;
  return JSON.stringify(leftBase) === JSON.stringify(rightBase);
}

function normalizeLatestBalanceConfirmation(
  value: VoucherProtectedAttemptStateDraft["latestBalanceConfirmation"],
  operation: "purchase" | "refund",
  phase: VoucherProtectedPhase,
): VoucherProtectedAttemptStateDraft["latestBalanceConfirmation"] {
  if (value === undefined) return undefined;
  if (
    !value ||
    typeof value !== "object" ||
    operation !== "purchase" ||
    (phase !== "approved" &&
      phase !== "release-submitted" &&
      phase !== "released")
  ) {
    throw new TypeError("Voucher latest balance confirmation is invalid.");
  }
  const confirmedAtIso = canonicalIso(
    value.confirmedAtIso,
    "voucher latest balance confirmation time",
  );
  if (value.status === "unavailable") {
    if (value.remainingCents !== null) {
      throw new TypeError("Voucher unavailable balance must be null.");
    }
    return Object.freeze({
      status: "unavailable",
      remainingCents: null,
      confirmedAtIso,
    });
  }
  if (
    value.status !== "confirmed" ||
    !Number.isSafeInteger(value.remainingCents) ||
    value.remainingCents < 0
  ) {
    throw new TypeError("Voucher confirmed balance is invalid.");
  }
  return Object.freeze({
    status: "confirmed",
    remainingCents: value.remainingCents,
    confirmedAtIso,
  });
}

function nextIso(previous: string, candidateInput: string): string {
  const candidate = canonicalIso(candidateInput, "voucher update time");
  if (candidate > previous) return candidate;
  const next = new Date(Date.parse(previous) + 1).toISOString();
  return canonicalIso(next, "voucher update time");
}

function voucherPhase(value: unknown): VoucherProtectedPhase {
  if (
    value === "purchase-prepared" ||
    value === "lock-submitted" ||
    value === "approved" ||
    value === "release-submitted" ||
    value === "released" ||
    value === "refund-submitted"
  ) {
    return value;
  }
  throw new TypeError("Voucher protected phase is invalid.");
}

function opaqueReference(value: string, prefix: "vpr_"): string {
  const normalized = value.trim();
  if (
    !normalized.startsWith(prefix) ||
    normalized.length < 20 ||
    normalized.length > 128 ||
    !/^[A-Za-z0-9_-]+$/u.test(normalized)
  ) {
    throw new TypeError("Voucher protected reference is invalid.");
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

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new TypeError(`${label} must be integer cents.`);
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
