import type {
  AttendanceRuntimeConnectivityPort,
} from "./production-attendance-audit-runtime";
import type {
  ProductionAttendanceAuditRuntimeConfiguration,
} from "./production-pos-service-composition";

import type {
  AttendanceQrCachePort,
  AttendanceQrCryptoPort,
  AttendanceSchedulerPort,
  AttendanceSecurityRemotePort,
  OperationAuditReadPort,
} from "@/features/attendance-audit";

export type ExpoAttendanceDeviceCredentials = Readonly<{
  authorizationCode: string;
  deviceCode: string;
  hardwareId: string;
  storeCode: string;
}>;

export type ExpoAttendanceRuntimeConfigurationInput = Readonly<{
  attendanceSecurity: AttendanceSecurityRemotePort;
  authorizationMarker: string;
  connectivity: AttendanceRuntimeConnectivityPort;
  credentials: ExpoAttendanceDeviceCredentials;
  localAudit: OperationAuditReadPort;
  qrCache: AttendanceQrCachePort;
  qrCrypto: AttendanceQrCryptoPort;
  readCurrentCredentials(): Promise<ExpoAttendanceDeviceCredentials | null>;
  readStoreName(): Promise<string>;
  remoteAudit: OperationAuditReadPort;
  scheduler: AttendanceSchedulerPort;
  sha256Hex(material: string): Promise<string>;
}>;

/**
 * Expo 组合根的考勤配置。初始凭据只用于冻结 scope；每次签发二维码前仍重新读取
 * Keychain，并同时复核授权码摘要、门店、设备和安装 UUID，避免重注册后的旧 runtime
 * 继续签发旧身份二维码。
 */
export function createExpoAttendanceRuntimeConfiguration(
  input: ExpoAttendanceRuntimeConfigurationInput,
): ProductionAttendanceAuditRuntimeConfiguration {
  const credentials = normalizeCredentials(input.credentials);
  const authorizationMarker = requiredText(
    input.authorizationMarker,
    "attendance authorization marker",
    256,
  ).toUpperCase();

  return Object.freeze({
    attendanceSecurity: input.attendanceSecurity,
    clock: Object.freeze({ now: Date.now }),
    connectivity: input.connectivity,
    deviceContext: Object.freeze({
      getDeviceContext: async () => {
        const current = await input.readCurrentCredentials();
        if (!current || !sameTerminalCredentials(current, credentials)) {
          return null;
        }
        const currentMarker = (
          await input.sha256Hex(current.authorizationCode)
        ).toUpperCase();
        if (currentMarker !== authorizationMarker) {
          return null;
        }

        let storeName = credentials.storeCode;
        try {
          storeName =
            optionalDisplayText(await input.readStoreName(), 256) ??
            credentials.storeCode;
        } catch {
          // 门店显示名不是签名身份的一部分；读取失败时安全回退受信任门店代码。
        }
        return Object.freeze({
          authorizationMarker,
          deviceCode: credentials.deviceCode,
          hardwareId: credentials.hardwareId,
          isAllowed: true,
          storeCode: credentials.storeCode,
          storeName,
        });
      },
    }),
    localAudit: input.localAudit,
    qrCache: input.qrCache,
    qrCrypto: input.qrCrypto,
    remoteAudit: input.remoteAudit,
    scheduler: input.scheduler,
  });
}

function sameTerminalCredentials(
  value: ExpoAttendanceDeviceCredentials,
  expected: ExpoAttendanceDeviceCredentials,
): boolean {
  return (
    value.authorizationCode === expected.authorizationCode &&
    value.deviceCode === expected.deviceCode &&
    value.hardwareId === expected.hardwareId &&
    value.storeCode === expected.storeCode
  );
}

function normalizeCredentials(
  value: ExpoAttendanceDeviceCredentials,
): ExpoAttendanceDeviceCredentials {
  return Object.freeze({
    authorizationCode: requiredText(
      value.authorizationCode,
      "attendance authorization code",
      4_096,
    ),
    deviceCode: requiredText(
      value.deviceCode,
      "attendance device code",
      128,
    ),
    hardwareId: requiredText(
      value.hardwareId,
      "attendance hardware id",
      256,
    ),
    storeCode: requiredText(
      value.storeCode,
      "attendance store code",
      50,
    ),
  });
}

function requiredText(
  value: unknown,
  label: string,
  maximumLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maximumLength ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new TypeError(`${label} is invalid.`);
  }
  return value;
}

function optionalDisplayText(
  value: unknown,
  maximumLength: number,
): string | null {
  if (typeof value !== "string") return null;
  const normalized = value
    .replace(/[\u0000-\u001f\u007f]/gu, " ")
    .trim();
  return normalized.length > 0 && normalized.length <= maximumLength
    ? normalized
    : null;
}
