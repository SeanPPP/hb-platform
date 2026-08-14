import type {
  AttendanceSecurityRemotePort,
  EmergencyPublicKey,
  EmergencyPublicKeyAckResult,
  EmergencyPublicKeyPackage,
} from "./hbpos-attendance-security-api";

export const EMERGENCY_LOGIN_MAX_TOKEN_LENGTH = 2_048;
const EMERGENCY_TRUSTED_TIME_MAX_ACK_RTT_MS = 5_000;
const EMERGENCY_TRUSTED_TIME_UNCERTAINTY_MS =
  EMERGENCY_TRUSTED_TIME_MAX_ACK_RTT_MS;

export interface EmergencyPublicKeyCachePort {
  read(): Promise<EmergencyPublicKeyPackage | null>;
  replace(value: EmergencyPublicKeyPackage): Promise<void>;
}

export type EmergencyVerifiedClaims = Readonly<{
  expiresAtEpochMs: number;
  grantId: string;
  notBeforeEpochMs: number;
  storeCode: string;
}>;

export type EmergencyTokenCryptoResult =
  | Readonly<{ ok: true; claims: EmergencyVerifiedClaims }>
  | Readonly<{ ok: false; errorCode: string }>;

export interface EmergencyLoginCryptoPort {
  validateEs256P256PublicKey(
    key: EmergencyPublicKey,
  ): Promise<boolean>;
  verifyEs256P256Token(input: Readonly<{
    expectedStoreCode: string;
    nowEpochMs: number;
    publicKeys: readonly EmergencyPublicKey[];
    token: string;
  }>): Promise<EmergencyTokenCryptoResult>;
}

export type EmergencyTrustedTimeAnchor = Readonly<{
  serverEpochMs: number;
  systemUptimeMs: number;
}>;

export interface EmergencyTrustedTimePort {
  readAnchor(): Promise<EmergencyTrustedTimeAnchor | null>;
  replaceAnchor(value: EmergencyTrustedTimeAnchor): Promise<void>;
}

export interface EmergencySystemUptimePort {
  getSystemUptimeMilliseconds(): number;
}

export interface EmergencyLoginSessionPort {
  activateEmergencyOverride(input: Readonly<{
    authorizationToken: string;
    deviceCode: string;
    emergencyGrantId: string;
    expiresAtEpochMs: number;
    storeCode: string;
    systemUptimeMs: number;
    trustedNowEpochMs: number;
  }>): Promise<void>;
}

export interface EmergencyPublicKeySyncPort {
  sync(): Promise<boolean>;
}

export type EmergencyLoginResult =
  | Readonly<{
      ok: true;
      emergencyGrantId: string;
      expiresAtEpochMs: number;
      systemUptimeMs: number;
      trustedNowEpochMs: number;
    }>
  | Readonly<{
      ok: false;
      errorCode:
        | "EMERGENCY_CLOCK_ROLLBACK"
        | "EMERGENCY_SESSION_ACTIVATION_FAILED"
        | "EMERGENCY_TOKEN_EXPIRED"
        | "EMERGENCY_TOKEN_FORMAT_INVALID"
        | "EMERGENCY_TOKEN_INVALID"
        | "EMERGENCY_TOKEN_KEY_UNKNOWN"
        | "EMERGENCY_TOKEN_NOT_ACTIVE"
        | "EMERGENCY_TOKEN_SIGNATURE_INVALID"
        | "EMERGENCY_TOKEN_TOO_LONG"
        | "EMERGENCY_TOKEN_WRONG_STORE"
        | "EMERGENCY_TRUSTED_TIME_UNAVAILABLE";
    }>;

export class EmergencyPublicKeySyncService
  implements EmergencyPublicKeySyncPort
{
  private inFlight: Promise<boolean> | null = null;

  public constructor(
    private readonly options: Readonly<{
      cache: EmergencyPublicKeyCachePort;
      crypto: EmergencyLoginCryptoPort;
      remote: AttendanceSecurityRemotePort;
      systemUptime: EmergencySystemUptimePort;
      trustedTime: EmergencyTrustedTimePort;
    }>,
  ) {}

  public sync(): Promise<boolean> {
    if (this.inFlight) return this.inFlight;
    let running!: Promise<boolean>;
    running = this.runSync()
      .catch(() => false)
      .finally(() => {
        if (this.inFlight === running) this.inFlight = null;
      });
    this.inFlight = running;
    return running;
  }

  private async runSync(): Promise<boolean> {
    const cached = await this.options.cache.read();
    const validCurrent = await validatePublicKeyPackage(
      cached,
      this.options.crypto,
    );
    const fetched =
      await this.options.remote.fetchEmergencyPublicKeys(
        validCurrent?.version ?? null,
      );
    if (fetched.kind === "not-modified") {
      return validCurrent
        ? this.acknowledgeWithSingleRetry(validCurrent.version)
        : false;
    }

    const next = await validatePublicKeyPackage(
      fetched.package,
      this.options.crypto,
    );
    if (
      !next ||
      (validCurrent && next.version < validCurrent.version)
    ) {
      return false;
    }
    if (!validCurrent || next.version > validCurrent.version) {
      // 整包验证通过后才原子替换；ACK 必须晚于持久化成功。
      await this.options.cache.replace(next);
    }
    return this.acknowledgeWithSingleRetry(next.version);
  }

  private async acknowledgeWithSingleRetry(
    version: number,
  ): Promise<boolean> {
    const firstAttempt = await this.requestAcknowledgement(version);
    if (firstAttempt === null) return false;
    const { acknowledgement } = firstAttempt;
    if (
      acknowledgement.acknowledged &&
      acknowledgement.serverVersion === version
    ) {
      return this.acceptServerTime(
        acknowledgement.serverTimeEpochMs,
        firstAttempt.requestStartedSystemUptimeMs,
        firstAttempt.responseReceivedSystemUptimeMs,
      );
    }

    // 轮换竞态只允许一次无 ETag 重拉与一次 ACK，持续冲突交给后台节流。
    const refreshed =
      await this.options.remote.fetchEmergencyPublicKeys(null);
    if (refreshed.kind !== "changed") return false;
    const next = await validatePublicKeyPackage(
      refreshed.package,
      this.options.crypto,
    );
    if (
      !next ||
      next.version <
        Math.max(version, acknowledgement.serverVersion)
    ) {
      return false;
    }
    await this.options.cache.replace(next);
    const retryAttempt = await this.requestAcknowledgement(
      next.version,
    );
    if (retryAttempt === null) return false;
    const retried = retryAttempt.acknowledgement;
    return retried.acknowledged &&
      retried.serverVersion === next.version
      ? this.acceptServerTime(
          retried.serverTimeEpochMs,
          retryAttempt.requestStartedSystemUptimeMs,
          retryAttempt.responseReceivedSystemUptimeMs,
        )
      : false;
  }

  private async requestAcknowledgement(
    version: number,
  ): Promise<
    | Readonly<{
        acknowledgement: EmergencyPublicKeyAckResult;
        requestStartedSystemUptimeMs: number;
        responseReceivedSystemUptimeMs: number;
      }>
    | null
  > {
    // 服务端时间产生于 ACK 往返期间；响应 uptime 与服务端时间构成可信下界，
    // 固定 RTT 上限再构成上界。该安全约束不能依赖普通 HTTP timeout。
    const requestStartedSystemUptimeMs = validSystemUptime(
      this.options.systemUptime.getSystemUptimeMilliseconds(),
    );
    const acknowledgement =
      await this.options.remote.acknowledgeEmergencyPublicKeys(version);
    const responseReceivedSystemUptimeMs = validSystemUptime(
      this.options.systemUptime.getSystemUptimeMilliseconds(),
    );
    if (
      !validAcknowledgementRoundTrip(
        requestStartedSystemUptimeMs,
        responseReceivedSystemUptimeMs,
      )
    ) {
      return null;
    }
    return Object.freeze({
      acknowledgement,
      requestStartedSystemUptimeMs,
      responseReceivedSystemUptimeMs,
    });
  }

  private async acceptServerTime(
    serverTimeEpochMs: number,
    requestStartedSystemUptimeMs: number,
    responseReceivedSystemUptimeMs: number,
  ): Promise<boolean> {
    if (
      !Number.isSafeInteger(serverTimeEpochMs) ||
      serverTimeEpochMs < 0 ||
      !validAcknowledgementRoundTrip(
        requestStartedSystemUptimeMs,
        responseReceivedSystemUptimeMs,
      )
    ) {
      return false;
    }
    try {
      const current =
        await this.options.trustedTime.readAnchor();
      let persistedLowerEpochMs = serverTimeEpochMs;
      if (current !== null) {
        if (!validAnchor(current)) return false;
        if (
          requestStartedSystemUptimeMs < current.systemUptimeMs
        ) {
          // 无 boot ID 时，只有服务端时间严格前进才能把 uptime 下降解释为设备重启。
          if (serverTimeEpochMs <= current.serverEpochMs) {
            return false;
          }
        } else {
          const minimumServerTimeAtRequest =
            current.serverEpochMs +
            (requestStartedSystemUptimeMs -
              current.systemUptimeMs);
          if (
            !Number.isSafeInteger(minimumServerTimeAtRequest) ||
            serverTimeEpochMs < minimumServerTimeAtRequest
          ) {
            return false;
          }
          const minimumServerTimeAtResponse =
            current.serverEpochMs +
            (responseReceivedSystemUptimeMs -
              current.systemUptimeMs);
          if (!Number.isSafeInteger(minimumServerTimeAtResponse)) {
            return false;
          }
          // ACK 时间和旧锚点分别提供一个响应时刻下界，必须保留两者中较大者。
          persistedLowerEpochMs = Math.max(
            serverTimeEpochMs,
            minimumServerTimeAtResponse,
          );
        }
      }
      await this.options.trustedTime.replaceAnchor({
        serverEpochMs: persistedLowerEpochMs,
        // ACK 服务端时间不晚于响应到达时刻，因此这里持久化可信下界。
        systemUptimeMs: responseReceivedSystemUptimeMs,
      });
      return true;
    } catch {
      return false;
    }
  }
}

export class EmergencyLoginSecurityService {
  public constructor(
    private readonly options: Readonly<{
      cache: EmergencyPublicKeyCachePort;
      crypto: EmergencyLoginCryptoPort;
      session: EmergencyLoginSessionPort;
      sync: EmergencyPublicKeySyncPort;
      systemUptime: EmergencySystemUptimePort;
      trustedTime: EmergencyTrustedTimePort;
    }>,
  ) {}

  public async verifyAndActivate(
    token: string,
    device: Readonly<{ deviceCode: string; storeCode: string }>,
  ): Promise<EmergencyLoginResult> {
    if (token.length > EMERGENCY_LOGIN_MAX_TOKEN_LENGTH) {
      return failed("EMERGENCY_TOKEN_TOO_LONG");
    }
    if (
      !token.startsWith("HBPOSE1-") &&
      !token.startsWith("HBPOSE2-")
    ) {
      return failed("EMERGENCY_TOKEN_FORMAT_INVALID");
    }
    if (
      !validCode(device.storeCode, 50) ||
      !validCode(device.deviceCode, 128)
    ) {
      return failed("EMERGENCY_TOKEN_INVALID");
    }

    let trusted = await this.readTrustedTime();
    if (!trusted.ok) {
      // 首次安装或设备重启后只允许一次设备认证 ACK 重建锚点；离线时同步会安全失败。
      await this.options.sync.sync();
      trusted = await this.readTrustedTime();
    }
    if (!trusted.ok) {
      return failed(trusted.errorCode);
    }
    let {
      lowerNowEpochMs,
      systemUptimeMs,
      upperNowEpochMs,
    } = trusted;

    let verified: EmergencyTokenCryptoResult;
    try {
      verified = await this.verifyWithCurrentKeys(
        token,
        device.storeCode,
        lowerNowEpochMs,
      );
      if (
        !verified.ok &&
        verified.errorCode === "EMERGENCY_TOKEN_KEY_UNKNOWN"
      ) {
        // 未知 kid 只同步并重验一次，避免扫描触发无界网络循环。
        await this.options.sync.sync();
        trusted = await this.readTrustedTime();
        if (!trusted.ok) {
          return failed(trusted.errorCode);
        }
        lowerNowEpochMs = trusted.lowerNowEpochMs;
        systemUptimeMs = trusted.systemUptimeMs;
        upperNowEpochMs = trusted.upperNowEpochMs;
        verified = await this.verifyWithCurrentKeys(
          token,
          device.storeCode,
          lowerNowEpochMs,
        );
      }
    } catch {
      return failed("EMERGENCY_TOKEN_KEY_UNKNOWN");
    }
    if (!verified.ok) {
      return failed(normalizeVerificationError(verified.errorCode));
    }
    const claims = validClaims(
      verified.claims,
      device.storeCode,
      lowerNowEpochMs,
      upperNowEpochMs,
    );
    if (!claims) return failed("EMERGENCY_TOKEN_INVALID");

    try {
      await this.options.trustedTime.replaceAnchor({
        serverEpochMs: lowerNowEpochMs,
        systemUptimeMs,
      });
    } catch {
      return failed("EMERGENCY_TRUSTED_TIME_UNAVAILABLE");
    }
    try {
      await this.options.session.activateEmergencyOverride({
        authorizationToken: token,
        deviceCode: device.deviceCode,
        emergencyGrantId: claims.grantId,
        expiresAtEpochMs: claims.expiresAtEpochMs,
        storeCode: device.storeCode,
        systemUptimeMs,
        trustedNowEpochMs: upperNowEpochMs,
      });
    } catch {
      return failed("EMERGENCY_SESSION_ACTIVATION_FAILED");
    }
    return Object.freeze({
      ok: true,
      emergencyGrantId: claims.grantId,
      expiresAtEpochMs: claims.expiresAtEpochMs,
      systemUptimeMs,
      trustedNowEpochMs: upperNowEpochMs,
    });
  }

  private async readTrustedTime(): Promise<
    | Readonly<{
        ok: true;
        lowerNowEpochMs: number;
        systemUptimeMs: number;
        upperNowEpochMs: number;
      }>
    | Readonly<{
        ok: false;
        errorCode:
          | "EMERGENCY_CLOCK_ROLLBACK"
          | "EMERGENCY_TRUSTED_TIME_UNAVAILABLE";
      }>
  > {
    try {
      const systemUptimeMs = validSystemUptime(
        this.options.systemUptime.getSystemUptimeMilliseconds(),
      );
      const anchor = await this.options.trustedTime.readAnchor();
      if (anchor === null || !validAnchor(anchor)) {
        return {
          errorCode: "EMERGENCY_TRUSTED_TIME_UNAVAILABLE",
          ok: false,
        };
      }
      if (systemUptimeMs < anchor.systemUptimeMs) {
        return {
          errorCode: "EMERGENCY_CLOCK_ROLLBACK",
          ok: false,
        };
      }
      const lowerNowEpochMs =
        anchor.serverEpochMs +
        (systemUptimeMs - anchor.systemUptimeMs);
      const upperNowEpochMs =
        lowerNowEpochMs +
        EMERGENCY_TRUSTED_TIME_UNCERTAINTY_MS;
      if (
        !Number.isSafeInteger(lowerNowEpochMs) ||
        lowerNowEpochMs < anchor.serverEpochMs ||
        !Number.isSafeInteger(upperNowEpochMs) ||
        upperNowEpochMs < lowerNowEpochMs
      ) {
        return {
          errorCode: "EMERGENCY_TRUSTED_TIME_UNAVAILABLE",
          ok: false,
        };
      }
      return {
        lowerNowEpochMs,
        ok: true,
        systemUptimeMs,
        upperNowEpochMs,
      };
    } catch {
      return {
        errorCode: "EMERGENCY_TRUSTED_TIME_UNAVAILABLE",
        ok: false,
      };
    }
  }

  private async verifyWithCurrentKeys(
    token: string,
    storeCode: string,
    nowEpochMs: number,
  ): Promise<EmergencyTokenCryptoResult> {
    const cached = await this.options.cache.read();
    const packageValue = await validatePublicKeyPackage(
      cached,
      this.options.crypto,
    );
    return this.options.crypto.verifyEs256P256Token({
      expectedStoreCode: storeCode,
      nowEpochMs,
      publicKeys: packageValue?.keys ?? [],
      token,
    });
  }
}

function validAnchor(
  value: EmergencyTrustedTimeAnchor,
): boolean {
  return (
    Number.isSafeInteger(value.serverEpochMs) &&
    value.serverEpochMs >= 0 &&
    Number.isSafeInteger(value.systemUptimeMs) &&
    value.systemUptimeMs >= 0
  );
}

function validSystemUptime(value: number): number {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error("System uptime is invalid.");
  }
  return value;
}

function validAcknowledgementRoundTrip(
  requestStartedSystemUptimeMs: number,
  responseReceivedSystemUptimeMs: number,
): boolean {
  const elapsedMs =
    responseReceivedSystemUptimeMs -
    requestStartedSystemUptimeMs;
  return (
    Number.isSafeInteger(requestStartedSystemUptimeMs) &&
    requestStartedSystemUptimeMs >= 0 &&
    Number.isSafeInteger(responseReceivedSystemUptimeMs) &&
    responseReceivedSystemUptimeMs >= 0 &&
    Number.isSafeInteger(elapsedMs) &&
    elapsedMs >= 0 &&
    elapsedMs <= EMERGENCY_TRUSTED_TIME_MAX_ACK_RTT_MS
  );
}

async function validatePublicKeyPackage(
  value: EmergencyPublicKeyPackage | null,
  crypto: EmergencyLoginCryptoPort,
): Promise<EmergencyPublicKeyPackage | null> {
  if (
    !value ||
    !Number.isSafeInteger(value.version) ||
    value.version < 0 ||
    !Number.isSafeInteger(value.generatedAtEpochMs) ||
    value.generatedAtEpochMs < 0 ||
    !Array.isArray(value.keys) ||
    value.keys.length === 0 ||
    value.keys.length > 128
  ) {
    return null;
  }
  const keyIds = new Set<string>();
  const keys: EmergencyPublicKey[] = [];
  for (const key of value.keys) {
    if (
      !/^[A-Za-z0-9]{1,32}$/u.test(key.kid) ||
      keyIds.has(key.kid) ||
      key.algorithm !== "ES256" ||
      !/^[A-Fa-f0-9]{64}$/u.test(key.fingerprintHex) ||
      key.publicKeyPem.length < 64 ||
      key.publicKeyPem.length > 8_192 ||
      !key.publicKeyPem.includes("-----BEGIN PUBLIC KEY-----") ||
      !key.publicKeyPem.includes("-----END PUBLIC KEY-----") ||
      key.publicKeyPem.includes("PRIVATE KEY")
    ) {
      return null;
    }
    let validKey = false;
    try {
      validKey = await crypto.validateEs256P256PublicKey(key);
    } catch {
      return null;
    }
    if (!validKey) return null;
    keyIds.add(key.kid);
    keys.push(
      Object.freeze({
        algorithm: "ES256",
        fingerprintHex: key.fingerprintHex.toUpperCase(),
        kid: key.kid,
        publicKeyPem: key.publicKeyPem,
      }),
    );
  }
  const activeKeyId =
    value.activeKeyId === null || value.activeKeyId === ""
      ? null
      : value.activeKeyId;
  if (
    activeKeyId !== null &&
    (!/^[A-Za-z0-9]{1,32}$/u.test(activeKeyId) ||
      !keyIds.has(activeKeyId))
  ) {
    return null;
  }
  return Object.freeze({
    activeKeyId,
    generatedAtEpochMs: value.generatedAtEpochMs,
    keys: Object.freeze(keys),
    version: value.version,
  });
}

function validClaims(
  claims: EmergencyVerifiedClaims,
  expectedStoreCode: string,
  lowerNowEpochMs: number,
  upperNowEpochMs: number,
): EmergencyVerifiedClaims | null {
  if (
    !isUuid(claims.grantId) ||
    !validCode(claims.storeCode, 50) ||
    claims.storeCode.toUpperCase() !==
      expectedStoreCode.toUpperCase() ||
    !Number.isSafeInteger(claims.notBeforeEpochMs) ||
    !Number.isSafeInteger(claims.expiresAtEpochMs) ||
    claims.notBeforeEpochMs > lowerNowEpochMs ||
    claims.expiresAtEpochMs <= upperNowEpochMs ||
    claims.expiresAtEpochMs <= claims.notBeforeEpochMs
  ) {
    return null;
  }
  return Object.freeze({ ...claims });
}

function normalizeVerificationError(
  value: string,
): Exclude<EmergencyLoginResult, { ok: true }>["errorCode"] {
  if (value === "EMERGENCY_TOKEN_EXPIRED") return value;
  if (value === "EMERGENCY_TOKEN_NOT_ACTIVE") return value;
  if (value === "EMERGENCY_TOKEN_KEY_UNKNOWN") return value;
  if (value === "EMERGENCY_TOKEN_WRONG_STORE") return value;
  if (value === "EMERGENCY_TOKEN_SIGNATURE_INVALID") return value;
  if (value === "EMERGENCY_TOKEN_FORMAT_INVALID") return value;
  return "EMERGENCY_TOKEN_INVALID";
}

function failed(
  errorCode: Exclude<
    EmergencyLoginResult,
    { ok: true }
  >["errorCode"],
): EmergencyLoginResult {
  return Object.freeze({ errorCode, ok: false });
}

function validCode(value: string, maxLength: number): boolean {
  return (
    value.trim() === value &&
    value.length > 0 &&
    value.length <= maxLength &&
    !/[\u0000-\u001f\u007f]/u.test(value)
  );
}

function isUuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
    value,
  );
}
