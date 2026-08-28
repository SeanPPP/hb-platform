import type {
  VoucherPaymentContext,
  VoucherPaymentContextProvider,
  VoucherProtectedAttemptState,
  VoucherProtectedAttemptStateDraft,
  VoucherProtectedPhase,
  VoucherProtectedTokenPort,
} from "../../features/payments/voucher/voucher-payment-adapter";
import type { PaymentAttempt } from "../contracts";
import type {
  InstallmentProviderAttemptRecord,
  InstallmentVoucherIntentVaultPort,
  InstallmentVoucherMaterialPort,
} from "../runtime/production-installment-payment-adapter";
import type { PersistedInstallmentAction } from "../runtime/production-installment-runtime";

import { ProtectedMaterialIntegrityError } from "@hb/pos-db/core/db/protected-material-integrity-error";
import { SqliteInstallmentActionStore } from "./sqlite-installment-action-store";
import type { SqliteInstallmentProviderAttemptStore } from "./sqlite-installment-provider-attempt-store";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

export type PersistedInstallmentVoucherIntent = Parameters<
  InstallmentVoucherIntentVaultPort["stage"]
>[0];

type VoucherIntentRow = Readonly<{
  action_id: unknown;
  installment_guid: unknown;
  payment_guid: unknown;
  store_code: unknown;
  device_code: unknown;
  cashier_id: unknown;
  amount_cents: unknown;
  payload_revision: unknown;
  intent_ciphertext: unknown;
}>;

type VoucherIntentEnvelopeV1 = Readonly<{
  format: "hb-pos-installment-voucher-intent-v1";
  aad: Readonly<{
    revision: 1;
    actionId: string;
    installmentGuid: string;
    paymentGuid: string;
    storeCode: string;
    deviceCode: string;
    cashierId: string;
    amountCents: number;
  }>;
  voucherReference: string;
  voucherReservationToken: string | null;
}>;

type VoucherStateRow = Readonly<{
  protected_reference: unknown;
  attempt_id: unknown;
  action_id: unknown;
  idempotency_key: unknown;
  payload_revision: unknown;
  state_ciphertext: unknown;
  updated_at_iso: unknown;
}>;

type VoucherAttemptBindingRow = Readonly<{
  attempt_id: unknown;
  action_id: unknown;
  idempotency_key: unknown;
  provider: unknown;
  operation: unknown;
  amount_cents: unknown;
}>;

type VoucherStateEnvelopeV1 = Readonly<{
  format: "hb-pos-installment-voucher-state-v1";
  aad: Readonly<{
    revision: 1;
    protectedReference: string;
    actionId: string;
    attemptId: string;
  }>;
  state: VoucherProtectedAttemptStateDraft;
}>;

const VOUCHER_INTENT_KEYS = new Set([
  "actionId",
  "installmentGuid",
  "paymentGuid",
  "storeCode",
  "deviceCode",
  "cashierId",
  "amountCents",
  "voucherReference",
  "voucherReservationToken",
]);

/** UI 券输入先于 action 竞争落库，因此这里不能使用 action 外键。 */
export class SqliteInstallmentVoucherIntentVault
implements InstallmentVoucherIntentVaultPort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly nowIso: () => string,
  ) {}

  public async stage(
    input: PersistedInstallmentVoucherIntent,
  ): Promise<void> {
    const intent = normalizeIntent(input);
    const existing = await selectIntentRow(
      this.connection,
      intent.actionId,
    );
    if (existing) {
      assertSameIntent(await this.decode(existing), intent);
      return;
    }

    const ciphertext = await encryptIntent(this.encryptor, intent);
    const createdAtIso = canonicalIso(
      this.nowIso(),
      "voucher intent creation time",
    );
    await this.connection.withExclusiveTransaction(async (transaction) => {
      const raced = await selectIntentRow(transaction, intent.actionId);
      if (raced) {
        assertSameIntent(await this.decode(raced), intent);
        return;
      }
      await transaction.run(
        `INSERT INTO installment_voucher_intents (
          action_id, installment_guid, payment_guid, store_code, device_code,
          cashier_id, amount_cents, payload_revision, intent_ciphertext,
          created_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?)`,
        [
          intent.actionId,
          intent.installmentGuid,
          intent.paymentGuid,
          intent.storeCode,
          intent.deviceCode,
          intent.cashierId,
          intent.amountCents,
          ciphertext,
          createdAtIso,
        ],
      );
    });
  }

  public async load(
    actionIdInput: string,
  ): Promise<PersistedInstallmentVoucherIntent | null> {
    const actionId = uuid(actionIdInput, "voucher action ID");
    const row = await selectIntentRow(this.connection, actionId);
    return row ? this.decode(row) : null;
  }

  private async decode(
    row: VoucherIntentRow,
  ): Promise<PersistedInstallmentVoucherIntent> {
    const revision = integer(
      row.payload_revision,
      "voucher intent payload revision",
    );
    if (revision !== 1) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_VERSION_INVALID",
      );
    }
    const raw = await this.encryptor.decrypt(
      bytes(row.intent_ciphertext, "voucher intent ciphertext"),
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
      !isRecord(parsed) ||
      parsed.format !== "hb-pos-installment-voucher-intent-v1" ||
      !isRecord(parsed.aad)
    ) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_SHAPE_INVALID",
      );
    }
    const envelope = parsed as VoucherIntentEnvelopeV1;
    let intent: PersistedInstallmentVoucherIntent;
    try {
      intent = normalizeIntent({
        actionId: envelope.aad.actionId,
        installmentGuid: envelope.aad.installmentGuid,
        paymentGuid: envelope.aad.paymentGuid,
        storeCode: envelope.aad.storeCode,
        deviceCode: envelope.aad.deviceCode,
        cashierId: envelope.aad.cashierId,
        amountCents: envelope.aad.amountCents,
        voucherReference: envelope.voucherReference,
        voucherReservationToken: envelope.voucherReservationToken,
      });
    } catch (error) {
      if (error instanceof TypeError) {
        throw new ProtectedMaterialIntegrityError(
          "PROTECTED_MATERIAL_SHAPE_INVALID",
        );
      }
      throw error;
    }
    if (
      envelope.aad.revision !== 1 ||
      !matches(row.action_id, intent.actionId) ||
      !matches(row.installment_guid, intent.installmentGuid) ||
      !matches(row.payment_guid, intent.paymentGuid) ||
      !matches(row.store_code, intent.storeCode) ||
      !matches(row.device_code, intent.deviceCode) ||
      !matches(row.cashier_id, intent.cashierId) ||
      integer(row.amount_cents, "voucher intent amount") !==
        intent.amountCents
    ) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_BINDING_MISMATCH",
      );
    }
    return intent;
  }
}

/** 分期专用券状态只绑定 installment_provider_attempts，不触碰通用支付表。 */
export class SqliteInstallmentVoucherProtectedTokenStore
implements VoucherProtectedTokenPort {
  private readonly actions: SqliteInstallmentActionStore;

  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly createProtectedReference: () => string,
    private readonly nowIso: () => string,
  ) {
    this.actions = new SqliteInstallmentActionStore(
      connection,
      encryptor,
      nowIso,
    );
  }

  public async save(
    input: VoucherProtectedAttemptStateDraft,
  ): Promise<string> {
    const state = normalizeVoucherState(input);
    const binding = await this.loadBinding(state.attemptId);
    assertVoucherStateBinding(state, binding.action, binding.row);

    return this.connection.withExclusiveTransaction(
      async (transaction) => {
        const existing = await selectVoucherStateByAttempt(
          transaction,
          state.attemptId,
        );
        if (!existing) {
          const protectedReference = opaqueReference(
            this.createProtectedReference(),
          );
          const ciphertext = await encryptVoucherState(
            this.encryptor,
            protectedReference,
            binding.action.action.actionId,
            state,
          );
          const timestamp = canonicalIso(
            this.nowIso(),
            "voucher state creation time",
          );
          await transaction.run(
            `INSERT INTO installment_voucher_protected_states (
              protected_reference, attempt_id, action_id, idempotency_key,
              payload_revision, state_ciphertext, created_at_iso,
              updated_at_iso
            ) VALUES (?, ?, ?, ?, 1, ?, ?, ?)`,
            [
              protectedReference,
              state.attemptId,
              binding.action.action.actionId,
              state.idempotencyKey,
              ciphertext,
              timestamp,
              timestamp,
            ],
          );
          return protectedReference;
        }

        const previous = await this.decode(existing);
        assertVoucherStateBinding(previous, binding.action, binding.row);
        assertImmutableVoucherState(previous, state);
        assertVoucherPhaseTransition(previous, state);
        const protectedReference = opaqueReference(
          text(existing.protected_reference, "protected reference"),
        );
        if (JSON.stringify(previous) === JSON.stringify(state)) {
          return protectedReference;
        }
        const ciphertext = await encryptVoucherState(
          this.encryptor,
          protectedReference,
          binding.action.action.actionId,
          state,
        );
        const previousUpdatedAt = canonicalIso(
          text(existing.updated_at_iso, "voucher state update time"),
          "voucher state update time",
        );
        const updatedAtIso = nextIso(previousUpdatedAt, this.nowIso());
        const result = await transaction.run(
          `UPDATE installment_voucher_protected_states
           SET state_ciphertext = ?, updated_at_iso = ?
           WHERE protected_reference = ? AND attempt_id = ?
             AND action_id = ? AND idempotency_key = ?
             AND state_ciphertext = ? AND updated_at_iso = ?`,
          [
            ciphertext,
            updatedAtIso,
            protectedReference,
            state.attemptId,
            binding.action.action.actionId,
            state.idempotencyKey,
            bytes(existing.state_ciphertext, "voucher state ciphertext"),
            previousUpdatedAt,
          ],
        );
        if (result.changes !== 1) {
          throw new Error("Installment voucher state CAS failed.");
        }
        return protectedReference;
      },
    );
  }

  public async getByAttempt(
    attemptIdInput: string,
  ): Promise<VoucherProtectedAttemptState | null> {
    const attemptId = identity(
      attemptIdInput,
      "voucher attempt ID",
      256,
    );
    const row = await selectVoucherStateByAttempt(
      this.connection,
      attemptId,
    );
    return row ? this.readBound(row) : null;
  }

  public async resolve(
    protectedReferenceInput: string,
  ): Promise<VoucherProtectedAttemptState | null> {
    const protectedReference = opaqueReference(
      protectedReferenceInput,
    );
    const row = await this.connection.getFirst<VoucherStateRow>(
      `${voucherStateColumns()}
       WHERE protected_reference = ?`,
      [protectedReference],
    );
    return row ? this.readBound(row) : null;
  }

  private async readBound(
    row: VoucherStateRow,
  ): Promise<VoucherProtectedAttemptState> {
    const state = await this.decode(row);
    const binding = await this.loadBinding(state.attemptId);
    assertVoucherStateBinding(state, binding.action, binding.row);
    const protectedReference = opaqueReference(
      text(row.protected_reference, "protected reference"),
    );
    if (
      !matches(row.action_id, binding.action.action.actionId) ||
      !matches(row.idempotency_key, state.idempotencyKey)
    ) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_BINDING_MISMATCH",
      );
    }
    return Object.freeze({ ...state, protectedReference });
  }

  private async decode(
    row: VoucherStateRow,
  ): Promise<VoucherProtectedAttemptStateDraft> {
    if (
      integer(row.payload_revision, "voucher state revision") !== 1
    ) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_VERSION_INVALID",
      );
    }
    const raw = await this.encryptor.decrypt(
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
      !isRecord(parsed) ||
      parsed.format !== "hb-pos-installment-voucher-state-v1" ||
      !isRecord(parsed.aad)
    ) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_SHAPE_INVALID",
      );
    }
    const envelope = parsed as VoucherStateEnvelopeV1;
    let state: VoucherProtectedAttemptStateDraft;
    try {
      state = normalizeVoucherState(envelope.state);
    } catch (error) {
      if (error instanceof TypeError) {
        throw new ProtectedMaterialIntegrityError(
          "PROTECTED_MATERIAL_SHAPE_INVALID",
        );
      }
      throw error;
    }
    if (
      envelope.aad.revision !== 1 ||
      envelope.aad.protectedReference !== row.protected_reference ||
      envelope.aad.actionId !== row.action_id ||
      envelope.aad.attemptId !== state.attemptId ||
      !matches(row.attempt_id, state.attemptId) ||
      !matches(row.idempotency_key, state.idempotencyKey)
    ) {
      throw new ProtectedMaterialIntegrityError(
        "PROTECTED_MATERIAL_BINDING_MISMATCH",
      );
    }
    return state;
  }

  private async loadBinding(
    attemptId: string,
  ): Promise<Readonly<{
    row: VoucherAttemptBindingRow;
    action: PersistedInstallmentAction;
  }>> {
    const row = await this.connection.getFirst<VoucherAttemptBindingRow>(
      `SELECT attempt_id, action_id, idempotency_key, provider,
        operation, amount_cents
       FROM installment_provider_attempts
       WHERE attempt_id = ?`,
      [attemptId],
    );
    if (!row) {
      throw new Error(
        "Installment voucher attempt binding was not found.",
      );
    }
    const actionId = uuid(row.action_id, "voucher action ID");
    const action = await this.actions.loadById(actionId);
    if (!action) {
      throw new Error(
        "Installment voucher action binding was not found.",
      );
    }
    return Object.freeze({ row, action });
  }
}

/** VoucherPaymentAdapter 的 context 从 action 与 staged intent 重建，不接受页面旁路。 */
export class SqliteInstallmentVoucherContext {
  public constructor(
    private readonly providers: Pick<
      SqliteInstallmentProviderAttemptStore,
      "loadAttemptBinding"
    >,
    private readonly intents: Pick<
      SqliteInstallmentVoucherIntentVault,
      "load"
    >,
  ) {}

  public readonly provide: VoucherPaymentContextProvider = async (
    attempt,
  ): Promise<VoucherPaymentContext> => {
    const binding = await loadProviderBinding(this.providers, attempt);
    const action = binding.action;
    if (attempt.operation === "purchase") {
      const intent = await this.intents.load(action.action.actionId);
      if (!intent || !intentMatchesAction(intent, action, binding.record)) {
        throw new Error("Installment voucher intent binding is invalid.");
      }
      return Object.freeze({
        storeCode: action.storeCode,
        cashierId: action.command.cashierId,
        voucherCode: intent.voucherReference,
        refundReason: null,
      });
    }
    if (
      action.action.kind !== "cancel-refund" ||
      action.command.kind !== "cancel-refund"
    ) {
      throw new Error("Installment voucher refund action is invalid.");
    }
    return Object.freeze({
      storeCode: action.storeCode,
      cashierId: action.command.cashierId,
      voucherCode: null,
      refundReason: action.command.reason,
    });
  };
}

/** Runtime material bridge 只验证/解析已耐久密文，不调用 provider 或远端 API。 */
export class SqliteInstallmentVoucherMaterialStore
implements InstallmentVoucherMaterialPort {
  public constructor(
    private readonly providers: Pick<
      SqliteInstallmentProviderAttemptStore,
      "loadAttemptBinding"
    >,
    private readonly intents: Pick<
      SqliteInstallmentVoucherIntentVault,
      "load"
    >,
    private readonly tokens: VoucherProtectedTokenPort,
  ) {}

  public async prepare(
    input: Parameters<InstallmentVoucherMaterialPort["prepare"]>[0],
  ): Promise<void> {
    const binding = await assertRuntimeVoucherBinding(
      this.providers,
      input.action,
      input.record,
    );
    if (binding.record.attempt.operation === "purchase") {
      const intent = await this.intents.load(
        binding.action.action.actionId,
      );
      if (
        !intent ||
        !intentMatchesAction(intent, binding.action, binding.record)
      ) {
        throw new Error(
          "Installment voucher intent is missing or rebound.",
        );
      }
    } else if (binding.action.action.kind !== "cancel-refund") {
      throw new Error("Installment voucher refund action is invalid.");
    }
  }

  public async resolveApproved(
    input: Parameters<
      InstallmentVoucherMaterialPort["resolveApproved"]
    >[0],
  ): Promise<Readonly<{
    reference: string;
    reservationToken: string | null;
  }>> {
    const binding = await assertRuntimeVoucherBinding(
      this.providers,
      input.action,
      input.record,
    );
    if (
      input.record.attempt.state !== "Approved" ||
      input.record.attempt.references.voucherReservationToken !==
        input.protectedReference
    ) {
      throw new Error(
        "Approved installment voucher reference is invalid.",
      );
    }
    const state = await this.tokens.resolve(input.protectedReference);
    if (
      !state ||
      state.phase !== "approved" ||
      state.attemptId !== binding.record.attempt.attemptId ||
      state.idempotencyKey !== binding.record.attempt.idempotencyKey ||
      state.orderGuid !== binding.record.attempt.orderGuid ||
      state.operation !== binding.record.attempt.operation ||
      state.storeCode !== binding.action.storeCode ||
      state.cashierId !== binding.action.command.cashierId ||
      state.amountCents !== binding.record.attempt.amount.cents ||
      !state.voucherCode ||
      (state.operation === "purchase" && !state.reservationToken) ||
      (state.operation === "refund" && state.reservationToken !== null)
    ) {
      throw new Error(
        "Approved installment voucher material is invalid.",
      );
    }
    return Object.freeze({
      reference: state.voucherCode,
      reservationToken: state.reservationToken,
    });
  }
}

async function selectIntentRow(
  connection: SqliteConnectionPort,
  actionId: string,
): Promise<VoucherIntentRow | null> {
  return connection.getFirst<VoucherIntentRow>(
    `SELECT action_id, installment_guid, payment_guid, store_code,
      device_code, cashier_id, amount_cents, payload_revision,
      intent_ciphertext
     FROM installment_voucher_intents
     WHERE action_id = ?`,
    [actionId],
  );
}

async function encryptIntent(
  encryptor: SensitivePayloadEncryptor,
  intent: PersistedInstallmentVoucherIntent,
): Promise<Uint8Array> {
  const envelope: VoucherIntentEnvelopeV1 = Object.freeze({
    format: "hb-pos-installment-voucher-intent-v1",
    aad: Object.freeze({
      revision: 1,
      actionId: intent.actionId,
      installmentGuid: intent.installmentGuid,
      paymentGuid: intent.paymentGuid,
      storeCode: intent.storeCode,
      deviceCode: intent.deviceCode,
      cashierId: intent.cashierId,
      amountCents: intent.amountCents,
    }),
    voucherReference: intent.voucherReference,
    voucherReservationToken: intent.voucherReservationToken,
  });
  const ciphertext = await encryptor.encrypt(JSON.stringify(envelope));
  if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
    throw new Error("Installment voucher intent encryption failed.");
  }
  return ciphertext;
}

async function selectVoucherStateByAttempt(
  connection: SqliteConnectionPort,
  attemptId: string,
): Promise<VoucherStateRow | null> {
  return connection.getFirst<VoucherStateRow>(
    `${voucherStateColumns()} WHERE attempt_id = ?`,
    [attemptId],
  );
}

function voucherStateColumns(): string {
  return `SELECT protected_reference, attempt_id, action_id,
    idempotency_key, payload_revision, state_ciphertext, updated_at_iso
  FROM installment_voucher_protected_states`;
}

async function encryptVoucherState(
  encryptor: SensitivePayloadEncryptor,
  protectedReference: string,
  actionId: string,
  state: VoucherProtectedAttemptStateDraft,
): Promise<Uint8Array> {
  const envelope: VoucherStateEnvelopeV1 = Object.freeze({
    format: "hb-pos-installment-voucher-state-v1",
    aad: Object.freeze({
      revision: 1,
      protectedReference,
      actionId,
      attemptId: state.attemptId,
    }),
    state,
  });
  const ciphertext = await encryptor.encrypt(JSON.stringify(envelope));
  if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
    throw new Error("Installment voucher state encryption failed.");
  }
  return ciphertext;
}

function normalizeVoucherState(
  input: VoucherProtectedAttemptStateDraft,
): VoucherProtectedAttemptStateDraft {
  if (!isRecord(input)) {
    throw new TypeError("Installment voucher state is invalid.");
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
    4_096,
  );
  const expiresAtIso =
    input.expiresAtIso === null
      ? null
      : canonicalIso(input.expiresAtIso, "voucher expiry");
  const reason = optionalSecret(
    input.reason ?? null,
    "voucher reason",
    1_024,
  );
  validateVoucherPhase(
    operation,
    phase,
    voucherCode,
    reservationToken,
    expiresAtIso,
  );
  return Object.freeze({
    attemptId: identity(input.attemptId, "voucher attempt ID", 256),
    idempotencyKey: identity(
      input.idempotencyKey,
      "voucher idempotency key",
      512,
    ),
    orderGuid: uuid(input.orderGuid, "voucher installment GUID"),
    operation,
    phase,
    storeCode: identity(input.storeCode, "voucher store code", 64),
    cashierId: identity(input.cashierId, "voucher cashier ID", 128),
    voucherCode,
    reservationToken,
    amountCents,
    expiresAtIso,
    reason,
  });
}

function validateVoucherPhase(
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
        (!reservationToken || !expiresAtIso)) ||
      ((phase === "purchase-prepared" ||
        phase === "lock-submitted") &&
        (reservationToken !== null || expiresAtIso !== null))
    ) {
      throw new TypeError(
        "Installment voucher purchase state is invalid.",
      );
    }
    return;
  }
  if (
    (phase !== "refund-submitted" && phase !== "approved") ||
    reservationToken !== null ||
    (phase === "refund-submitted" &&
      (voucherCode !== null || expiresAtIso !== null)) ||
    (phase === "approved" && (!voucherCode || !expiresAtIso))
  ) {
    throw new TypeError(
      "Installment voucher refund state is invalid.",
    );
  }
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
  throw new TypeError("Voucher phase is invalid.");
}

function assertVoucherStateBinding(
  state: VoucherProtectedAttemptStateDraft,
  action: PersistedInstallmentAction,
  row: VoucherAttemptBindingRow,
): void {
  if (
    !matches(row.attempt_id, state.attemptId) ||
    !matches(row.action_id, action.action.actionId) ||
    !matches(row.idempotency_key, state.idempotencyKey) ||
    !matches(row.provider, "voucher") ||
    !matches(row.operation, state.operation) ||
    integer(row.amount_cents, "voucher attempt amount") !==
      state.amountCents ||
    state.orderGuid !== action.action.installmentGuid ||
    state.storeCode !== action.storeCode ||
    state.cashierId !== action.command.cashierId ||
    (state.operation === "purchase" &&
      (action.action.kind === "cancel-refund" ||
        action.action.method !== "voucher")) ||
    (state.operation === "refund" &&
      action.action.kind !== "cancel-refund")
  ) {
    throw new Error(
      "Installment voucher state does not match provider attempt.",
    );
  }
}

function assertImmutableVoucherState(
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
    throw new Error("Installment voucher state cannot be rebound.");
  }
}

function assertVoucherPhaseTransition(
  previous: VoucherProtectedAttemptStateDraft,
  next: VoucherProtectedAttemptStateDraft,
): void {
  if (JSON.stringify(previous) === JSON.stringify(next)) return;
  const transition = `${previous.phase}->${next.phase}`;
  if (
    transition !== "purchase-prepared->lock-submitted" &&
    transition !== "lock-submitted->approved" &&
    transition !== "approved->release-submitted" &&
    transition !== "release-submitted->released" &&
    transition !== "refund-submitted->approved"
  ) {
    throw new Error(
      "Installment voucher state transition is invalid.",
    );
  }
  if (
    previous.operation === "purchase" &&
    previous.voucherCode !== next.voucherCode
  ) {
    throw new Error("Installment voucher code cannot change.");
  }
}

async function loadProviderBinding(
  providers: Pick<
    SqliteInstallmentProviderAttemptStore,
    "loadAttemptBinding"
  >,
  attempt: PaymentAttempt,
): Promise<Readonly<{
  action: PersistedInstallmentAction;
  record: InstallmentProviderAttemptRecord;
}>> {
  const binding = await providers.loadAttemptBinding(attempt.attemptId);
  if (!binding) {
    throw new Error(
      "Installment voucher provider binding was not found.",
    );
  }
  assertAttemptIdentity(binding.record.attempt, attempt);
  return binding;
}

async function assertRuntimeVoucherBinding(
  providers: Pick<
    SqliteInstallmentProviderAttemptStore,
    "loadAttemptBinding"
  >,
  action: PersistedInstallmentAction,
  record: InstallmentProviderAttemptRecord,
): Promise<Readonly<{
  action: PersistedInstallmentAction;
  record: InstallmentProviderAttemptRecord;
}>> {
  const binding = await providers.loadAttemptBinding(
    record.attempt.attemptId,
  );
  if (
    !binding ||
    binding.action.action.actionId !== action.action.actionId ||
    JSON.stringify(binding.action) !== JSON.stringify(action)
  ) {
    throw new Error("Installment voucher action binding is invalid.");
  }
  assertRecordIdentity(binding.record, record);
  if (record.attempt.provider !== "voucher") {
    throw new Error("Installment voucher provider is invalid.");
  }
  return binding;
}

function assertRecordIdentity(
  persisted: InstallmentProviderAttemptRecord,
  candidate: InstallmentProviderAttemptRecord,
): void {
  if (
    persisted.actionId !== candidate.actionId ||
    persisted.paymentGuid !== candidate.paymentGuid ||
    persisted.sourcePaymentGuid !== candidate.sourcePaymentGuid ||
    persisted.originalTenderEvidenceId !==
      candidate.originalTenderEvidenceId ||
    persisted.sourceAttemptId !== candidate.sourceAttemptId ||
    persisted.sequence !== candidate.sequence
  ) {
    throw new Error(
      "Installment voucher attempt record was rebound.",
    );
  }
  assertAttemptIdentity(persisted.attempt, candidate.attempt);
}

function assertAttemptIdentity(
  persisted: PaymentAttempt,
  candidate: PaymentAttempt,
): void {
  if (
    persisted.attemptId !== candidate.attemptId ||
    persisted.idempotencyKey !== candidate.idempotencyKey ||
    persisted.orderGuid !== candidate.orderGuid ||
    persisted.provider !== candidate.provider ||
    persisted.operation !== candidate.operation ||
    persisted.amount.currency !== candidate.amount.currency ||
    persisted.amount.cents !== candidate.amount.cents ||
    persisted.createdAtIso !== candidate.createdAtIso
  ) {
    throw new Error("Installment voucher attempt identity changed.");
  }
}

function intentMatchesAction(
  intent: PersistedInstallmentVoucherIntent,
  action: PersistedInstallmentAction,
  record: InstallmentProviderAttemptRecord,
): boolean {
  return (
    action.action.kind !== "cancel-refund" &&
    action.action.method === "voucher" &&
    intent.actionId === action.action.actionId &&
    intent.installmentGuid === action.action.installmentGuid &&
    intent.paymentGuid === record.paymentGuid &&
    intent.paymentGuid === action.action.paymentGuid &&
    intent.storeCode === action.storeCode &&
    intent.deviceCode === action.deviceCode &&
    intent.cashierId === action.command.cashierId &&
    intent.amountCents === record.attempt.amount.cents &&
    intent.amountCents === action.action.amountCents
  );
}

function opaqueReference(value: unknown): string {
  const normalized = identity(
    value,
    "voucher protected reference",
    128,
  );
  if (
    !normalized.startsWith("vpr_") ||
    normalized.length < 20 ||
    !/^[A-Za-z0-9_-]+$/u.test(normalized)
  ) {
    throw new TypeError("Voucher protected reference is invalid.");
  }
  return normalized;
}

function optionalSecret(
  value: unknown,
  label: string,
  maxLength: number,
): string | null {
  return value === null || value === undefined
    ? null
    : secret(value, label, maxLength);
}

function nextIso(previous: string, candidateInput: string): string {
  const candidate = canonicalIso(
    candidateInput,
    "voucher update time",
  );
  if (candidate > previous) return candidate;
  return new Date(Date.parse(previous) + 1).toISOString();
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return value;
}

function normalizeIntent(
  input: PersistedInstallmentVoucherIntent,
): PersistedInstallmentVoucherIntent {
  if (!isRecord(input)) {
    throw new TypeError("Installment voucher intent is invalid.");
  }
  for (const key of Object.keys(input)) {
    if (!VOUCHER_INTENT_KEYS.has(key)) {
      throw new TypeError(
        `Installment voucher intent contains unsupported field: ${key}.`,
      );
    }
  }
  const amountCents = integer(input.amountCents, "voucher intent amount");
  if (amountCents <= 0) {
    throw new TypeError("Voucher intent amount must be positive.");
  }
  return Object.freeze({
    actionId: uuid(input.actionId, "voucher action ID"),
    installmentGuid: uuid(
      input.installmentGuid,
      "voucher installment GUID",
    ),
    paymentGuid: uuid(input.paymentGuid, "voucher payment GUID"),
    storeCode: identity(input.storeCode, "voucher store code", 128),
    deviceCode: identity(input.deviceCode, "voucher device code", 128),
    cashierId: identity(input.cashierId, "voucher cashier ID", 128),
    amountCents,
    voucherReference: secret(
      input.voucherReference,
      "voucher reference",
      512,
    ),
    voucherReservationToken:
      input.voucherReservationToken === null
        ? null
        : secret(
            input.voucherReservationToken,
            "voucher reservation token",
            4096,
          ),
  });
}

function assertSameIntent(
  existing: PersistedInstallmentVoucherIntent,
  candidate: PersistedInstallmentVoucherIntent,
): void {
  if (JSON.stringify(existing) !== JSON.stringify(candidate)) {
    throw new Error("Installment voucher intent binding conflict.");
  }
}

function uuid(value: unknown, label: string): string {
  const normalized = identity(value, label, 36);
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      normalized,
    )
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized.toLowerCase();
}

function identity(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`${label} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return normalized;
}

function secret(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  return identity(value, label, maxLength);
}

function canonicalIso(value: unknown, label: string): string {
  if (typeof value !== "string") {
    throw new TypeError(`${label} is invalid.`);
  }
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString() !== value) {
    throw new TypeError(`${label} must be canonical ISO UTC.`);
  }
  return value;
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new TypeError(`${label} must be a safe integer.`);
  }
  return parsed;
}

function bytes(value: unknown, label: string): Uint8Array {
  if (!(value instanceof Uint8Array) || value.length === 0) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return value;
}

function matches(value: unknown, expected: string): boolean {
  return typeof value === "string" && value === expected;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return (
    typeof value === "object" &&
    value !== null &&
    !Array.isArray(value)
  );
}

function invalid<T>(message: string): T {
  throw new TypeError(message);
}
