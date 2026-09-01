import type { PendingMobileDeviceActivation } from "./types";

interface DeviceActivationExchangeIdentity {
  hardwareId: string;
  credential: string;
  apiHost: string;
}

interface DeviceActivationCommitBody {
  activationCode: string;
  credentialVerifier: string;
  deviceName?: string;
  hardwareId?: string;
  deviceSystem?: string;
  currentHardwareId?: string;
  currentCredential?: string;
}

interface PreparedDeviceActivationCommitRequest {
  body: DeviceActivationCommitBody;
  accessToken: string | null;
  skipAuthentication: boolean;
  recoveryOnly: boolean;
}

export async function prepareMobileDeviceActivationCommitRequest(
  pending: PendingMobileDeviceActivation,
  recoveryOnly: boolean,
  exchange: (
    identity: DeviceActivationExchangeIdentity,
  ) => Promise<{ accessToken: string }>,
): Promise<PreparedDeviceActivationCommitRequest> {
  if (pending.mode === "redeem") {
    return {
      body: {
        activationCode: pending.activationCode,
        hardwareId: pending.hardwareId,
        deviceSystem: pending.deviceSystem,
        credentialVerifier: pending.credentialVerifier,
        ...(pending.deviceName ? { deviceName: pending.deviceName } : {}),
      },
      accessToken: null,
      skipAuthentication: true,
      recoveryOnly,
    };
  }

  if (!pending.currentHardwareId || !pending.currentCredential) {
    throw new Error("DEVICE_ACCOUNT_REBIND_RECOVERY_IDENTITY_REQUIRED");
  }

  let accessToken: string | null = null;
  if (!recoveryOnly) {
    try {
      const exchanged = await exchange({
        hardwareId: pending.currentHardwareId,
        credential: pending.currentCredential,
        apiHost: pending.apiHost,
      });
      accessToken = exchanged.accessToken || null;
    } catch {
      // 旧账号失效时，后端仍可用旧设备凭据 + 新开通码原子校验并完成重绑。
      accessToken = null;
    }
  }

  return {
    body: {
      activationCode: pending.activationCode,
      credentialVerifier: pending.credentialVerifier,
      ...(pending.deviceName ? { deviceName: pending.deviceName } : {}),
      currentHardwareId: pending.currentHardwareId,
      currentCredential: pending.currentCredential,
    },
    accessToken,
    skipAuthentication: recoveryOnly || !accessToken,
    recoveryOnly,
  };
}
