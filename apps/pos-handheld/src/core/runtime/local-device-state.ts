import type {
  PendingDeviceRegistration,
  StoredDeviceCredentials,
} from "../security/secure-storage";

import type { RuntimeDeviceState } from "./pos-runtime";

export type LocalDeviceEvidence = Readonly<{
  locked: boolean;
  registrationResetPending?: boolean;
  installationId: string;
  credentials: StoredDeviceCredentials | null;
  pending: PendingDeviceRegistration | null;
}>;

/**
 * 离线授权必须把 Keychain 凭据绑定到当前不可同步的安装 UUID。
 * 只看“记录存在”会让换机、Keychain 残留或旧格式数据绕过设备审批。
 */
export function resolveLocalDeviceState(
  input: LocalDeviceEvidence,
): Exclude<RuntimeDeviceState, "unknown"> {
  if (input.locked || input.registrationResetPending === true) {
    return "locked";
  }
  if (isCurrentInstallationCredential(input.credentials, input.installationId)) {
    return "authorized-local";
  }
  if (isValidPendingRegistration(input.pending)) {
    return "pending-approval";
  }
  return "registration-required";
}

function isCurrentInstallationCredential(
  credentials: StoredDeviceCredentials | null,
  installationId: string,
): boolean {
  return (
    credentials !== null &&
    nonEmpty(installationId) &&
    credentials.hardwareId === installationId &&
    nonEmpty(credentials.deviceCode) &&
    nonEmpty(credentials.storeCode) &&
    nonEmpty(credentials.authorizationCode)
  );
}

function isValidPendingRegistration(
  pending: PendingDeviceRegistration | null,
): boolean {
  return (
    pending !== null &&
    nonEmpty(pending.deviceCode) &&
    nonEmpty(pending.storeCode)
  );
}

function nonEmpty(value: string): boolean {
  return value.trim().length > 0;
}
