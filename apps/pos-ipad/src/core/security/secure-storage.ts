import type { CashierSessionDto } from "../api/hbpos-api";

export type SecureStoreWriteOptions = Readonly<{
  requireThisDeviceOnly: boolean;
}>;

export interface SecureStorePort {
  get(key: string): Promise<string | null>;
  set(key: string, value: string, options: SecureStoreWriteOptions): Promise<void>;
  remove(key: string): Promise<void>;
}

/** 由运行时注入 SHA-256，核心层不依赖 Node 或 Expo Crypto。 */
export interface CashierSessionKeyHasher {
  sha256Hex(input: string): Promise<string>;
}

const installationIdKey = "hbpos.ipad.installation-id.v1";
const deviceCredentialsKey = "hbpos.ipad.device-credentials.v1";
const devicePresentationKey = "hbpos.ipad.device-presentation.v1";
const pendingDeviceRegistrationKey = "hbpos.ipad.pending-device-registration.v1";
const deviceLockKey = "hbpos.ipad.device-lock.v1";
const activeCashierAuthorizationKey = "hbpos.ipad.active-cashier-authorization.v1";
const secureThisDeviceOnly: SecureStoreWriteOptions = { requireThisDeviceOnly: true };

export type StoredDeviceCredentials = Readonly<{
  deviceCode: string;
  storeCode: string;
  hardwareId: string;
  authorizationCode: string;
}>;

export type DevicePresentationCache = Readonly<{
  deviceCode: string;
  storeCode: string;
  storeName: string;
}>;

type StoredDevicePresentation = DevicePresentationCache &
  Readonly<{
    version: 1;
  }>;

export type PendingDeviceRegistration = Readonly<{
  deviceCode: string;
  storeCode: string;
}>;

export class InstallationIdentityStore {
  public constructor(
    private readonly secureStore: SecureStorePort,
    private readonly createUuid: () => string
  ) {}

  public async getOrCreate(): Promise<string> {
    const current = await this.secureStore.get(installationIdKey);
    if (current) {
      return current;
    }

    const installationId = this.createUuid();
    if (!installationId) {
      throw new Error("Unable to create a secure installation identifier.");
    }
    await this.secureStore.set(installationIdKey, installationId, secureThisDeviceOnly);
    return installationId;
  }
}

export class DeviceCredentialStore {
  public constructor(public readonly secureStore: SecureStorePort) {}

  public async load(): Promise<StoredDeviceCredentials | null> {
    const raw = await this.secureStore.get(deviceCredentialsKey);
    if (!raw) {
      return null;
    }

    return parseDeviceCredentials(raw);
  }

  public async save(credentials: StoredDeviceCredentials): Promise<void> {
    await this.secureStore.set(
      deviceCredentialsKey,
      JSON.stringify(validateDeviceCredentials(credentials)),
      secureThisDeviceOnly,
    );
  }

  public async clear(): Promise<void> {
    await this.secureStore.remove(deviceCredentialsKey);
  }
}

/** 展示名称不属于设备凭据；损坏或不可读时只降级为无名称。 */
export class DevicePresentationStore {
  public constructor(private readonly secureStore: SecureStorePort) {}

  public async load(): Promise<DevicePresentationCache | null> {
    try {
      const raw = await this.secureStore.get(devicePresentationKey);
      return raw ? parseDevicePresentation(raw) : null;
    } catch {
      try {
        await this.clear();
      } catch {
        // 清理失败也不能让非认证展示缓存阻断 POS。
      }
      return null;
    }
  }

  public async save(
    presentation: DevicePresentationCache,
  ): Promise<void> {
    const normalized = validateDevicePresentation(presentation);
    const stored: StoredDevicePresentation = {
      version: 1,
      ...normalized,
    };
    await this.secureStore.set(
      devicePresentationKey,
      JSON.stringify(stored),
      secureThisDeviceOnly,
    );
  }

  public clear(): Promise<void> {
    return this.secureStore.remove(devicePresentationKey);
  }
}

/** 首次注册尚未获得授权码时，仍需保存可恢复的 verify 目标。 */
export class PendingDeviceRegistrationStore {
  public constructor(private readonly secureStore: SecureStorePort) {}

  public async load(): Promise<PendingDeviceRegistration | null> {
    const raw = await this.secureStore.get(pendingDeviceRegistrationKey);
    return raw ? parsePendingDeviceRegistration(raw) : null;
  }

  public save(registration: PendingDeviceRegistration): Promise<void> {
    return this.secureStore.set(
      pendingDeviceRegistrationKey,
      JSON.stringify(validatePendingDeviceRegistration(registration)),
      secureThisDeviceOnly,
    );
  }

  public clear(): Promise<void> {
    return this.secureStore.remove(pendingDeviceRegistrationKey);
  }
}

export class DeviceLockStore {
  public constructor(private readonly secureStore: SecureStorePort) {}

  public async isLocked(): Promise<boolean> {
    return (await this.secureStore.get(deviceLockKey)) !== null;
  }

  public async lock(reason: string): Promise<void> {
    await this.secureStore.set(deviceLockKey, reason, secureThisDeviceOnly);
  }

  public async unlock(): Promise<void> {
    await this.secureStore.remove(deviceLockKey);
  }
}

export class CashierSessionCache {
  public constructor(
    public readonly secureStore: SecureStorePort,
    private readonly keyHasher: CashierSessionKeyHasher,
  ) {}

  public async load(
    storeCode: string,
    deviceCode: string,
    userBarcode: string
  ): Promise<CashierSessionDto | null> {
    const raw = await this.secureStore.get(await this.key(storeCode, deviceCode, userBarcode));
    return raw
      ? validateCashierSession(parseStored<unknown>(raw, "cashier session"), {
          storeCode,
          deviceCode,
        })
      : null;
  }

  public async save(
    storeCode: string,
    deviceCode: string,
    userBarcode: string,
    session: CashierSessionDto
  ): Promise<void> {
    const validated = validateCashierSession(session, {
      storeCode,
      deviceCode,
    });
    await this.secureStore.set(
      await this.key(storeCode, deviceCode, userBarcode),
      JSON.stringify(validated),
      secureThisDeviceOnly
    );
  }

  private async key(storeCode: string, deviceCode: string, userBarcode: string): Promise<string> {
    // Keychain 项名不能泄露可扫描的收银员条码；仅保存规范化三元组的 SHA-256 摘要。
    const material = `${storeCode.trim()}\n${deviceCode.trim()}\n${userBarcode.trim()}`;
    const digest = await this.keyHasher.sha256Hex(material);
    if (!/^[a-f0-9]{64}$/i.test(digest)) {
      throw new Error("Cashier cache key hasher returned an invalid SHA-256 digest.");
    }
    return `hbpos.ipad.cashier.v2.${digest.toLowerCase()}`;
  }
}

export class CashierAuthorizationStore {
  public constructor(
    private readonly secureStore: SecureStorePort,
    private readonly time: CashierAuthorizationTimePort = {
      getSystemUptimeMilliseconds: defaultSystemUptimeMilliseconds,
      nowEpochMs: Date.now,
    },
  ) {}

  public async get(): Promise<string | null> {
    const raw = await this.secureStore.get(
      activeCashierAuthorizationKey,
    );
    if (!raw) return null;

    let stored: StoredCashierAuthorization;
    try {
      stored = parseCashierAuthorization(raw);
      const systemUptimeMs = validTimeValue(
        this.time.getSystemUptimeMilliseconds(),
        "system uptime",
      );
      if (
        systemUptimeMs < stored.activatedAtSystemUptimeMs ||
        systemUptimeMs >= stored.expiresAtSystemUptimeMs ||
        (stored.source !== "emergency-override" &&
          validTimeValue(this.time.nowEpochMs(), "wall time") >=
            stored.expiresAtEpochMs)
      ) {
        await this.clear();
        return null;
      }
    } catch {
      // 旧版纯字符串或损坏 envelope 不能继续成为网络 bearer。
      await this.clear();
      return null;
    }
    return stored.authorizationToken;
  }

  public async set(
    authorization: CashierAuthorizationWrite,
  ): Promise<void> {
    const normalized = normalizeCashierAuthorizationWrite(
      authorization,
      this.time,
    );
    if (normalized === null) {
      await this.clear();
      return;
    }
    await this.secureStore.set(
      activeCashierAuthorizationKey,
      JSON.stringify(normalized),
      secureThisDeviceOnly,
    );
  }

  public clear(): Promise<void> {
    return this.secureStore.remove(activeCashierAuthorizationKey);
  }
}

export type CashierAuthorizationSource =
  | "online"
  | "offline-cache"
  | "emergency-override";

export type CashierAuthorizationWrite = Readonly<{
  authorizationToken: string;
  expiresAtEpochMs: number;
  source: CashierAuthorizationSource;
  systemUptimeMs?: number;
  trustedNowEpochMs?: number;
}>;

export type CashierAuthorizationTimePort = Readonly<{
  getSystemUptimeMilliseconds(): number;
  nowEpochMs(): number;
}>;

type StoredCashierAuthorization = Readonly<{
  activatedAtSystemUptimeMs: number;
  authorizationToken: string;
  expiresAtEpochMs: number;
  expiresAtSystemUptimeMs: number;
  source: CashierAuthorizationSource;
  version: 2;
}>;

function normalizeCashierAuthorizationWrite(
  input: CashierAuthorizationWrite,
  time: CashierAuthorizationTimePort,
): StoredCashierAuthorization | null {
  const authorizationToken = validAuthorizationToken(
    input.authorizationToken,
  );
  const expiresAtEpochMs = validTimeValue(
    input.expiresAtEpochMs,
    "authorization expiry",
  );
  const source = validCashierAuthorizationSource(input.source);
  const currentSystemUptimeMs = validTimeValue(
    time.getSystemUptimeMilliseconds(),
    "system uptime",
  );
  const trustedNowEpochMs =
    source === "emergency-override"
      ? validTimeValue(
          input.trustedNowEpochMs,
          "trusted emergency time",
        )
      : validTimeValue(time.nowEpochMs(), "wall time");
  const activatedAtSystemUptimeMs =
    source === "emergency-override"
      ? validTimeValue(
          input.systemUptimeMs,
          "trusted emergency uptime",
        )
      : currentSystemUptimeMs;
  const durationMs = expiresAtEpochMs - trustedNowEpochMs;
  const expiresAtSystemUptimeMs =
    activatedAtSystemUptimeMs + durationMs;
  if (
    durationMs <= 0 ||
    currentSystemUptimeMs < activatedAtSystemUptimeMs ||
    !Number.isSafeInteger(expiresAtSystemUptimeMs) ||
    currentSystemUptimeMs >= expiresAtSystemUptimeMs
  ) {
    return null;
  }
  return Object.freeze({
    activatedAtSystemUptimeMs,
    authorizationToken,
    expiresAtEpochMs,
    expiresAtSystemUptimeMs,
    source,
    version: 2,
  });
}

function parseCashierAuthorization(
  raw: string,
): StoredCashierAuthorization {
  const value = parseStored<unknown>(
    raw,
    activeCashierAuthorizationKey,
  );
  const record = storedRecord(
    value,
    activeCashierAuthorizationKey,
  );
  const expectedKeys = [
    "activatedAtSystemUptimeMs",
    "authorizationToken",
    "expiresAtEpochMs",
    "expiresAtSystemUptimeMs",
    "source",
    "version",
  ];
  if (
    Object.keys(record).length !== expectedKeys.length ||
    expectedKeys.some((key) => !(key in record)) ||
    record.version !== 2
  ) {
    throw new Error(
      `Stored ${activeCashierAuthorizationKey} is invalid.`,
    );
  }
  const activatedAtSystemUptimeMs = validTimeValue(
    record.activatedAtSystemUptimeMs,
    "stored authorization uptime",
  );
  const expiresAtSystemUptimeMs = validTimeValue(
    record.expiresAtSystemUptimeMs,
    "stored authorization monotonic expiry",
  );
  const expiresAtEpochMs = validTimeValue(
    record.expiresAtEpochMs,
    "stored authorization expiry",
  );
  if (expiresAtSystemUptimeMs <= activatedAtSystemUptimeMs) {
    throw new Error(
      `Stored ${activeCashierAuthorizationKey} is invalid.`,
    );
  }
  return Object.freeze({
    activatedAtSystemUptimeMs,
    authorizationToken: validAuthorizationToken(
      record.authorizationToken,
    ),
    expiresAtEpochMs,
    expiresAtSystemUptimeMs,
    source: validCashierAuthorizationSource(record.source),
    version: 2,
  });
}

function validCashierAuthorizationSource(
  value: unknown,
): CashierAuthorizationSource {
  if (
    value !== "online" &&
    value !== "offline-cache" &&
    value !== "emergency-override"
  ) {
    throw new Error("Cashier authorization source is invalid.");
  }
  return value;
}

function validAuthorizationToken(value: unknown): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 16_384 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error("Cashier authorization token is invalid.");
  }
  return value;
}

function validTimeValue(
  value: unknown,
  label: string,
): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0
  ) {
    throw new Error(`${label} is invalid.`);
  }
  return value;
}

function defaultSystemUptimeMilliseconds(): number {
  const value = globalThis.performance?.now();
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error("System uptime is unavailable.");
  }
  return Math.floor(value);
}

function parseStored<T>(raw: string, label: string): T {
  try {
    return JSON.parse(raw) as T;
  } catch {
    throw new Error(`Stored ${label} is invalid.`);
  }
}

function parseDeviceCredentials(raw: string): StoredDeviceCredentials {
  return validateDeviceCredentials(
    parseStored<unknown>(raw, deviceCredentialsKey),
  );
}

function validateDeviceCredentials(value: unknown): StoredDeviceCredentials {
  const record = storedRecord(value, deviceCredentialsKey);
  return {
    deviceCode: storedText(record.deviceCode, deviceCredentialsKey),
    storeCode: storedText(record.storeCode, deviceCredentialsKey),
    hardwareId: storedText(record.hardwareId, deviceCredentialsKey),
    authorizationCode: storedText(record.authorizationCode, deviceCredentialsKey),
  };
}

function parseDevicePresentation(
  raw: string,
): DevicePresentationCache {
  return validateDevicePresentation(
    parseStored<unknown>(raw, devicePresentationKey),
    true,
  );
}

function validateDevicePresentation(
  value: unknown,
  requireVersion = false,
): DevicePresentationCache {
  const record = storedRecord(value, devicePresentationKey);
  const expectedKeys = requireVersion
    ? ["version", "deviceCode", "storeCode", "storeName"]
    : ["deviceCode", "storeCode", "storeName"];
  if (
    Object.keys(record).length !== expectedKeys.length ||
    expectedKeys.some((key) => !(key in record)) ||
    (requireVersion && record.version !== 1)
  ) {
    throw new Error(`Stored ${devicePresentationKey} is invalid.`);
  }
  return {
    deviceCode: storedText(
      record.deviceCode,
      devicePresentationKey,
    ).trim(),
    storeCode: storedText(
      record.storeCode,
      devicePresentationKey,
    ).trim(),
    storeName: storedText(
      record.storeName,
      devicePresentationKey,
    ).trim(),
  };
}

function parsePendingDeviceRegistration(raw: string): PendingDeviceRegistration {
  return validatePendingDeviceRegistration(
    parseStored<unknown>(raw, pendingDeviceRegistrationKey),
  );
}

function validatePendingDeviceRegistration(
  value: unknown,
): PendingDeviceRegistration {
  const record = storedRecord(value, pendingDeviceRegistrationKey);
  return {
    deviceCode: storedText(record.deviceCode, pendingDeviceRegistrationKey),
    storeCode: storedText(record.storeCode, pendingDeviceRegistrationKey),
  };
}

function validateCashierSession(
  value: unknown,
  binding: Readonly<{ storeCode: string; deviceCode: string }>,
): CashierSessionDto {
  const label = "cashier session";
  const record = storedRecord(value, label);
  const storeCode = storedText(record.storeCode, label);
  const deviceCode = storedText(record.deviceCode, label);
  if (storeCode !== binding.storeCode || deviceCode !== binding.deviceCode) {
    throw new Error(`Stored ${label} is invalid.`);
  }

  return {
    cashierId: storedText(record.cashierId, label),
    userGuid: storedText(record.userGuid, label),
    cashierName: storedText(record.cashierName, label),
    storeCode,
    deviceCode,
    ...optionalStoredTextArrayProperty(record, "roles", label),
    ...optionalStoredTextArrayProperty(record, "permissionCodes", label),
    ...optionalStoredTextArrayProperty(record, "allowedStoreCodes", label),
    ...optionalStoredBooleanProperty(record, "isSuperAdmin", label),
    ...optionalStoredBooleanProperty(record, "isOfflineCached", label),
    ...optionalStoredBooleanProperty(record, "isEmergencyOverride", label),
    ...optionalStoredTextProperty(record, "authorizationToken", label),
    ...optionalStoredDateProperty(record, "authorizationExpiresAtUtc", label),
    ...optionalStoredTextProperty(record, "emergencyGrantId", label),
  };
}

function optionalStoredTextArrayProperty(
  record: Readonly<Record<string, unknown>>,
  key: "roles" | "permissionCodes" | "allowedStoreCodes",
  label: string,
): Partial<Record<typeof key, string[] | null>> {
  const value = record[key];
  if (value === undefined) return {};
  if (value === null) return { [key]: null };
  if (!Array.isArray(value)) throw new Error(`Stored ${label} is invalid.`);
  return { [key]: value.map((item) => storedText(item, label)) };
}

function optionalStoredBooleanProperty(
  record: Readonly<Record<string, unknown>>,
  key: "isSuperAdmin" | "isOfflineCached" | "isEmergencyOverride",
  label: string,
): Partial<Record<typeof key, boolean>> {
  const value = record[key];
  if (value === undefined) return {};
  if (typeof value !== "boolean") throw new Error(`Stored ${label} is invalid.`);
  return { [key]: value };
}

function optionalStoredTextProperty(
  record: Readonly<Record<string, unknown>>,
  key: "authorizationToken" | "emergencyGrantId",
  label: string,
): Partial<Record<typeof key, string | null>> {
  const value = record[key];
  if (value === undefined) return {};
  return { [key]: value === null ? null : storedText(value, label) };
}

function optionalStoredDateProperty(
  record: Readonly<Record<string, unknown>>,
  key: "authorizationExpiresAtUtc",
  label: string,
): Partial<Record<typeof key, string | null>> {
  const value = record[key];
  if (value === undefined || value === null) {
    return value === undefined ? {} : { [key]: null };
  }
  const date = storedText(value, label);
  if (!Number.isFinite(Date.parse(date))) {
    throw new Error(`Stored ${label} is invalid.`);
  }
  return { [key]: date };
}

function storedRecord(
  value: unknown,
  label: string,
): Readonly<Record<string, unknown>> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`Stored ${label} is invalid.`);
  }
  return value as Readonly<Record<string, unknown>>;
}

function storedText(value: unknown, label: string): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error(`Stored ${label} is invalid.`);
  }
  return value;
}

/** 仅用于 Node 测试；生产应用必须注入 Expo Keychain 适配器。 */
export class InMemorySecureStore implements SecureStorePort {
  private readonly values = new Map<string, string>();

  public lastWriteOptions: SecureStoreWriteOptions | undefined;
  public lastWriteKey: string | undefined;

  public async get(key: string): Promise<string | null> {
    return this.values.get(key) ?? null;
  }

  public async set(key: string, value: string, options: SecureStoreWriteOptions): Promise<void> {
    this.lastWriteKey = key;
    this.lastWriteOptions = options;
    this.values.set(key, value);
  }

  public async remove(key: string): Promise<void> {
    this.values.delete(key);
  }
}
