import type { CashierLoginResult } from "../security/cashier-authentication";

export type TrustedCashierSession = Readonly<{
  epoch: number;
  cashierId: string;
  userGuid: string | null;
  cashierName: string;
  storeCode: string;
  deviceCode: string;
  permissionCodes: readonly string[];
  source: "online" | "offline-cache" | "emergency-override";
  isEmergencyOverride: boolean;
  authorizationExpiresAtEpochMs: number | null;
  authorizationActivatedAtSystemUptimeMs: number | null;
  authorizationExpiresAtSystemUptimeMs: number | null;
}>;

export type PosCashierSummary = Readonly<{
  cashierId: string;
  userGuid: string | null;
  cashierName: string;
  storeCode: string;
  deviceCode: string;
  permissions: readonly string[];
  source: "online" | "offline-cache" | "emergency-override";
}>;

export type TrustedCashierLease = Readonly<{
  get(): TrustedCashierSession;
}>;

export class CurrentCashierSessionError extends Error {
  public constructor(
    public readonly code:
      | "CURRENT_CASHIER_REQUIRED"
      | "CASHIER_AUTHENTICATION_SUPERSEDED"
      | "CASHIER_SESSION_IDENTITY_INVALID",
  ) {
    super(code);
  }
}

/**
 * 当前收银员只存在于组合根。React/Zustand 得到的是脱敏投影，不能反向写回权限、
 * 订单身份或钱箱授权；epoch 使 401、403 和锁屏后的旧 presenter 立即失效。
 */
export class CurrentCashierSession {
  private active: TrustedCashierSession | null = null;
  private epoch = 0;

  public constructor(
    private readonly systemUptimeMs: () => number =
      defaultSystemUptimeMilliseconds,
    private readonly onEmergencyExpired: () => void = () =>
      undefined,
  ) {}

  public beginAuthentication(): number {
    this.clear();
    return this.epoch;
  }

  public activate(
    authenticationEpoch: number,
    result: CashierLoginResult,
    expectedDevice: Readonly<{ storeCode: string; deviceCode: string }>,
  ): PosCashierSummary {
    if (authenticationEpoch !== this.epoch || this.active !== null) {
      throw new CurrentCashierSessionError(
        "CASHIER_AUTHENTICATION_SUPERSEDED",
      );
    }
    const expectedStoreCode = requiredText(
      expectedDevice.storeCode,
      "Expected store code",
    );
    const expectedDeviceCode = requiredText(
      expectedDevice.deviceCode,
      "Expected device code",
    );
    const session = result.session;
    const storeCode = requiredSessionText(session.storeCode);
    const deviceCode = requiredSessionText(session.deviceCode);
    if (
      storeCode !== expectedStoreCode ||
      deviceCode !== expectedDeviceCode
    ) {
      throw new CurrentCashierSessionError(
        "CASHIER_SESSION_IDENTITY_INVALID",
      );
    }
    const permissionCodes = Object.freeze(
      [
        ...new Set(
          (session.permissionCodes ?? []).map((permission) =>
            requiredSessionText(permission),
          ),
        ),
      ].sort(),
    );
    const isEmergencyOverride =
      session.isEmergencyOverride === true;
    const emergencyTiming = result.emergencyTiming;
    const authorizationExpiresAtEpochMs = isEmergencyOverride
      ? requiredFutureEpoch(
          session.authorizationExpiresAtUtc,
          emergencyTiming?.trustedNowEpochMs,
        )
      : null;
    const authorizationActivatedAtSystemUptimeMs =
      isEmergencyOverride
        ? requiredSafeTimeValue(
            emergencyTiming?.systemUptimeMs,
          )
        : null;
    const authorizationExpiresAtSystemUptimeMs =
      isEmergencyOverride &&
      authorizationExpiresAtEpochMs !== null &&
      authorizationActivatedAtSystemUptimeMs !== null &&
      emergencyTiming
        ? requiredSafeTimeValue(
            authorizationActivatedAtSystemUptimeMs +
              (authorizationExpiresAtEpochMs -
                emergencyTiming.trustedNowEpochMs),
          )
        : null;
    if (
      (result.source === "emergency-override") !==
        isEmergencyOverride ||
      (isEmergencyOverride !== (emergencyTiming !== undefined))
    ) {
      throw new CurrentCashierSessionError(
        "CASHIER_SESSION_IDENTITY_INVALID",
      );
    }
    const next = Object.freeze({
      epoch: this.epoch,
      cashierId: requiredSessionText(session.cashierId),
      userGuid: optionalSessionText(session.userGuid),
      cashierName: requiredSessionText(session.cashierName),
      storeCode,
      deviceCode,
      permissionCodes,
      source: result.source,
      isEmergencyOverride,
      authorizationExpiresAtEpochMs,
      authorizationActivatedAtSystemUptimeMs,
      authorizationExpiresAtSystemUptimeMs,
    }) satisfies TrustedCashierSession;
    if (isEmergencyOverride) {
      const currentSystemUptimeMs = requiredSafeTimeValue(
        this.systemUptimeMs(),
      );
      if (
        authorizationActivatedAtSystemUptimeMs === null ||
        authorizationExpiresAtSystemUptimeMs === null ||
        currentSystemUptimeMs <
          authorizationActivatedAtSystemUptimeMs ||
        currentSystemUptimeMs >=
          authorizationExpiresAtSystemUptimeMs
      ) {
        throw new CurrentCashierSessionError(
          "CASHIER_SESSION_IDENTITY_INVALID",
        );
      }
    }
    this.active = next;
    return toPublicSummary(next);
  }

  public require(): TrustedCashierSession {
    if (!this.active) {
      throw new CurrentCashierSessionError("CURRENT_CASHIER_REQUIRED");
    }
    if (
      this.active.isEmergencyOverride &&
      (this.active.authorizationActivatedAtSystemUptimeMs ===
        null ||
        this.active.authorizationExpiresAtSystemUptimeMs ===
          null ||
        sessionUptimeExpired(
          this.systemUptimeMs,
          this.active.authorizationActivatedAtSystemUptimeMs,
          this.active.authorizationExpiresAtSystemUptimeMs,
        ))
    ) {
      this.clear();
      try {
        this.onEmergencyExpired();
      } catch {
        // 到期后的可信会话与 lease 已先失效；外部 Keychain 清理失败不能恢复它们。
      }
      throw new CurrentCashierSessionError(
        "CURRENT_CASHIER_REQUIRED",
      );
    }
    return this.active;
  }

  public createLease(): TrustedCashierLease {
    const session = this.require();
    const epoch = session.epoch;
    return Object.freeze({
      get: () => {
        this.require();
        if (this.epoch !== epoch || this.active !== session) {
          throw new CurrentCashierSessionError(
            "CURRENT_CASHIER_REQUIRED",
          );
        }
        return session;
      },
    });
  }

  public clear(): void {
    if (this.epoch >= Number.MAX_SAFE_INTEGER) {
      throw new RangeError("Cashier session epoch is exhausted.");
    }
    this.epoch += 1;
    this.active = null;
  }
}

function toPublicSummary(session: TrustedCashierSession): PosCashierSummary {
  return Object.freeze({
    cashierId: session.cashierId,
    userGuid: session.userGuid,
    cashierName: session.cashierName,
    storeCode: session.storeCode,
    deviceCode: session.deviceCode,
    permissions: Object.freeze([...session.permissionCodes]),
    source: session.source,
  });
}

function requiredSessionText(value: unknown): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new CurrentCashierSessionError(
      "CASHIER_SESSION_IDENTITY_INVALID",
    );
  }
  return value.trim();
}

function optionalSessionText(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function requiredFutureEpoch(
  value: unknown,
  trustedNowEpochMs: unknown,
): number {
  const epoch =
    typeof value === "string" ? Date.parse(value) : Number.NaN;
  const nowEpochMs = requiredSafeTimeValue(
    trustedNowEpochMs,
  );
  if (
    !Number.isSafeInteger(epoch) ||
    !Number.isSafeInteger(nowEpochMs) ||
    epoch <= nowEpochMs
  ) {
    throw new CurrentCashierSessionError(
      "CASHIER_SESSION_IDENTITY_INVALID",
    );
  }
  return epoch;
}

function requiredSafeTimeValue(value: unknown): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0
  ) {
    throw new CurrentCashierSessionError(
      "CASHIER_SESSION_IDENTITY_INVALID",
    );
  }
  return value;
}

function sessionUptimeExpired(
  readSystemUptimeMs: () => number,
  activatedAtSystemUptimeMs: number,
  expiresAtSystemUptimeMs: number,
): boolean {
  try {
    const current = requiredSafeTimeValue(
      readSystemUptimeMs(),
    );
    return (
      current < activatedAtSystemUptimeMs ||
      current >= expiresAtSystemUptimeMs
    );
  } catch {
    return true;
  }
}

function defaultSystemUptimeMilliseconds(): number {
  const value = globalThis.performance?.now();
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new CurrentCashierSessionError(
      "CASHIER_SESSION_IDENTITY_INVALID",
    );
  }
  return Math.floor(value);
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error(`${label} is required.`);
  return normalized;
}
