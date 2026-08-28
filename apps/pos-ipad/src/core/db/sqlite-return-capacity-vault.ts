import { ProtectedMaterialIntegrityError } from "@hb/pos-db/core/db/protected-material-integrity-error";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type { ReturnTenderMethod } from "@/features/returns";

export type ReturnTenderCapacitySeed = Readonly<{
  capacityId: string;
  originalOrderGuid: string;
  method: ReturnTenderMethod;
  originalAmountCents: number;
  remainingAmountCents: number;
  /**
   * PaymentId、RFN、券 token 等只能放在此对象并经二次加密。
   * 现金容量不需要 provider context，必须传 null。
   */
  protectedContext: Readonly<Record<string, unknown>> | null;
  observedAtIso: string;
}>;

export type ReturnTenderCapacity = Readonly<{
  capacityId: string;
  originalOrderGuid: string;
  method: ReturnTenderMethod;
  originalAmountCents: number;
  remainingAmountCents: number;
  observedAtIso: string;
}>;

type CapacityRow = Readonly<{
  capacity_id: unknown;
  original_order_guid: unknown;
  method: unknown;
  original_amount_cents: unknown;
  remaining_amount_cents: unknown;
  protected_context_ciphertext: unknown;
  observed_at_iso: unknown;
}>;

/**
 * 原支付引用的 SQLCipher 二次加密 Vault。普通账本仅持有 capacityId；
 * 只有支付 adapter 可通过本 facade 解密 provider context。
 */
export class SqliteReturnCapacityVault {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly nowIso: () => string,
  ) {}

  public async seedOrLoad(
    input: ReturnTenderCapacitySeed,
  ): Promise<ReturnTenderCapacity> {
    const seed = normalizeSeed(input);
    const contextJson =
      seed.protectedContext === null
        ? null
        : canonicalProtectedJson(seed.protectedContext);
    const ciphertext =
      contextJson === null
        ? null
        : await this.encryptor.encrypt(contextJson);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const existing = await readCapacityRow(transaction, seed.capacityId);
      if (existing) {
        await assertSeedMatchesExisting(
          existing,
          seed,
          contextJson,
          this.encryptor,
        );
        // 可信远端刷新只能收紧容量，绝不能把本地已消费金额加回去。
        if (
          seed.remainingAmountCents <
          integer(existing.remaining_amount_cents, "capacity remaining amount")
        ) {
          await transaction.run(
            `UPDATE return_tender_capacities
             SET remaining_amount_cents = ?, observed_at_iso = ?,
               updated_at_iso = ?
             WHERE capacity_id = ? AND remaining_amount_cents > ?`,
            [
              seed.remainingAmountCents,
              seed.observedAtIso,
              this.nowIso(),
              seed.capacityId,
              seed.remainingAmountCents,
            ],
          );
        }
        return requireCapacity(transaction, seed.capacityId);
      }

      const createdAtIso = canonicalIso(
        this.nowIso(),
        "capacity created time",
      );
      await transaction.run(
        `INSERT INTO return_tender_capacities (
          capacity_id, original_order_guid, method,
          original_amount_cents, remaining_amount_cents,
          protected_context_ciphertext, observed_at_iso,
          created_at_iso, updated_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        [
          seed.capacityId,
          seed.originalOrderGuid,
          seed.method,
          seed.originalAmountCents,
          seed.remainingAmountCents,
          ciphertext,
          seed.observedAtIso,
          createdAtIso,
          createdAtIso,
        ],
      );
      return requireCapacity(transaction, seed.capacityId);
    });
  }

  public get(capacityIdInput: string): Promise<ReturnTenderCapacity | null> {
    const capacityId = strictText(
      capacityIdInput,
      "return capacity id",
      128,
    );
    return this.connection
      .getFirst<CapacityRow>(
        `SELECT capacity_id, original_order_guid, method,
          original_amount_cents, remaining_amount_cents,
          protected_context_ciphertext, observed_at_iso
         FROM return_tender_capacities
         WHERE capacity_id = ?`,
        [capacityId],
      )
      .then((row) => (row ? mapCapacity(row) : null));
  }

  public async resolveProtectedContext(
    capacityIdInput: string,
  ): Promise<Readonly<Record<string, unknown>> | null> {
    const capacityId = strictText(
      capacityIdInput,
      "return capacity id",
      128,
    );
    const row = await this.connection.getFirst<CapacityRow>(
      `SELECT capacity_id, original_order_guid, method,
        original_amount_cents, remaining_amount_cents,
        protected_context_ciphertext, observed_at_iso
       FROM return_tender_capacities
       WHERE capacity_id = ?`,
      [capacityId],
    );
    if (!row) return null;
    const ciphertext = optionalBytes(row.protected_context_ciphertext);
    if (!ciphertext) {
      if (returnTenderMethod(row.method) !== "cash") {
        throw new ProtectedMaterialIntegrityError(
          "PROTECTED_MATERIAL_CONTEXT_MISSING",
        );
      }
      return null;
    }
    return parseProtectedJson(await this.encryptor.decrypt(ciphertext));
  }
}

async function readCapacityRow(
  connection: SqliteConnectionPort,
  capacityId: string,
): Promise<CapacityRow | null> {
  return connection.getFirst<CapacityRow>(
    `SELECT capacity_id, original_order_guid, method,
      original_amount_cents, remaining_amount_cents,
      protected_context_ciphertext, observed_at_iso
     FROM return_tender_capacities
     WHERE capacity_id = ?`,
    [capacityId],
  );
}

async function requireCapacity(
  connection: SqliteConnectionPort,
  capacityId: string,
): Promise<ReturnTenderCapacity> {
  const row = await readCapacityRow(connection, capacityId);
  if (!row) throw new Error("Return tender capacity commit is missing.");
  return mapCapacity(row);
}

async function assertSeedMatchesExisting(
  row: CapacityRow,
  seed: ReturnTenderCapacitySeed,
  expectedContextJson: string | null,
  encryptor: SensitivePayloadEncryptor,
): Promise<void> {
  if (
    text(row.capacity_id, "capacity id") !== seed.capacityId ||
    text(row.original_order_guid, "capacity original order") !==
      seed.originalOrderGuid ||
    returnTenderMethod(row.method) !== seed.method ||
    integer(row.original_amount_cents, "capacity original amount") !==
      seed.originalAmountCents
  ) {
    throw new Error(
      "Return tender capacity was replayed with different immutable identity.",
    );
  }
  const ciphertext = optionalBytes(row.protected_context_ciphertext);
  const existingContextJson =
    ciphertext === null ? null : await encryptor.decrypt(ciphertext);
  if (existingContextJson !== expectedContextJson) {
    throw new Error(
      "Return tender capacity was replayed with different protected context.",
    );
  }
}

function normalizeSeed(
  input: ReturnTenderCapacitySeed,
): ReturnTenderCapacitySeed {
  const capacityId = strictText(input.capacityId, "return capacity id", 128);
  const originalOrderGuid = strictText(
    input.originalOrderGuid,
    "capacity original order",
    128,
  );
  const method = returnTenderMethod(input.method);
  const originalAmountCents = nonNegativeInteger(
    input.originalAmountCents,
    "capacity original amount",
  );
  const remainingAmountCents = nonNegativeInteger(
    input.remainingAmountCents,
    "capacity remaining amount",
  );
  if (remainingAmountCents > originalAmountCents) {
    throw new TypeError("Return capacity remaining amount exceeds original.");
  }
  if (
    (method === "cash" && input.protectedContext !== null) ||
    (method !== "cash" && input.protectedContext === null)
  ) {
    throw new TypeError("Return capacity protected context is invalid.");
  }
  return {
    capacityId,
    originalOrderGuid,
    method,
    originalAmountCents,
    remainingAmountCents,
    protectedContext: input.protectedContext,
    observedAtIso: canonicalIso(
      input.observedAtIso,
      "capacity observed time",
    ),
  };
}

function mapCapacity(row: CapacityRow): ReturnTenderCapacity {
  const originalAmountCents = nonNegativeInteger(
    row.original_amount_cents,
    "persisted capacity original amount",
  );
  const remainingAmountCents = nonNegativeInteger(
    row.remaining_amount_cents,
    "persisted capacity remaining amount",
  );
  if (remainingAmountCents > originalAmountCents) {
    throw new Error("Persisted return capacity amount is invalid.");
  }
  return Object.freeze({
    capacityId: strictText(
      row.capacity_id,
      "persisted return capacity id",
      128,
    ),
    originalOrderGuid: strictText(
      row.original_order_guid,
      "persisted capacity original order",
      128,
    ),
    method: returnTenderMethod(row.method),
    originalAmountCents,
    remainingAmountCents,
    observedAtIso: canonicalIso(
      text(row.observed_at_iso, "capacity observed time"),
      "capacity observed time",
    ),
  });
}

function canonicalProtectedJson(
  value: Readonly<Record<string, unknown>>,
): string {
  let json: string;
  try {
    json = JSON.stringify(value);
  } catch {
    throw new TypeError("Return capacity protected context is not JSON.");
  }
  if (
    !json ||
    json.length > 1_048_576 ||
    json[0] !== "{" ||
    Object.keys(value).length === 0
  ) {
    throw new TypeError("Return capacity protected context is invalid.");
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
    throw new ProtectedMaterialIntegrityError(
      "PROTECTED_MATERIAL_JSON_INVALID",
    );
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new ProtectedMaterialIntegrityError(
      "PROTECTED_MATERIAL_SHAPE_INVALID",
    );
  }
  return parsed as Readonly<Record<string, unknown>>;
}

function returnTenderMethod(value: unknown): ReturnTenderMethod {
  if (
    value === "cash" ||
    value === "card" ||
    value === "voucher" ||
    value === "installment"
  ) {
    return value;
  }
  throw new Error("Return tender capacity method is invalid.");
}

function optionalBytes(value: unknown): Uint8Array | null {
  if (value === null || value === undefined) return null;
  if (value instanceof Uint8Array && value.length > 0) return value;
  throw new Error("Return capacity ciphertext is invalid.");
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

function nonNegativeInteger(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 0) {
    throw new TypeError(`${label} is invalid.`);
  }
  return parsed;
}

function integer(value: unknown, label: string): number {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return parsed;
}

function canonicalIso(value: string, label: string): string {
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString() !== value) {
    throw new TypeError(`${label} must be canonical ISO UTC.`);
  }
  return value;
}
