import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type {
  AttendanceQrCachePort,
  AttendanceQrProvisioning,
} from "@/features/attendance-audit/attendance-qr-controller";
import type {
  EmergencyPublicKeyCachePort,
  EmergencyTrustedTimeAnchor,
  EmergencyTrustedTimePort,
} from "@/features/attendance-audit/emergency-login-security";
import type {
  EmergencyPublicKey,
  EmergencyPublicKeyPackage,
} from "@hb/pos-api-client/features/attendance-audit/hbpos-attendance-security-api";

export type {
  AttendanceQrCachePort,
  AttendanceQrProvisioning,
  EmergencyPublicKeyCachePort,
  EmergencyPublicKeyPackage,
  EmergencyTrustedTimeAnchor,
  EmergencyTrustedTimePort,
};

export const ATTENDANCE_SECURITY_PAYLOAD_REVISION = 1;

export type AttendanceSecurityTerminalScope = Readonly<{
  apiPartition: string;
  storeCode: string;
  deviceCode: string;
  hardwareId: string;
  authorizationMarker: string;
}>;

type PreparedTerminalScope = Readonly<{
  terminal: AttendanceSecurityTerminalScope;
  hash: string;
}>;

type AttendanceQrCacheRow = Readonly<{
  scope_hash: unknown;
  api_partition: unknown;
  store_code: unknown;
  device_code: unknown;
  payload_revision: unknown;
  provisioning_ciphertext: unknown;
}>;

type EmergencyPublicKeyPackageRow = Readonly<{
  scope_hash: unknown;
  api_partition: unknown;
  store_code: unknown;
  device_code: unknown;
  package_version: unknown;
  generated_at_epoch_ms: unknown;
  active_key_id: unknown;
  keys_json: unknown;
}>;

type EmergencyTrustedTimeRow = Readonly<{
  scope_hash: unknown;
  api_partition: unknown;
  store_code: unknown;
  device_code: unknown;
  payload_revision: unknown;
  trusted_time_ciphertext: unknown;
}>;

type AttendanceQrEnvelopeV1 = Readonly<{
  format: "hb-pos-attendance-qr-cache-v1";
  scope: AttendanceSecurityTerminalScope;
  provisioning: AttendanceQrProvisioning;
}>;

type EmergencyTrustedTimeEnvelopeV1 = Readonly<{
  format: "hb-pos-emergency-trusted-time-v1";
  scope: AttendanceSecurityTerminalScope;
  highWaterEpochMs: number;
}>;

type EmergencyTrustedTimeEnvelopeV2 = Readonly<{
  format: "hb-pos-emergency-trusted-time-v2";
  scope: AttendanceSecurityTerminalScope;
  serverEpochMs: number;
  systemUptimeMs: number;
}>;

type PersistedEmergencyTrustedTime =
  | Readonly<{
      kind: "legacy-v1";
      highWaterEpochMs: number;
    }>
  | Readonly<{
      kind: "anchor-v2";
      anchor: EmergencyTrustedTimeAnchor;
    }>;

/**
 * 业务组合根只取得三个既有 feature Port；裸连接、scope hash 与密文格式均留在 DB 层。
 */
export class SqliteAttendanceSecurityFacade {
  public readonly attendanceQrCache: AttendanceQrCachePort;
  public readonly emergencyPublicKeyCache: EmergencyPublicKeyCachePort;
  public readonly emergencyTrustedTime: EmergencyTrustedTimePort;

  public constructor(
    connection: SqliteConnectionPort,
    encryptor: SensitivePayloadEncryptor,
    terminal: AttendanceSecurityTerminalScope,
    nowIso: () => string,
  ) {
    const scope = prepareTerminalScope(terminal);
    this.attendanceQrCache = new SqliteAttendanceQrCache(
      connection,
      encryptor,
      scope,
      nowIso,
    );
    this.emergencyPublicKeyCache =
      new SqliteEmergencyPublicKeyCache(connection, scope, nowIso);
    this.emergencyTrustedTime = new SqliteEmergencyTrustedTime(
      connection,
      encryptor,
      scope,
      nowIso,
    );
    Object.freeze(this);
  }
}

class SqliteAttendanceQrCache implements AttendanceQrCachePort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly scope: PreparedTerminalScope,
    private readonly nowIso: () => string,
  ) {}

  public async load(): Promise<AttendanceQrProvisioning | null> {
    const row = await this.connection.getFirst<AttendanceQrCacheRow>(
      `${selectAttendanceQrCache()}
       WHERE scope_hash = ?`,
      [this.scope.hash],
    );
    if (row === null) return null;
    assertPersistedScope(row, this.scope, "attendance cache");
    const revision = persistedSafeInteger(
      row.payload_revision,
      "attendance cache payload revision",
    );
    if (revision !== ATTENDANCE_SECURITY_PAYLOAD_REVISION) {
      throw new Error("Persisted attendance cache payload revision is invalid.");
    }
    const ciphertext = persistedCiphertext(
      row.provisioning_ciphertext,
      "attendance cache",
    );
    const envelope = await decryptAttendanceEnvelope(
      this.encryptor,
      ciphertext,
    );
    if (!sameTerminalScope(envelope.scope, this.scope.terminal)) {
      return null;
    }
    return envelope.provisioning;
  }

  public async replace(value: AttendanceQrProvisioning): Promise<void> {
    const provisioning = normalizeAttendanceProvisioning(
      value,
      this.scope.terminal,
    );
    const envelope: AttendanceQrEnvelopeV1 = Object.freeze({
      format: "hb-pos-attendance-qr-cache-v1",
      scope: this.scope.terminal,
      provisioning,
    });
    const ciphertext = await encryptPayload(
      this.encryptor,
      JSON.stringify(envelope),
      "attendance cache",
    );
    const updatedAtIso = strictCanonicalIso(
      this.nowIso(),
      "attendance cache update time",
    );

    await this.connection.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        `INSERT INTO attendance_qr_provisioning_cache (
          scope_hash, api_partition, store_code, device_code,
          payload_revision, provisioning_ciphertext, updated_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(scope_hash) DO UPDATE SET
          api_partition = excluded.api_partition,
          store_code = excluded.store_code,
          device_code = excluded.device_code,
          payload_revision = excluded.payload_revision,
          provisioning_ciphertext = excluded.provisioning_ciphertext,
          updated_at_iso = excluded.updated_at_iso`,
        [
          this.scope.hash,
          this.scope.terminal.apiPartition,
          this.scope.terminal.storeCode,
          this.scope.terminal.deviceCode,
          ATTENDANCE_SECURITY_PAYLOAD_REVISION,
          ciphertext,
          updatedAtIso,
        ],
      );
    });
  }

  public async clear(): Promise<void> {
    await this.connection.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        "DELETE FROM attendance_qr_provisioning_cache WHERE scope_hash = ?",
        [this.scope.hash],
      );
    });
  }
}

class SqliteEmergencyPublicKeyCache
  implements EmergencyPublicKeyCachePort
{
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly scope: PreparedTerminalScope,
    private readonly nowIso: () => string,
  ) {}

  public read(): Promise<EmergencyPublicKeyPackage | null> {
    return readPublicKeyPackage(this.connection, this.scope);
  }

  public async replace(value: EmergencyPublicKeyPackage): Promise<void> {
    const packageValue = normalizePublicKeyPackage(value);
    const keysJson = JSON.stringify(packageValue.keys);
    const updatedAtIso = strictCanonicalIso(
      this.nowIso(),
      "emergency public key package update time",
    );

    await this.connection.withExclusiveTransaction(async (transaction) => {
      const current = await readPublicKeyPackage(transaction, this.scope);
      if (current !== null) {
        if (packageValue.version < current.version) {
          throw new Error(
            "Emergency public key package version rollback is forbidden.",
          );
        }
        if (packageValue.version === current.version) {
          if (
            JSON.stringify(packageValue) !== JSON.stringify(current)
          ) {
            throw new Error(
              "Emergency public key package version conflicts with cached content.",
            );
          }
          return;
        }
      }

      await transaction.run(
        `INSERT INTO emergency_public_key_package_cache (
          scope_hash, api_partition, store_code, device_code,
          package_version, generated_at_epoch_ms, active_key_id,
          keys_json, updated_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(scope_hash) DO UPDATE SET
          api_partition = excluded.api_partition,
          store_code = excluded.store_code,
          device_code = excluded.device_code,
          package_version = excluded.package_version,
          generated_at_epoch_ms = excluded.generated_at_epoch_ms,
          active_key_id = excluded.active_key_id,
          keys_json = excluded.keys_json,
          updated_at_iso = excluded.updated_at_iso`,
        [
          this.scope.hash,
          this.scope.terminal.apiPartition,
          this.scope.terminal.storeCode,
          this.scope.terminal.deviceCode,
          packageValue.version,
          packageValue.generatedAtEpochMs,
          packageValue.activeKeyId,
          keysJson,
          updatedAtIso,
        ],
      );
    });
  }
}

class SqliteEmergencyTrustedTime implements EmergencyTrustedTimePort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly scope: PreparedTerminalScope,
    private readonly nowIso: () => string,
  ) {}

  public async readAnchor(): Promise<EmergencyTrustedTimeAnchor | null> {
    const persisted = await readTrustedTime(
      this.connection,
      this.encryptor,
      this.scope,
    );
    return persisted?.kind === "anchor-v2"
      ? persisted.anchor
      : null;
  }

  public async replaceAnchor(
    value: EmergencyTrustedTimeAnchor,
  ): Promise<void> {
    const anchor = normalizeTrustedTimeAnchor(
      value,
    );
    const envelope: EmergencyTrustedTimeEnvelopeV2 = Object.freeze({
      format: "hb-pos-emergency-trusted-time-v2",
      scope: this.scope.terminal,
      serverEpochMs: anchor.serverEpochMs,
      systemUptimeMs: anchor.systemUptimeMs,
    });
    // 加密先于事务；失败时不获取写锁，也不可能污染旧锚点。
    const ciphertext = await encryptPayload(
      this.encryptor,
      JSON.stringify(envelope),
      "emergency trusted time cache",
    );
    const updatedAtIso = strictCanonicalIso(
      this.nowIso(),
      "emergency trusted time update time",
    );

    await this.connection.withExclusiveTransaction(async (transaction) => {
      const current = await readTrustedTime(
        transaction,
        this.encryptor,
        this.scope,
      );
      assertTrustedTimeReplacement(current, anchor);
      if (
        current?.kind === "anchor-v2" &&
        sameTrustedTimeAnchor(current.anchor, anchor)
      ) {
        return;
      }

      await transaction.run(
        `INSERT INTO emergency_trusted_time_cache (
          scope_hash, api_partition, store_code, device_code,
          payload_revision, trusted_time_ciphertext, updated_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(scope_hash) DO UPDATE SET
          api_partition = excluded.api_partition,
          store_code = excluded.store_code,
          device_code = excluded.device_code,
          payload_revision = excluded.payload_revision,
          trusted_time_ciphertext = excluded.trusted_time_ciphertext,
          updated_at_iso = excluded.updated_at_iso`,
        [
          this.scope.hash,
          this.scope.terminal.apiPartition,
          this.scope.terminal.storeCode,
          this.scope.terminal.deviceCode,
          ATTENDANCE_SECURITY_PAYLOAD_REVISION,
          ciphertext,
          updatedAtIso,
        ],
      );
    });
  }
}

async function readPublicKeyPackage(
  connection: SqliteConnectionPort,
  scope: PreparedTerminalScope,
): Promise<EmergencyPublicKeyPackage | null> {
  const row = await connection.getFirst<EmergencyPublicKeyPackageRow>(
    `SELECT
      scope_hash, api_partition, store_code, device_code,
      package_version, generated_at_epoch_ms, active_key_id, keys_json
     FROM emergency_public_key_package_cache
     WHERE scope_hash = ?`,
    [scope.hash],
  );
  if (row === null) return null;
  assertPersistedScope(row, scope, "emergency public key package");

  try {
    if (typeof row.keys_json !== "string") {
      throw new Error("keys JSON is not text");
    }
    const packageValue = normalizePublicKeyPackage({
      version: persistedSafeInteger(
        row.package_version,
        "emergency public key package version",
      ),
      generatedAtEpochMs: persistedSafeInteger(
        row.generated_at_epoch_ms,
        "emergency public key package generated time",
      ),
      activeKeyId:
        row.active_key_id === null
          ? null
          : strictEmergencyKid(
              row.active_key_id,
              "emergency public key package active key",
            ),
      keys: JSON.parse(row.keys_json) as unknown,
    });
    if (row.keys_json !== JSON.stringify(packageValue.keys)) {
      throw new Error("keys JSON is not canonical");
    }
    return packageValue;
  } catch {
    throw new Error("Persisted emergency public key package JSON is invalid.");
  }
}

async function readTrustedTime(
  connection: SqliteConnectionPort,
  encryptor: SensitivePayloadEncryptor,
  scope: PreparedTerminalScope,
): Promise<PersistedEmergencyTrustedTime | null> {
  const row = await connection.getFirst<EmergencyTrustedTimeRow>(
    `SELECT
      scope_hash, api_partition, store_code, device_code,
      payload_revision, trusted_time_ciphertext
     FROM emergency_trusted_time_cache
     WHERE scope_hash = ?`,
    [scope.hash],
  );
  if (row === null) return null;
  assertPersistedScope(row, scope, "emergency trusted time cache");
  const revision = persistedSafeInteger(
    row.payload_revision,
    "emergency trusted time payload revision",
  );
  if (revision !== ATTENDANCE_SECURITY_PAYLOAD_REVISION) {
    throw new Error(
      "Persisted emergency trusted time payload revision is invalid.",
    );
  }
  const ciphertext = persistedCiphertext(
    row.trusted_time_ciphertext,
    "emergency trusted time cache",
  );
  const envelope = await decryptTrustedTimeEnvelope(
    encryptor,
    ciphertext,
  );
  if (!sameTerminalScope(envelope.scope, scope.terminal)) {
    throw new Error(
      "Persisted emergency trusted time cache scope is invalid.",
    );
  }
  if (envelope.format === "hb-pos-emergency-trusted-time-v1") {
    return Object.freeze({
      kind: "legacy-v1",
      highWaterEpochMs: envelope.highWaterEpochMs,
    });
  }
  return Object.freeze({
    kind: "anchor-v2",
    anchor: Object.freeze({
      serverEpochMs: envelope.serverEpochMs,
      systemUptimeMs: envelope.systemUptimeMs,
    }),
  });
}

async function decryptAttendanceEnvelope(
  encryptor: SensitivePayloadEncryptor,
  ciphertext: Uint8Array,
): Promise<AttendanceQrEnvelopeV1> {
  try {
    const parsed = JSON.parse(await encryptor.decrypt(ciphertext)) as unknown;
    if (
      !hasExactKeys(parsed, ["format", "scope", "provisioning"]) ||
      parsed.format !== "hb-pos-attendance-qr-cache-v1"
    ) {
      throw new Error("invalid envelope");
    }
    const scope = normalizeTerminalScope(parsed.scope);
    return Object.freeze({
      format: "hb-pos-attendance-qr-cache-v1",
      scope,
      provisioning: normalizeAttendanceProvisioning(
        parsed.provisioning,
        scope,
      ),
    });
  } catch {
    throw new Error("Persisted attendance cache ciphertext is invalid.");
  }
}

async function decryptTrustedTimeEnvelope(
  encryptor: SensitivePayloadEncryptor,
  ciphertext: Uint8Array,
): Promise<
  EmergencyTrustedTimeEnvelopeV1 | EmergencyTrustedTimeEnvelopeV2
> {
  try {
    const parsed = JSON.parse(await encryptor.decrypt(ciphertext)) as unknown;
    if (
      hasExactKeys(parsed, [
        "format",
        "scope",
        "highWaterEpochMs",
      ]) &&
      parsed.format === "hb-pos-emergency-trusted-time-v1"
    ) {
      return Object.freeze({
        format: "hb-pos-emergency-trusted-time-v1",
        scope: normalizeTerminalScope(parsed.scope),
        highWaterEpochMs: strictNonnegativeSafeInteger(
          parsed.highWaterEpochMs,
          "emergency trusted time high water",
        ),
      });
    }
    if (
      !hasExactKeys(parsed, [
        "format",
        "scope",
        "serverEpochMs",
        "systemUptimeMs",
      ]) ||
      parsed.format !== "hb-pos-emergency-trusted-time-v2"
    ) {
      throw new Error("invalid anchor envelope");
    }
    const anchor = normalizeTrustedTimeAnchor({
      serverEpochMs: parsed.serverEpochMs,
      systemUptimeMs: parsed.systemUptimeMs,
    });
    return Object.freeze({
      format: "hb-pos-emergency-trusted-time-v2",
      scope: normalizeTerminalScope(parsed.scope),
      serverEpochMs: anchor.serverEpochMs,
      systemUptimeMs: anchor.systemUptimeMs,
    });
  } catch {
    throw new Error(
      "Persisted emergency trusted time cache ciphertext is invalid.",
    );
  }
}

function normalizeTrustedTimeAnchor(
  value: unknown,
): EmergencyTrustedTimeAnchor {
  if (
    !hasExactKeys(value, ["serverEpochMs", "systemUptimeMs"])
  ) {
    throw new TypeError("Emergency trusted time anchor is invalid.");
  }
  return Object.freeze({
    serverEpochMs: strictNonnegativeSafeInteger(
      value.serverEpochMs,
      "emergency trusted time anchor server time",
    ),
    systemUptimeMs: strictNonnegativeSafeInteger(
      value.systemUptimeMs,
      "emergency trusted time anchor system uptime",
    ),
  });
}

function assertTrustedTimeReplacement(
  current: PersistedEmergencyTrustedTime | null,
  next: EmergencyTrustedTimeAnchor,
): void {
  if (current === null) return;
  const currentServerEpochMs =
    current.kind === "legacy-v1"
      ? current.highWaterEpochMs
      : current.anchor.serverEpochMs;
  if (next.serverEpochMs < currentServerEpochMs) {
    throw new Error(
      "Emergency trusted time server rollback is forbidden.",
    );
  }
  if (current.kind === "legacy-v1") return;
  if (sameTrustedTimeAnchor(current.anchor, next)) return;

  if (next.systemUptimeMs < current.anchor.systemUptimeMs) {
    // uptime 下降只能表示新的 iOS boot，必须由严格前进的在线服务端时间重建锚点。
    if (next.serverEpochMs <= current.anchor.serverEpochMs) {
      throw new Error(
        "Emergency trusted time uptime rollback is forbidden.",
      );
    }
    return;
  }

  const elapsedMs =
    next.systemUptimeMs - current.anchor.systemUptimeMs;
  const minimumServerEpochMs =
    current.anchor.serverEpochMs + elapsedMs;
  if (
    !Number.isSafeInteger(minimumServerEpochMs) ||
    next.serverEpochMs < minimumServerEpochMs
  ) {
    throw new Error(
      "Emergency trusted time server rollback is forbidden.",
    );
  }
}

function sameTrustedTimeAnchor(
  left: EmergencyTrustedTimeAnchor,
  right: EmergencyTrustedTimeAnchor,
): boolean {
  return (
    left.serverEpochMs === right.serverEpochMs &&
    left.systemUptimeMs === right.systemUptimeMs
  );
}

function normalizeAttendanceProvisioning(
  value: unknown,
  expectedScope: AttendanceSecurityTerminalScope,
): AttendanceQrProvisioning {
  if (!hasExactKeys(value, ["identity", "trustedTime"])) {
    throw new TypeError("Attendance cache provisioning is invalid.");
  }
  const identity = value.identity;
  const trustedTime = value.trustedTime;
  if (
    !hasExactKeys(identity, [
      "authorizationMarker",
      "deviceCode",
      "hardwareId",
      "keyHandle",
      "kid",
      "registeredAtEpochMs",
      "storeCode",
    ]) ||
    !hasExactKeys(trustedTime, ["localEpochMs", "serverEpochMs"])
  ) {
    throw new TypeError("Attendance cache provisioning is invalid.");
  }
  const normalized = Object.freeze({
    identity: Object.freeze({
      authorizationMarker: strictIdentity(
        identity.authorizationMarker,
        "attendance authorization marker",
        256,
      ),
      deviceCode: strictIdentity(
        identity.deviceCode,
        "attendance device code",
        128,
      ),
      hardwareId: strictIdentity(
        identity.hardwareId,
        "attendance hardware ID",
        256,
      ),
      keyHandle: strictOpaqueKeyHandle(identity.keyHandle),
      kid: strictAttendanceKid(identity.kid),
      registeredAtEpochMs: strictNonnegativeSafeInteger(
        identity.registeredAtEpochMs,
        "attendance registration time",
      ),
      storeCode: strictIdentity(
        identity.storeCode,
        "attendance store code",
        50,
      ),
    }),
    trustedTime: Object.freeze({
      localEpochMs: strictNonnegativeSafeInteger(
        trustedTime.localEpochMs,
        "attendance local trusted time",
      ),
      serverEpochMs: strictNonnegativeSafeInteger(
        trustedTime.serverEpochMs,
        "attendance server trusted time",
      ),
    }),
  });
  if (
    normalized.identity.storeCode !== expectedScope.storeCode ||
    normalized.identity.deviceCode !== expectedScope.deviceCode ||
    normalized.identity.hardwareId !== expectedScope.hardwareId ||
    normalized.identity.authorizationMarker !==
      expectedScope.authorizationMarker
  ) {
    throw new Error("Attendance cache provisioning scope is invalid.");
  }
  return normalized;
}

function normalizePublicKeyPackage(
  value: unknown,
): EmergencyPublicKeyPackage {
  if (
    !hasExactKeys(value, [
      "version",
      "activeKeyId",
      "generatedAtEpochMs",
      "keys",
    ]) ||
    !Array.isArray(value.keys) ||
    value.keys.length === 0 ||
    value.keys.length > 128
  ) {
    throw new TypeError("Emergency public key package is invalid.");
  }
  const version = strictNonnegativeSafeInteger(
    value.version,
    "emergency public key package version",
  );
  const generatedAtEpochMs = strictNonnegativeSafeInteger(
    value.generatedAtEpochMs,
    "emergency public key package generated time",
  );
  const seenKids = new Set<string>();
  const keys: EmergencyPublicKey[] = value.keys.map((candidate) => {
    if (
      !hasExactKeys(candidate, [
        "kid",
        "algorithm",
        "publicKeyPem",
        "fingerprintHex",
      ]) ||
      candidate.algorithm !== "ES256"
    ) {
      throw new TypeError("Emergency public key package key is invalid.");
    }
    const kid = strictEmergencyKid(
      candidate.kid,
      "emergency public key package key ID",
    );
    if (seenKids.has(kid)) {
      throw new TypeError("Emergency public key package key is duplicate.");
    }
    seenKids.add(kid);
    const publicKeyPem = strictPublicKeyPem(candidate.publicKeyPem);
    const fingerprintHex = strictFingerprint(candidate.fingerprintHex);
    return Object.freeze({
      kid,
      algorithm: "ES256" as const,
      publicKeyPem,
      fingerprintHex,
    });
  });
  const activeKeyId =
    value.activeKeyId === null
      ? null
      : strictEmergencyKid(
          value.activeKeyId,
          "emergency public key package active key",
        );
  if (activeKeyId !== null && !seenKids.has(activeKeyId)) {
    throw new TypeError(
      "Emergency public key package active key is missing.",
    );
  }
  return Object.freeze({
    version,
    activeKeyId,
    generatedAtEpochMs,
    keys: Object.freeze(keys),
  });
}

function prepareTerminalScope(
  value: AttendanceSecurityTerminalScope,
): PreparedTerminalScope {
  const terminal = normalizeTerminalScope(value);
  const material = JSON.stringify({
    format: "hb-pos-attendance-security-scope-v1",
    apiPartition: terminal.apiPartition,
    storeCode: terminal.storeCode,
    deviceCode: terminal.deviceCode,
    hardwareId: terminal.hardwareId,
    authorizationMarker: terminal.authorizationMarker,
  });
  return Object.freeze({
    terminal,
    hash: sha256Hex(material),
  });
}

function normalizeTerminalScope(
  value: unknown,
): AttendanceSecurityTerminalScope {
  if (
    !hasExactKeys(value, [
      "apiPartition",
      "storeCode",
      "deviceCode",
      "hardwareId",
      "authorizationMarker",
    ])
  ) {
    throw new TypeError("Attendance security terminal scope is invalid.");
  }
  return Object.freeze({
    apiPartition: strictIdentity(
      value.apiPartition,
      "attendance API partition",
      2_048,
    ),
    storeCode: strictIdentity(
      value.storeCode,
      "attendance store code",
      50,
    ),
    deviceCode: strictIdentity(
      value.deviceCode,
      "attendance device code",
      128,
    ),
    hardwareId: strictIdentity(
      value.hardwareId,
      "attendance hardware ID",
      256,
    ),
    authorizationMarker: strictIdentity(
      value.authorizationMarker,
      "attendance authorization marker",
      256,
    ),
  });
}

function assertPersistedScope(
  row: Readonly<{
    scope_hash: unknown;
    api_partition: unknown;
    store_code: unknown;
    device_code: unknown;
  }>,
  expected: PreparedTerminalScope,
  label: string,
): void {
  if (
    row.scope_hash !== expected.hash ||
    row.api_partition !== expected.terminal.apiPartition ||
    row.store_code !== expected.terminal.storeCode ||
    row.device_code !== expected.terminal.deviceCode
  ) {
    throw new Error(`Persisted ${label} scope is invalid.`);
  }
}

function sameTerminalScope(
  left: AttendanceSecurityTerminalScope,
  right: AttendanceSecurityTerminalScope,
): boolean {
  return (
    left.apiPartition === right.apiPartition &&
    left.storeCode === right.storeCode &&
    left.deviceCode === right.deviceCode &&
    left.hardwareId === right.hardwareId &&
    left.authorizationMarker === right.authorizationMarker
  );
}

function selectAttendanceQrCache(): string {
  return `SELECT
    scope_hash, api_partition, store_code, device_code,
    payload_revision, provisioning_ciphertext
   FROM attendance_qr_provisioning_cache`;
}

async function encryptPayload(
  encryptor: SensitivePayloadEncryptor,
  plaintext: string,
  label: string,
): Promise<Uint8Array> {
  const ciphertext = await encryptor.encrypt(plaintext);
  if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
    throw new Error(`${label} encryption failed.`);
  }
  return ciphertext;
}

function persistedCiphertext(value: unknown, label: string): Uint8Array {
  if (!(value instanceof Uint8Array) || value.length === 0) {
    throw new Error(`Persisted ${label} ciphertext is invalid.`);
  }
  return value;
}

function persistedSafeInteger(value: unknown, label: string): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0
  ) {
    throw new Error(`Persisted ${label} is invalid.`);
  }
  return value;
}

function strictNonnegativeSafeInteger(
  value: unknown,
  label: string,
): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0
  ) {
    throw new TypeError(`${label} is invalid.`);
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
    value.length === 0 ||
    value.length > maxLength ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}

function strictAttendanceKid(value: unknown): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9_-]{1,64}$/u.test(value)
  ) {
    throw new TypeError("Attendance key ID is invalid.");
  }
  return value;
}

function strictEmergencyKid(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9]{1,32}$/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}

function strictOpaqueKeyHandle(value: unknown): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 256 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError("Attendance secure key handle is invalid.");
  }
  return value;
}

function strictPublicKeyPem(value: unknown): string {
  if (
    typeof value !== "string" ||
    value.length < 64 ||
    value.length > 8_192 ||
    !value.includes("-----BEGIN PUBLIC KEY-----") ||
    !value.includes("-----END PUBLIC KEY-----") ||
    value.includes("PRIVATE KEY") ||
    /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(
      "Emergency public key package public key PEM is invalid.",
    );
  }
  return value;
}

function strictFingerprint(value: unknown): string {
  if (
    typeof value !== "string" ||
    !/^[A-Fa-f0-9]{64}$/u.test(value)
  ) {
    throw new TypeError(
      "Emergency public key package fingerprint is invalid.",
    );
  }
  return value.toUpperCase();
}

function strictCanonicalIso(value: unknown, label: string): string {
  if (
    typeof value !== "string" ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u.test(value) ||
    new Date(value).toISOString() !== value
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}

function hasExactKeys<T extends readonly string[]>(
  value: unknown,
  keys: T,
): value is Record<T[number], unknown> {
  if (
    typeof value !== "object" ||
    value === null ||
    Array.isArray(value)
  ) {
    return false;
  }
  const actual = Object.keys(value);
  return (
    actual.length === keys.length &&
    keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
  );
}

/**
 * DB facade 的 scope 在构造期同步固定；这里使用无平台依赖的 SHA-256，
 * 避免核心持久层反向依赖 Expo 原生模块或把原始授权标记写入普通列。
 */
function sha256Hex(material: string): string {
  const bytes = utf8Bytes(material);
  const paddedLength = Math.ceil((bytes.length + 9) / 64) * 64;
  const message = new Uint8Array(paddedLength);
  message.set(bytes);
  message[bytes.length] = 0x80;
  const bitLength = bytes.length * 8;
  const lengthView = new DataView(message.buffer);
  lengthView.setUint32(
    paddedLength - 8,
    Math.floor(bitLength / 0x1_0000_0000),
    false,
  );
  lengthView.setUint32(paddedLength - 4, bitLength >>> 0, false);

  let h0 = 0x6a09e667;
  let h1 = 0xbb67ae85;
  let h2 = 0x3c6ef372;
  let h3 = 0xa54ff53a;
  let h4 = 0x510e527f;
  let h5 = 0x9b05688c;
  let h6 = 0x1f83d9ab;
  let h7 = 0x5be0cd19;
  const words = new Uint32Array(64);

  for (let offset = 0; offset < message.length; offset += 64) {
    for (let index = 0; index < 16; index += 1) {
      words[index] = lengthView.getUint32(offset + index * 4, false);
    }
    for (let index = 16; index < 64; index += 1) {
      const word15 = words[index - 15] ?? 0;
      const word2 = words[index - 2] ?? 0;
      const sigma0 =
        rotateRight(word15, 7) ^
        rotateRight(word15, 18) ^
        (word15 >>> 3);
      const sigma1 =
        rotateRight(word2, 17) ^
        rotateRight(word2, 19) ^
        (word2 >>> 10);
      words[index] =
        ((words[index - 16] ?? 0) +
          sigma0 +
          (words[index - 7] ?? 0) +
          sigma1) >>>
        0;
    }

    let a = h0;
    let b = h1;
    let c = h2;
    let d = h3;
    let e = h4;
    let f = h5;
    let g = h6;
    let h = h7;
    for (let index = 0; index < 64; index += 1) {
      const sum1 =
        rotateRight(e, 6) ^
        rotateRight(e, 11) ^
        rotateRight(e, 25);
      const choice = (e & f) ^ (~e & g);
      const temporary1 =
        (h +
          sum1 +
          choice +
          (SHA256_CONSTANTS[index] ?? 0) +
          (words[index] ?? 0)) >>>
        0;
      const sum0 =
        rotateRight(a, 2) ^
        rotateRight(a, 13) ^
        rotateRight(a, 22);
      const majority = (a & b) ^ (a & c) ^ (b & c);
      const temporary2 = (sum0 + majority) >>> 0;
      h = g;
      g = f;
      f = e;
      e = (d + temporary1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (temporary1 + temporary2) >>> 0;
    }
    h0 = (h0 + a) >>> 0;
    h1 = (h1 + b) >>> 0;
    h2 = (h2 + c) >>> 0;
    h3 = (h3 + d) >>> 0;
    h4 = (h4 + e) >>> 0;
    h5 = (h5 + f) >>> 0;
    h6 = (h6 + g) >>> 0;
    h7 = (h7 + h) >>> 0;
  }

  return [h0, h1, h2, h3, h4, h5, h6, h7]
    .map((word) => word.toString(16).padStart(8, "0"))
    .join("");
}

function rotateRight(value: number, amount: number): number {
  return (value >>> amount) | (value << (32 - amount));
}

function utf8Bytes(value: string): Uint8Array {
  const bytes: number[] = [];
  for (const character of value) {
    const codePoint = character.codePointAt(0);
    if (codePoint === undefined) continue;
    if (codePoint <= 0x7f) {
      bytes.push(codePoint);
    } else if (codePoint <= 0x7ff) {
      bytes.push(
        0xc0 | (codePoint >>> 6),
        0x80 | (codePoint & 0x3f),
      );
    } else if (codePoint <= 0xffff) {
      bytes.push(
        0xe0 | (codePoint >>> 12),
        0x80 | ((codePoint >>> 6) & 0x3f),
        0x80 | (codePoint & 0x3f),
      );
    } else {
      bytes.push(
        0xf0 | (codePoint >>> 18),
        0x80 | ((codePoint >>> 12) & 0x3f),
        0x80 | ((codePoint >>> 6) & 0x3f),
        0x80 | (codePoint & 0x3f),
      );
    }
  }
  return Uint8Array.from(bytes);
}

const SHA256_CONSTANTS = Object.freeze([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
  0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
  0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
  0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
  0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
  0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
  0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
  0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);
