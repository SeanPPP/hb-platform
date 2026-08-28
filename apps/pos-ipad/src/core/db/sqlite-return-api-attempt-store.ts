import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type { ReturnTenderMethod } from "@/features/returns";

export type ReturnApiAttemptState =
  | "Created"
  | "Submitted"
  | "Pending"
  | "Approved"
  | "Declined"
  | "Cancelled"
  | "Unknown";

export type PrepareReturnApiAttempt = Readonly<{
  durableAttemptId: string;
  externalAttemptId: string;
  returnOrderGuid: string;
  actionId: string;
  allocationId: string;
  externalActionId: string;
  idempotencyKey: string;
  method: Extract<ReturnTenderMethod, "cash" | "voucher" | "installment">;
  signedAmountCents: number;
  protectedContext: Readonly<Record<string, unknown>> | null;
  createdAtIso: string;
}>;

export type DurableReturnApiAttempt = Omit<
  PrepareReturnApiAttempt,
  "protectedContext"
> &
  Readonly<{
    state: ReturnApiAttemptState;
    updatedAtIso: string;
  }>;

type ApiAttemptRow = Readonly<{
  durable_attempt_id: unknown;
  external_attempt_id: unknown;
  return_order_guid: unknown;
  action_id: unknown;
  allocation_id: unknown;
  external_action_id: unknown;
  idempotency_key: unknown;
  method: unknown;
  signed_amount_cents: unknown;
  state: unknown;
  protected_context_ciphertext: unknown;
  created_at_iso: unknown;
  updated_at_iso: unknown;
}>;

/**
 * 在线现金/分期和可选 Hbpos 券退款的本地 attempt。它只建立 API 调用前的
 * durable idempotency seam，不执行网络请求。
 */
export class SqliteReturnApiAttemptStore {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
  ) {}

  public async prepareOrLoad(
    input: PrepareReturnApiAttempt,
  ): Promise<DurableReturnApiAttempt> {
    const prepared = normalizeAttempt(input);
    const contextJson =
      prepared.protectedContext === null
        ? null
        : canonicalProtectedJson(prepared.protectedContext);
    const ciphertext =
      contextJson === null
        ? null
        : await this.encryptor.encrypt(contextJson);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const existing = await readAttemptRow(
        transaction,
        prepared.durableAttemptId,
      );
      if (existing) {
        await assertAttemptReplay(
          existing,
          prepared,
          contextJson,
          this.encryptor,
        );
        return mapAttempt(existing);
      }
      const allocation = await transaction.getFirst<{
        return_order_guid: unknown;
        external_attempt_id: unknown;
        execution_kind: unknown;
        method: unknown;
        signed_amount_cents: unknown;
      }>(
        `SELECT action.return_order_guid,
          allocation.external_attempt_id, allocation.execution_kind,
          allocation.method, allocation.signed_amount_cents
         FROM return_action_allocations allocation
         INNER JOIN return_actions action
           ON action.action_id = allocation.action_id
         WHERE allocation.action_id = ?
           AND allocation.allocation_id = ?`,
        [prepared.actionId, prepared.allocationId],
      );
      if (
        !allocation ||
        text(allocation.return_order_guid, "API attempt return order") !==
          prepared.returnOrderGuid ||
        text(allocation.external_attempt_id, "API external attempt id") !==
          prepared.externalAttemptId ||
        text(allocation.execution_kind, "API attempt execution kind") !==
          "online-refund" ||
        returnApiMethod(allocation.method) !== prepared.method ||
        integer(
          allocation.signed_amount_cents,
          "API attempt signed amount",
        ) !== prepared.signedAmountCents
      ) {
        throw new Error(
          "Return API attempt does not match its durable allocation.",
        );
      }
      await transaction.run(
        `INSERT INTO return_api_attempts (
          durable_attempt_id, external_attempt_id, return_order_guid,
          action_id, allocation_id, external_action_id, idempotency_key,
          method, signed_amount_cents, state, protected_context_ciphertext,
          created_at_iso, updated_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'Created', ?, ?, ?)`,
        [
          prepared.durableAttemptId,
          prepared.externalAttemptId,
          prepared.returnOrderGuid,
          prepared.actionId,
          prepared.allocationId,
          prepared.externalActionId,
          prepared.idempotencyKey,
          prepared.method,
          prepared.signedAmountCents,
          ciphertext,
          prepared.createdAtIso,
          prepared.createdAtIso,
        ],
      );
      return requireAttempt(transaction, prepared.durableAttemptId);
    });
  }

  public get(
    durableAttemptIdInput: string,
  ): Promise<DurableReturnApiAttempt | null> {
    const durableAttemptId = strictText(
      durableAttemptIdInput,
      "return API durable attempt id",
      128,
    );
    return readAttemptRow(this.connection, durableAttemptId).then((row) =>
      row ? mapAttempt(row) : null,
    );
  }

  public async compareAndSetState(input: Readonly<{
    durableAttemptId: string;
    expected: ReturnApiAttemptState;
    next: ReturnApiAttemptState;
    updatedAtIso: string;
  }>): Promise<boolean> {
    const durableAttemptId = strictText(
      input.durableAttemptId,
      "return API durable attempt id",
      128,
    );
    const expected = apiAttemptState(input.expected);
    const next = apiAttemptState(input.next);
    if (!canTransition(expected, next)) {
      throw new TypeError("Return API attempt transition is invalid.");
    }
    const updatedAtIso = canonicalIso(
      input.updatedAtIso,
      "return API attempt update time",
    );
    const result = await this.connection.run(
      `UPDATE return_api_attempts
       SET state = ?, updated_at_iso = ?
       WHERE durable_attempt_id = ? AND state = ?`,
      [next, updatedAtIso, durableAttemptId, expected],
    );
    return result.changes === 1;
  }

  public async resolveProtectedContext(
    durableAttemptIdInput: string,
  ): Promise<Readonly<Record<string, unknown>> | null> {
    const durableAttemptId = strictText(
      durableAttemptIdInput,
      "return API durable attempt id",
      128,
    );
    const row = await readAttemptRow(this.connection, durableAttemptId);
    if (!row) return null;
    const ciphertext = optionalBytes(row.protected_context_ciphertext);
    if (!ciphertext) return null;
    return parseProtectedJson(await this.encryptor.decrypt(ciphertext));
  }
}

async function readAttemptRow(
  connection: SqliteConnectionPort,
  durableAttemptId: string,
): Promise<ApiAttemptRow | null> {
  return connection.getFirst<ApiAttemptRow>(
    `SELECT durable_attempt_id, external_attempt_id, return_order_guid,
      action_id, allocation_id, external_action_id, idempotency_key,
      method, signed_amount_cents, state, protected_context_ciphertext,
      created_at_iso, updated_at_iso
     FROM return_api_attempts
     WHERE durable_attempt_id = ?`,
    [durableAttemptId],
  );
}

async function requireAttempt(
  connection: SqliteConnectionPort,
  durableAttemptId: string,
): Promise<DurableReturnApiAttempt> {
  const row = await readAttemptRow(connection, durableAttemptId);
  if (!row) throw new Error("Return API attempt commit is missing.");
  return mapAttempt(row);
}

async function assertAttemptReplay(
  row: ApiAttemptRow,
  input: PrepareReturnApiAttempt,
  expectedContextJson: string | null,
  encryptor: SensitivePayloadEncryptor,
): Promise<void> {
  const mapped = mapAttempt(row);
  if (
    mapped.durableAttemptId !== input.durableAttemptId ||
    mapped.externalAttemptId !== input.externalAttemptId ||
    mapped.returnOrderGuid !== input.returnOrderGuid ||
    mapped.actionId !== input.actionId ||
    mapped.allocationId !== input.allocationId ||
    mapped.externalActionId !== input.externalActionId ||
    mapped.idempotencyKey !== input.idempotencyKey ||
    mapped.method !== input.method ||
    mapped.signedAmountCents !== input.signedAmountCents ||
    mapped.createdAtIso !== input.createdAtIso
  ) {
    throw new Error(
      "Return API attempt was replayed with different immutable content.",
    );
  }
  const ciphertext = optionalBytes(row.protected_context_ciphertext);
  const contextJson =
    ciphertext === null ? null : await encryptor.decrypt(ciphertext);
  if (contextJson !== expectedContextJson) {
    throw new Error(
      "Return API attempt was replayed with different protected context.",
    );
  }
}

function normalizeAttempt(
  input: PrepareReturnApiAttempt,
): PrepareReturnApiAttempt {
  const signedAmountCents = integer(
    input.signedAmountCents,
    "return API signed amount",
  );
  if (signedAmountCents >= 0) {
    throw new TypeError("Return API amount must be negative.");
  }
  return {
    durableAttemptId: strictText(
      input.durableAttemptId,
      "return API durable attempt id",
      128,
    ),
    externalAttemptId: strictText(
      input.externalAttemptId,
      "return API external attempt id",
      128,
    ),
    returnOrderGuid: strictText(
      input.returnOrderGuid,
      "return API order guid",
      128,
    ),
    actionId: strictText(input.actionId, "return API action id", 128),
    allocationId: strictText(
      input.allocationId,
      "return API allocation id",
      128,
    ),
    externalActionId: strictText(
      input.externalActionId,
      "return API external action id",
      128,
    ),
    idempotencyKey: strictText(
      input.idempotencyKey,
      "return API idempotency key",
      256,
    ),
    method: returnApiMethod(input.method),
    signedAmountCents,
    protectedContext: input.protectedContext,
    createdAtIso: canonicalIso(
      input.createdAtIso,
      "return API attempt created time",
    ),
  };
}

function mapAttempt(row: ApiAttemptRow): DurableReturnApiAttempt {
  return Object.freeze({
    durableAttemptId: strictText(
      row.durable_attempt_id,
      "persisted API durable attempt id",
      128,
    ),
    externalAttemptId: strictText(
      row.external_attempt_id,
      "persisted API external attempt id",
      128,
    ),
    returnOrderGuid: strictText(
      row.return_order_guid,
      "persisted API return order guid",
      128,
    ),
    actionId: strictText(
      row.action_id,
      "persisted API action id",
      128,
    ),
    allocationId: strictText(
      row.allocation_id,
      "persisted API allocation id",
      128,
    ),
    externalActionId: strictText(
      row.external_action_id,
      "persisted API external action id",
      128,
    ),
    idempotencyKey: strictText(
      row.idempotency_key,
      "persisted API idempotency key",
      256,
    ),
    method: returnApiMethod(row.method),
    signedAmountCents: negativeInteger(
      row.signed_amount_cents,
      "persisted API signed amount",
    ),
    state: apiAttemptState(row.state),
    createdAtIso: canonicalIso(
      text(row.created_at_iso, "API attempt created time"),
      "API attempt created time",
    ),
    updatedAtIso: canonicalIso(
      text(row.updated_at_iso, "API attempt updated time"),
      "API attempt updated time",
    ),
  });
}

function canTransition(
  current: ReturnApiAttemptState,
  next: ReturnApiAttemptState,
): boolean {
  if (current === next) return true;
  const allowed: Readonly<Record<ReturnApiAttemptState, readonly ReturnApiAttemptState[]>> = {
    Created: ["Submitted", "Cancelled", "Unknown"],
    Submitted: ["Pending", "Approved", "Declined", "Cancelled", "Unknown"],
    Pending: ["Approved", "Declined", "Cancelled", "Unknown"],
    Unknown: ["Pending", "Approved", "Declined", "Cancelled", "Unknown"],
    Approved: [],
    Declined: [],
    Cancelled: [],
  };
  return allowed[current].includes(next);
}

function apiAttemptState(value: unknown): ReturnApiAttemptState {
  if (
    value === "Created" ||
    value === "Submitted" ||
    value === "Pending" ||
    value === "Approved" ||
    value === "Declined" ||
    value === "Cancelled" ||
    value === "Unknown"
  ) {
    return value;
  }
  throw new Error("Return API attempt state is invalid.");
}

function returnApiMethod(
  value: unknown,
): PrepareReturnApiAttempt["method"] {
  if (value === "cash" || value === "voucher" || value === "installment") {
    return value;
  }
  throw new Error("Return API attempt method is invalid.");
}

function canonicalProtectedJson(
  value: Readonly<Record<string, unknown>>,
): string {
  let json: string;
  try {
    json = JSON.stringify(value);
  } catch {
    throw new TypeError("Return API protected context is not JSON.");
  }
  if (!json || json[0] !== "{" || json.length > 1_048_576) {
    throw new TypeError("Return API protected context is invalid.");
  }
  return json;
}

function parseProtectedJson(
  value: string,
): Readonly<Record<string, unknown>> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    throw new Error("Return API protected context is corrupt.");
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new Error("Return API protected context is invalid.");
  }
  return parsed as Readonly<Record<string, unknown>>;
}

function optionalBytes(value: unknown): Uint8Array | null {
  if (value === null || value === undefined) return null;
  if (value instanceof Uint8Array && value.length > 0) return value;
  throw new Error("Return API protected ciphertext is invalid.");
}

function strictText(value: unknown, label: string, max: number): string {
  if (
    typeof value !== "string" ||
    value !== value.trim() ||
    !value ||
    value.length > max ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string" || !value) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return value;
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new TypeError(`${label} is invalid.`);
  }
  return parsed;
}

function negativeInteger(value: unknown, label: string): number {
  const parsed = integer(value, label);
  if (parsed >= 0) throw new Error(`${label} must be negative.`);
  return parsed;
}

function canonicalIso(value: string, label: string): string {
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString() !== value) {
    throw new TypeError(`${label} must be canonical ISO UTC.`);
  }
  return value;
}
