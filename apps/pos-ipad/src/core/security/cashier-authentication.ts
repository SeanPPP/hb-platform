import { HbposApiError, type CashierSessionDto } from "../api/hbpos-api";

import { CashierAuthorizationStore, CashierSessionCache, DeviceLockStore } from "./secure-storage";

export interface CashierAuthenticationApi {
  barcodeLogin(input: Readonly<{
    storeCode: string;
    deviceCode: string;
    userBarcode: string;
  }>): Promise<CashierSessionDto>;
}

export interface NetworkStatusPort {
  isOnline(): Promise<boolean>;
}

export type EmergencyCashierLoginResult =
  | Readonly<{
      ok: true;
      emergencyGrantId: string;
      expiresAtEpochMs: number;
      systemUptimeMs: number;
      trustedNowEpochMs: number;
    }>
  | Readonly<{ ok: false; errorCode: string }>;

export interface EmergencyCashierLoginPort {
  verifyAndActivate(
    token: string,
    device: Readonly<{ storeCode: string; deviceCode: string }>,
  ): Promise<EmergencyCashierLoginResult>;
}

export type EmergencyCashierAuthenticationConfiguration = Readonly<{
  permissionCodes: readonly string[];
  service: EmergencyCashierLoginPort;
}>;

export type CashierLoginResult = Readonly<{
  emergencyTiming?: Readonly<{
    systemUptimeMs: number;
    trustedNowEpochMs: number;
  }>;
  source: "online" | "offline-cache" | "emergency-override";
  session: CashierSessionDto;
}>;

export class CashierAuthenticationService {
  private readonly deviceLock: DeviceLockStore;
  private readonly emergencyPermissions: readonly string[];

  public constructor(
    private readonly api: CashierAuthenticationApi,
    private readonly cache: CashierSessionCache,
    private readonly network: NetworkStatusPort,
    private readonly authorizationStore?: CashierAuthorizationStore,
    deviceLock?: DeviceLockStore,
    private readonly emergency?: EmergencyCashierAuthenticationConfiguration,
  ) {
    this.deviceLock = deviceLock ?? new DeviceLockStore(cache.secureStore);
    this.emergencyPermissions = Object.freeze(
      [...new Set(emergency?.permissionCodes.map(requiredPermission) ?? [])]
        .sort(),
    );
  }

  public async login(input: Readonly<{
    storeCode: string;
    deviceCode: string;
    userBarcode: string;
  }>): Promise<CashierLoginResult> {
    if (await this.deviceLock.isLocked()) {
      throw new HbposApiError("Device is disabled and must be re-authorized online.", {
        kind: "http",
        status: 403,
        code: "DEVICE_LOCKED"
      });
    }
    if (hasEmergencyPrefix(input.userBarcode)) {
      return this.loginEmergency(input);
    }
    if (!(await this.network.isOnline())) {
      return this.loadCached(input);
    }

    try {
      const session = await this.api.barcodeLogin(input);
      await this.cache.save(input.storeCode, input.deviceCode, input.userBarcode, session);
      await this.activateAuthorization(session, "online");
      return { source: "online", session };
    } catch (error: unknown) {
      // 关键逻辑：瞬时网络/服务端失败可回退；401/403/其他 4xx 与 envelope 均是明确在线拒绝。
      if (isCacheFallbackEligible(error)) {
        return this.loadCached(input);
      }
      throw error;
    }
  }

  private async loginEmergency(input: Readonly<{
    storeCode: string;
    deviceCode: string;
    userBarcode: string;
  }>): Promise<CashierLoginResult> {
    if (!this.emergency) {
      throw new HbposApiError("Emergency login is unavailable.", {
        kind: "envelope",
        code: "EMERGENCY_LOGIN_SERVICE_UNAVAILABLE",
      });
    }
    const verified = await this.emergency.service.verifyAndActivate(
      input.userBarcode,
      {
        storeCode: input.storeCode,
        deviceCode: input.deviceCode,
      },
    );
    if (!verified.ok) {
      throw new HbposApiError("Emergency login was rejected.", {
        kind: "envelope",
        code: requiredEmergencyErrorCode(verified.errorCode),
      });
    }
    const grantId = requiredUuid(verified.emergencyGrantId);
    if (
      !Number.isSafeInteger(verified.expiresAtEpochMs) ||
      !Number.isSafeInteger(verified.trustedNowEpochMs) ||
      !Number.isSafeInteger(verified.systemUptimeMs) ||
      verified.trustedNowEpochMs < 0 ||
      verified.systemUptimeMs < 0 ||
      verified.expiresAtEpochMs <= verified.trustedNowEpochMs
    ) {
      throw invalidEmergencyResult();
    }
    const compactGrantId = grantId.replaceAll("-", "");
    const emergencyIdentity = `EMERGENCY:${compactGrantId}`;
    return {
      emergencyTiming: {
        systemUptimeMs: verified.systemUptimeMs,
        trustedNowEpochMs: verified.trustedNowEpochMs,
      },
      source: "emergency-override",
      session: {
        cashierId: emergencyIdentity,
        userGuid: emergencyIdentity,
        cashierName: "EMERGENCY",
        storeCode: requiredScopeText(input.storeCode),
        deviceCode: requiredScopeText(input.deviceCode),
        roles: ["EmergencyOverride"],
        permissionCodes: [...this.emergencyPermissions],
        allowedStoreCodes: [requiredScopeText(input.storeCode)],
        isSuperAdmin: false,
        isOfflineCached: false,
        isEmergencyOverride: true,
        authorizationToken: input.userBarcode,
        authorizationExpiresAtUtc: new Date(
          verified.expiresAtEpochMs,
        ).toISOString(),
        emergencyGrantId: grantId,
      },
    };
  }

  private async loadCached(input: Readonly<{
    storeCode: string;
    deviceCode: string;
    userBarcode: string;
  }>): Promise<CashierLoginResult> {
    const session = await this.cache.load(input.storeCode, input.deviceCode, input.userBarcode);
    if (!session) {
      throw new HbposApiError("Cashier is not available in the local offline cache.", {
        kind: "transport",
        code: "OFFLINE_CASHIER_NOT_CACHED"
      });
    }
    await this.activateAuthorization(session, "offline-cache");
    return { source: "offline-cache", session };
  }

  private async activateAuthorization(
    session: CashierSessionDto,
    source: "online" | "offline-cache",
  ): Promise<void> {
    if (!this.authorizationStore) {
      return;
    }
    const expiresAtEpochMs =
      typeof session.authorizationExpiresAtUtc === "string"
        ? Date.parse(session.authorizationExpiresAtUtc)
        : Number.NaN;
    if (
      !session.authorizationToken ||
      !Number.isSafeInteger(expiresAtEpochMs) ||
      expiresAtEpochMs < 0
    ) {
      await this.authorizationStore.clear();
      return;
    }
    await this.authorizationStore.set({
      authorizationToken: session.authorizationToken,
      expiresAtEpochMs,
      source,
    });
  }
}

function isCacheFallbackEligible(error: unknown): boolean {
  if (!(error instanceof HbposApiError)) {
    return false;
  }
  if (error.kind === "transport") {
    return true;
  }
  return error.kind === "http"
    && (error.status === 408 || error.status === 429 || (error.status !== undefined && error.status >= 500));
}

function hasEmergencyPrefix(value: string): boolean {
  return value.startsWith("HBPOSE1-") || value.startsWith("HBPOSE2-");
}

function requiredPermission(value: string): string {
  const normalized = value.trim();
  if (!normalized.startsWith("Permissions.PosTerminal.")) {
    throw new TypeError("Emergency permission must be POS-scoped.");
  }
  return normalized;
}

function requiredScopeText(value: string): string {
  const normalized = value.trim();
  if (!normalized) throw invalidEmergencyResult();
  return normalized;
}

function requiredEmergencyErrorCode(value: string): string {
  const normalized = value.trim();
  if (!/^EMERGENCY_[A-Z0-9_]{1,96}$/u.test(normalized)) {
    throw invalidEmergencyResult();
  }
  return normalized;
}

function requiredUuid(value: string): string {
  const normalized = value.trim().toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw invalidEmergencyResult();
  }
  return normalized;
}

function invalidEmergencyResult(): HbposApiError {
  return new HbposApiError("Emergency login response is invalid.", {
    kind: "envelope",
    code: "EMERGENCY_LOGIN_RESPONSE_INVALID",
  });
}
