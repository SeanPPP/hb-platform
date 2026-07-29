import { ALL_POS_TERMINAL_PERMISSIONS } from "../contracts/pos-terminal-permissions";
import type { EmergencyCashierAuthenticationConfiguration } from "../security/cashier-authentication";
import type { CashierAuthorizationWrite } from "../security/secure-storage";

import {
  EmergencyLoginSecurityService,
  EmergencyPublicKeySyncService,
  type EmergencyLoginCryptoPort,
  type EmergencyPublicKeyCachePort,
  type EmergencySystemUptimePort,
  type EmergencyTrustedTimePort,
} from "@/features/attendance-audit/emergency-login-security";
import type { AttendanceSecurityRemotePort } from "@/features/attendance-audit/hbpos-attendance-security-api";

export type EmergencyCashierRuntime = Readonly<{
  authentication: EmergencyCashierAuthenticationConfiguration;
  syncPublicKeys(): Promise<boolean>;
}>;

/**
 * 紧急 token 的验证、可信时间和 Keychain 写入都留在组合根；普通收银认证只取得
 * 一个窄的 verifyAndActivate Port，无法读取公钥包或持久化高水位。
 */
export function createEmergencyCashierRuntime(
  input: Readonly<{
    authorization: Readonly<{
      set(value: CashierAuthorizationWrite): Promise<void>;
    }>;
    cache: EmergencyPublicKeyCachePort;
    crypto: EmergencyLoginCryptoPort;
    remote: AttendanceSecurityRemotePort;
    systemUptime: EmergencySystemUptimePort;
    trustedTime: EmergencyTrustedTimePort;
  }>,
): EmergencyCashierRuntime {
  const sync = new EmergencyPublicKeySyncService({
    cache: input.cache,
    crypto: input.crypto,
    remote: input.remote,
    systemUptime: input.systemUptime,
    trustedTime: input.trustedTime,
  });
  const service = new EmergencyLoginSecurityService({
    cache: input.cache,
    crypto: input.crypto,
    session: {
      activateEmergencyOverride: (session) =>
        input.authorization.set({
          authorizationToken: session.authorizationToken,
          expiresAtEpochMs: session.expiresAtEpochMs,
          source: "emergency-override",
          systemUptimeMs: session.systemUptimeMs,
          trustedNowEpochMs: session.trustedNowEpochMs,
        }),
    },
    sync,
    systemUptime: input.systemUptime,
    trustedTime: input.trustedTime,
  });

  return Object.freeze({
    authentication: Object.freeze({
      permissionCodes: ALL_POS_TERMINAL_PERMISSIONS,
      service,
    }),
    syncPublicKeys: () => sync.sync(),
  });
}
