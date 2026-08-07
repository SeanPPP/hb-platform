import type {
  InstallmentSnapshot,
  InstallmentSnapshotRepositoryPort,
} from "../contracts/installments";

import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "./types";

export const INSTALLMENT_SENSITIVE_PAYLOAD_REVISION = 1;

type InstallmentSnapshotRow = Readonly<{
  store_code: unknown;
  installment_guid: unknown;
  created_at_iso: unknown;
  updated_at_iso: unknown;
  total_cents: unknown;
  down_payment_cents: unknown;
  paid_cents: unknown;
  balance_cents: unknown;
  status: unknown;
  encrypted_sensitive_revision: unknown;
  sensitive_payload_ciphertext: unknown;
}>;

type InstallmentSensitivePayloadV1 = Readonly<{
  revision: 1;
  storeCode: string;
  installmentGuid: string;
  installmentNumber: string;
  deviceCode: string;
  cashierName: string;
  customerName: string;
  customerPhone: string | null;
  note: string | null;
}>;

type PreparedInstallmentSnapshot = Readonly<{
  snapshot: InstallmentSnapshot;
  ciphertext: Uint8Array;
}>;

type PreparedInstallmentSnapshotBatch = Readonly<{
  items: readonly PreparedInstallmentSnapshot[];
}>;

type SnapshotRepositoryContext = Readonly<{
  connection: SqliteConnectionPort;
  encryptor: SensitivePayloadEncryptor;
}>;

const repositoryContexts = new WeakMap<
  SqliteInstallmentSnapshotRepository,
  SnapshotRepositoryContext
>();
const preparedBatchOwners = new WeakMap<
  PreparedInstallmentSnapshotBatch,
  SqliteInstallmentSnapshotRepository
>();

export class SqliteInstallmentSnapshotRepository
  implements InstallmentSnapshotRepositoryPort
{
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
  ) {
    repositoryContexts.set(
      this,
      Object.freeze({ connection, encryptor }),
    );
  }

  public async replaceForStore(
    storeCode: string,
    snapshots: readonly InstallmentSnapshot[],
  ): Promise<void> {
    const store = strictIdentity(storeCode, "store code", 128);
    if (!Array.isArray(snapshots)) {
      throw new TypeError("Installment snapshots are invalid.");
    }

    // 校验与加密全部先于事务，避免单条坏响应删除门店上一份可用快照。
    const prepared: PreparedInstallmentSnapshot[] = [];
    const seenGuids = new Set<string>();
    for (const candidate of snapshots) {
      const snapshot = validateSnapshot(candidate, store);
      if (seenGuids.has(snapshot.installmentGuid)) {
        throw new TypeError("Installment snapshot GUID is duplicate.");
      }
      seenGuids.add(snapshot.installmentGuid);
      const ciphertext = await this.encryptor.encrypt(
        JSON.stringify(sensitivePayload(snapshot)),
      );
      if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
        throw new Error("Installment sensitive payload encryption failed.");
      }
      prepared.push({ snapshot, ciphertext });
    }

    await this.connection.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        "DELETE FROM installment_snapshots WHERE store_code = ?",
        [store],
      );
      for (const item of prepared) {
        const snapshot = item.snapshot;
        await transaction.run(
          `INSERT INTO installment_snapshots (
            store_code, installment_guid, created_at_iso, updated_at_iso,
            total_cents, down_payment_cents, paid_cents, balance_cents,
            status, encrypted_sensitive_revision,
            sensitive_payload_ciphertext
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
          [
            snapshot.storeCode,
            snapshot.installmentGuid,
            snapshot.createdAtIso,
            snapshot.updatedAtIso,
            snapshot.totalCents,
            snapshot.downPaymentCents,
            snapshot.paidCents,
            snapshot.balanceCents,
            snapshot.status,
            snapshot.encryptedSensitiveRevision,
            item.ciphertext,
          ],
        );
      }
    });
  }

  /**
   * 在线筛选页和 mutation 回包都不是全量目录；这里只更新返回的 GUID，
   * 未返回的历史快照必须继续可供离线浏览。
   */
  public async upsertForStore(
    storeCode: string,
    snapshots: readonly InstallmentSnapshot[],
  ): Promise<void> {
    const prepared = await prepareUpsertBatch(this, storeCode, snapshots);

    await this.connection.withExclusiveTransaction(async (transaction) => {
      await upsertPreparedBatch(this, transaction, prepared);
    });
  }

  public async listForStore(
    storeCode: string,
    limit: number,
    offset: number,
  ): Promise<readonly InstallmentSnapshot[]> {
    const store = strictIdentity(storeCode, "store code", 128);
    const pageLimit = strictPageLimit(limit);
    const pageOffset = strictPageOffset(offset);
    const rows = await this.connection.getAll<InstallmentSnapshotRow>(
      `${selectSnapshotRows()}
       WHERE store_code = ?
       ORDER BY created_at_iso DESC, installment_guid ASC
       LIMIT ? OFFSET ?`,
      [store, pageLimit, pageOffset],
    );
    return Object.freeze(
      await Promise.all(
        rows.map((row) => readSnapshot(row, this.encryptor)),
      ),
    );
  }

  public async get(
    storeCode: string,
    installmentGuid: string,
  ): Promise<InstallmentSnapshot | null> {
    const store = strictIdentity(storeCode, "store code", 128);
    const guid = strictGuid(installmentGuid);
    const row = await this.connection.getFirst<InstallmentSnapshotRow>(
      `${selectSnapshotRows()}
       WHERE store_code = ? AND installment_guid = ?`,
      [store, guid],
    );
    return row === null ? null : readSnapshot(row, this.encryptor);
  }
}

/**
 * committed repayment 唯一可见的协调入口：先确认仓储与 action store 使用同一
 * connection/encryptor，再在事务外校验并加密。prepared batch 保留在模块闭包中，
 * 调用方既不能伪造，也不能把其他仓储创建的 batch 换入。
 */
export async function prepareCommittedInstallmentSnapshotUpsert(
  repository: SqliteInstallmentSnapshotRepository,
  expectedConnection: SqliteConnectionPort,
  expectedEncryptor: SensitivePayloadEncryptor,
  storeCode: string,
  snapshot: InstallmentSnapshot,
): Promise<
  Readonly<{
    installmentGuid: string;
    upsertInTransaction(
      transaction: SqliteConnectionPort,
    ): Promise<void>;
    matchesPersistedInTransaction(
      transaction: SqliteConnectionPort,
    ): Promise<boolean>;
  }>
> {
  const context = requireRepositoryContext(repository);
  if (
    context.connection !== expectedConnection ||
    context.encryptor !== expectedEncryptor
  ) {
    throw new TypeError(
      "Installment snapshot repository context mismatch.",
    );
  }

  const prepared = await prepareUpsertBatch(repository, storeCode, [snapshot]);
  const item = prepared.items[0];
  if (prepared.items.length !== 1 || item === undefined) {
    throw new Error("Committed repayment snapshot preparation failed.");
  }

  return Object.freeze({
    installmentGuid: item.snapshot.installmentGuid,
    upsertInTransaction: async (transaction: SqliteConnectionPort) => {
      await upsertPreparedBatch(repository, transaction, prepared);
    },
    matchesPersistedInTransaction: async (
      transaction: SqliteConnectionPort,
    ) => {
      requirePreparedBatchOwner(repository, prepared);
      const row = await transaction.getFirst<InstallmentSnapshotRow>(
        `${selectSnapshotRows()}
         WHERE store_code = ? AND installment_guid = ?`,
        [item.snapshot.storeCode, item.snapshot.installmentGuid],
      );
      if (row === null) return false;
      const persisted = await readSnapshot(row, context.encryptor);
      return snapshotsEqual(persisted, item.snapshot);
    },
  });
}

async function prepareUpsertBatch(
  repository: SqliteInstallmentSnapshotRepository,
  storeCode: string,
  snapshots: readonly InstallmentSnapshot[],
): Promise<PreparedInstallmentSnapshotBatch> {
  const context = requireRepositoryContext(repository);
  const store = strictIdentity(storeCode, "store code", 128);
  if (!Array.isArray(snapshots)) {
    throw new TypeError("Installment snapshots are invalid.");
  }

  const items: PreparedInstallmentSnapshot[] = [];
  const seenGuids = new Set<string>();
  for (const candidate of snapshots) {
    const snapshot = validateSnapshot(candidate, store);
    if (seenGuids.has(snapshot.installmentGuid)) {
      throw new TypeError("Installment snapshot GUID is duplicate.");
    }
    seenGuids.add(snapshot.installmentGuid);
    const ciphertext = await context.encryptor.encrypt(
      JSON.stringify(sensitivePayload(snapshot)),
    );
    if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
      throw new Error("Installment sensitive payload encryption failed.");
    }
    items.push(Object.freeze({ snapshot, ciphertext }));
  }

  const prepared = Object.freeze({ items: Object.freeze(items) });
  preparedBatchOwners.set(prepared, repository);
  return prepared;
}

async function upsertPreparedBatch(
  repository: SqliteInstallmentSnapshotRepository,
  transaction: SqliteConnectionPort,
  prepared: PreparedInstallmentSnapshotBatch,
): Promise<void> {
  requirePreparedBatchOwner(repository, prepared);
  for (const item of prepared.items) {
    const snapshot = item.snapshot;
    await transaction.run(
      `INSERT INTO installment_snapshots (
        store_code, installment_guid, created_at_iso, updated_at_iso,
        total_cents, down_payment_cents, paid_cents, balance_cents,
        status, encrypted_sensitive_revision,
        sensitive_payload_ciphertext
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(store_code, installment_guid) DO UPDATE SET
        created_at_iso = excluded.created_at_iso,
        updated_at_iso = excluded.updated_at_iso,
        total_cents = excluded.total_cents,
        down_payment_cents = excluded.down_payment_cents,
        paid_cents = excluded.paid_cents,
        balance_cents = excluded.balance_cents,
        status = excluded.status,
        encrypted_sensitive_revision =
          excluded.encrypted_sensitive_revision,
        sensitive_payload_ciphertext =
          excluded.sensitive_payload_ciphertext`,
      [
        snapshot.storeCode,
        snapshot.installmentGuid,
        snapshot.createdAtIso,
        snapshot.updatedAtIso,
        snapshot.totalCents,
        snapshot.downPaymentCents,
        snapshot.paidCents,
        snapshot.balanceCents,
        snapshot.status,
        snapshot.encryptedSensitiveRevision,
        item.ciphertext,
      ],
    );
  }
}

function requireRepositoryContext(
  repository: SqliteInstallmentSnapshotRepository,
): SnapshotRepositoryContext {
  const context = repositoryContexts.get(repository);
  if (context === undefined) {
    throw new TypeError(
      "Installment snapshot repository context mismatch.",
    );
  }
  return context;
}

function requirePreparedBatchOwner(
  repository: SqliteInstallmentSnapshotRepository,
  prepared: PreparedInstallmentSnapshotBatch,
): void {
  if (preparedBatchOwners.get(prepared) !== repository) {
    throw new TypeError(
      "Installment snapshot prepared batch owner mismatch.",
    );
  }
}

async function readSnapshot(
  row: InstallmentSnapshotRow,
  encryptor: SensitivePayloadEncryptor,
): Promise<InstallmentSnapshot> {
  const storeCode = persistedIdentity(row.store_code, "store code", 128);
  const installmentGuid = persistedGuid(row.installment_guid);
  const revision = persistedInteger(
    row.encrypted_sensitive_revision,
    "sensitive revision",
  );
  if (revision !== INSTALLMENT_SENSITIVE_PAYLOAD_REVISION) {
    throw new Error("Persisted installment sensitive revision is invalid.");
  }
  const ciphertext = persistedBytes(
    row.sensitive_payload_ciphertext,
    "sensitive payload",
  );
  let payload: InstallmentSensitivePayloadV1;
  try {
    payload = parseSensitivePayload(
      JSON.parse(await encryptor.decrypt(ciphertext)),
    );
  } catch (error) {
    if (
      error instanceof Error &&
      error.message === "Installment sensitive payload scope mismatch."
    ) {
      throw error;
    }
    throw new Error("Persisted installment sensitive payload is invalid.");
  }
  if (
    payload.revision !== revision ||
    payload.storeCode !== storeCode ||
    payload.installmentGuid !== installmentGuid
  ) {
    throw new Error("Installment sensitive payload scope mismatch.");
  }

  return Object.freeze({
    installmentGuid,
    installmentNumber: payload.installmentNumber,
    storeCode,
    deviceCode: payload.deviceCode,
    cashierName: payload.cashierName,
    customerName: payload.customerName,
    customerPhone: payload.customerPhone,
    createdAtIso: persistedCanonicalIso(row.created_at_iso, "created at"),
    totalCents: persistedMoney(row.total_cents, "total"),
    downPaymentCents: persistedMoney(
      row.down_payment_cents,
      "down payment",
    ),
    paidCents: persistedMoney(row.paid_cents, "paid"),
    balanceCents: persistedMoney(row.balance_cents, "balance"),
    status: persistedStatus(row.status),
    updatedAtIso: persistedCanonicalIso(row.updated_at_iso, "updated at"),
    note: payload.note,
    encryptedSensitiveRevision: revision,
  });
}

function snapshotsEqual(
  left: InstallmentSnapshot,
  right: InstallmentSnapshot,
): boolean {
  return (
    left.installmentGuid === right.installmentGuid &&
    left.installmentNumber === right.installmentNumber &&
    left.storeCode === right.storeCode &&
    left.deviceCode === right.deviceCode &&
    left.cashierName === right.cashierName &&
    left.customerName === right.customerName &&
    left.customerPhone === right.customerPhone &&
    left.createdAtIso === right.createdAtIso &&
    left.totalCents === right.totalCents &&
    left.downPaymentCents === right.downPaymentCents &&
    left.paidCents === right.paidCents &&
    left.balanceCents === right.balanceCents &&
    left.status === right.status &&
    left.updatedAtIso === right.updatedAtIso &&
    left.note === right.note &&
    left.encryptedSensitiveRevision === right.encryptedSensitiveRevision
  );
}

function sensitivePayload(
  snapshot: InstallmentSnapshot,
): InstallmentSensitivePayloadV1 {
  return {
    revision: INSTALLMENT_SENSITIVE_PAYLOAD_REVISION,
    storeCode: snapshot.storeCode,
    installmentGuid: snapshot.installmentGuid,
    installmentNumber: snapshot.installmentNumber,
    deviceCode: snapshot.deviceCode,
    cashierName: snapshot.cashierName,
    customerName: snapshot.customerName,
    customerPhone: snapshot.customerPhone,
    note: snapshot.note,
  };
}

function validateSnapshot(
  value: unknown,
  expectedStore: string,
): InstallmentSnapshot {
  if (!isRecord(value)) {
    throw new TypeError("Installment snapshot is invalid.");
  }
  const storeCode = strictIdentity(value.storeCode, "store code", 128);
  if (storeCode !== expectedStore) {
    throw new TypeError("Installment snapshot store scope is invalid.");
  }
  const revision = strictInteger(
    value.encryptedSensitiveRevision,
    "sensitive revision",
  );
  if (revision !== INSTALLMENT_SENSITIVE_PAYLOAD_REVISION) {
    throw new TypeError("Installment sensitive revision is invalid.");
  }
  return Object.freeze({
    installmentGuid: strictGuid(value.installmentGuid),
    installmentNumber: strictDisplayText(
      value.installmentNumber,
      "number",
      128,
    ),
    storeCode,
    deviceCode: strictIdentity(value.deviceCode, "device code", 128),
    cashierName: strictDisplayText(value.cashierName, "cashier name", 256),
    customerName: strictDisplayText(
      value.customerName,
      "customer name",
      256,
    ),
    customerPhone: optionalDisplayText(
      value.customerPhone,
      "customer phone",
      128,
    ),
    createdAtIso: strictCanonicalIso(value.createdAtIso, "created at"),
    totalCents: strictMoney(value.totalCents, "total"),
    downPaymentCents: strictMoney(
      value.downPaymentCents,
      "down payment",
    ),
    paidCents: strictMoney(value.paidCents, "paid"),
    balanceCents: strictMoney(value.balanceCents, "balance"),
    status: strictStatus(value.status),
    updatedAtIso: strictCanonicalIso(value.updatedAtIso, "updated at"),
    note: optionalDisplayText(value.note, "note", 2_000),
    encryptedSensitiveRevision: revision,
  });
}

function parseSensitivePayload(
  value: unknown,
): InstallmentSensitivePayloadV1 {
  if (!isRecord(value) || value.revision !== 1) {
    throw new Error("Invalid installment sensitive payload.");
  }
  return {
    revision: INSTALLMENT_SENSITIVE_PAYLOAD_REVISION,
    storeCode: persistedIdentity(value.storeCode, "payload store code", 128),
    installmentGuid: persistedGuid(value.installmentGuid),
    installmentNumber: persistedDisplayText(
      value.installmentNumber,
      "payload number",
      128,
    ),
    deviceCode: persistedIdentity(
      value.deviceCode,
      "payload device code",
      128,
    ),
    cashierName: persistedDisplayText(
      value.cashierName,
      "payload cashier name",
      256,
    ),
    customerName: persistedDisplayText(
      value.customerName,
      "payload customer name",
      256,
    ),
    customerPhone: persistedOptionalDisplayText(
      value.customerPhone,
      "payload customer phone",
      128,
    ),
    note: persistedOptionalDisplayText(value.note, "payload note", 2_000),
  };
}

function selectSnapshotRows(): string {
  return `SELECT
    store_code, installment_guid, created_at_iso, updated_at_iso,
    total_cents, down_payment_cents, paid_cents, balance_cents,
    status, encrypted_sensitive_revision, sensitive_payload_ciphertext
    FROM installment_snapshots`;
}

function strictPageLimit(value: unknown): number {
  const limit = strictInteger(value, "page limit");
  if (limit <= 0 || limit > 5_000) {
    throw new TypeError("Installment page limit is invalid.");
  }
  return limit;
}

function strictPageOffset(value: unknown): number {
  const offset = strictInteger(value, "page offset");
  if (offset < 0) {
    throw new TypeError("Installment page offset is invalid.");
  }
  return offset;
}

function strictMoney(value: unknown, label: string): number {
  const amount = strictInteger(value, `${label} cents`);
  if (amount < 0) {
    throw new TypeError(`Installment ${label} cents are invalid.`);
  }
  return amount;
}

function strictInteger(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw new TypeError(`Installment ${label} is invalid.`);
  }
  return value;
}

function strictStatus(value: unknown): InstallmentSnapshot["status"] {
  if (
    value !== "Active" &&
    value !== "PaidOff" &&
    value !== "PickedUp" &&
    value !== "Cancelled"
  ) {
    throw new TypeError("Installment status is invalid.");
  }
  return value;
}

function strictGuid(value: unknown): string {
  if (
    typeof value !== "string" ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      value,
    )
  ) {
    throw new TypeError("Installment GUID is invalid.");
  }
  return value;
}

function strictIdentity(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value !== value.trim() ||
    value.length === 0 ||
    value.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`Installment ${label} is invalid.`);
  }
  return value;
}

function strictDisplayText(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    value.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`Installment ${label} is invalid.`);
  }
  return value;
}

function optionalDisplayText(
  value: unknown,
  label: string,
  maxLength: number,
): string | null {
  if (value === null) return null;
  return strictDisplayText(value, label, maxLength);
}

function strictCanonicalIso(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    !Number.isFinite(Date.parse(value)) ||
    new Date(value).toISOString() !== value
  ) {
    throw new TypeError(`Installment ${label} must be canonical ISO UTC.`);
  }
  return value;
}

function persistedIdentity(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  try {
    return strictIdentity(value, label, maxLength);
  } catch {
    throw new Error(`Persisted installment ${label} is invalid.`);
  }
}

function persistedDisplayText(
  value: unknown,
  label: string,
  maxLength: number,
): string {
  try {
    return strictDisplayText(value, label, maxLength);
  } catch {
    throw new Error(`Persisted installment ${label} is invalid.`);
  }
}

function persistedOptionalDisplayText(
  value: unknown,
  label: string,
  maxLength: number,
): string | null {
  if (value === null) return null;
  return persistedDisplayText(value, label, maxLength);
}

function persistedGuid(value: unknown): string {
  try {
    return strictGuid(value);
  } catch {
    throw new Error("Persisted installment GUID is invalid.");
  }
}

function persistedInteger(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw new Error(`Persisted installment ${label} is invalid.`);
  }
  return value;
}

function persistedMoney(value: unknown, label: string): number {
  const amount = persistedInteger(value, `${label} cents`);
  if (amount < 0) {
    throw new Error(`Persisted installment ${label} cents are invalid.`);
  }
  return amount;
}

function persistedStatus(value: unknown): InstallmentSnapshot["status"] {
  try {
    return strictStatus(value);
  } catch {
    throw new Error("Persisted installment status is invalid.");
  }
}

function persistedCanonicalIso(value: unknown, label: string): string {
  try {
    return strictCanonicalIso(value, label);
  } catch {
    throw new Error(`Persisted installment ${label} is invalid.`);
  }
}

function persistedBytes(value: unknown, label: string): Uint8Array {
  if (!(value instanceof Uint8Array) || value.length === 0) {
    throw new Error(`Persisted installment ${label} is invalid.`);
  }
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
