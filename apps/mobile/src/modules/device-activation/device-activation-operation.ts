import type {
  MobileDeviceActivationBinding,
  MobileDeviceActivationCommitResult,
  PendingMobileDeviceActivation,
  StoredMobileDeviceAccountBinding,
} from "./types";
import type { PersistedDeviceSession } from "@/modules/device/types";

interface DeviceActivationOperationDependencies {
  savePending(value: PendingMobileDeviceActivation): Promise<unknown>;
  clearPending(): Promise<unknown>;
  saveBinding(value: StoredMobileDeviceAccountBinding): Promise<unknown>;
  saveLegacyDeviceSession(value: PersistedDeviceSession): Promise<unknown>;
  commit(
    request: PendingMobileDeviceActivation,
    recoveryOnly: boolean,
  ): Promise<MobileDeviceActivationCommitResult>;
}

export class DeviceActivationRecoveryRequiredError extends Error {
  public constructor(cause?: unknown) {
    super("DEVICE_ACTIVATION_RECOVERY_REQUIRED", { cause });
    this.name = "DeviceActivationRecoveryRequiredError";
  }
}

export class DeviceActivationRejectedError extends Error {
  public readonly reasonCode: string;

  public constructor(result: MobileDeviceActivationCommitResult) {
    super(result.message || "DEVICE_ACTIVATION_REJECTED");
    this.name = "DeviceActivationRejectedError";
    this.reasonCode = result.reasonCode;
  }
}

async function persistSuccessfulBinding(
  pending: PendingMobileDeviceActivation,
  binding: MobileDeviceActivationBinding,
  dependencies: DeviceActivationOperationDependencies,
) {
  await dependencies.saveBinding({
    binding,
    apiHost: pending.apiHost,
    hardwareId: pending.hardwareId,
    credential: pending.credential,
  });
  // 兼容尚未迁移的移动端接口：同一原始凭据只通过 DeviceStorage 的 SecureStore 通道落盘。
  await dependencies.saveLegacyDeviceSession({
    hardwareId: pending.hardwareId,
    authCode: pending.credential,
    storeCode: binding.storeCode,
    storeName: binding.storeName,
    systemDeviceNumber: binding.deviceCode,
    status: 1,
    statusDescription: null,
    resolvedFromExisting: true,
  });
  await dependencies.clearPending();
  return binding;
}

function requireAllowedBinding(
  result: MobileDeviceActivationCommitResult,
): MobileDeviceActivationBinding {
  if (!result.isAllowed) {
    throw new DeviceActivationRejectedError(result);
  }
  if (!result.binding) {
    throw new Error("MOBILE_DEVICE_ACTIVATION_RESPONSE_INVALID");
  }
  return result.binding;
}

export async function commitMobileDeviceActivation(
  pending: PendingMobileDeviceActivation,
  dependencies: DeviceActivationOperationDependencies,
) {
  await dependencies.savePending(pending);

  try {
    const result = await dependencies.commit(pending, false);
    const binding = requireAllowedBinding(result);
    return await persistSuccessfulBinding(pending, binding, dependencies);
  } catch (error) {
    if (error instanceof DeviceActivationRejectedError) {
      await dependencies.clearPending();
      throw error;
    }
    // commit 已发出后，网络错误、异常响应以及本地落盘失败都不能证明服务端未提交。
    throw new DeviceActivationRecoveryRequiredError(error);
  }
}

export async function recoverPendingMobileDeviceActivation(
  pending: PendingMobileDeviceActivation,
  dependencies: DeviceActivationOperationDependencies,
) {
  try {
    const result = await dependencies.commit(pending, true);
    const binding = requireAllowedBinding(result);
    return await persistSuccessfulBinding(pending, binding, dependencies);
  } catch (error) {
    if (error instanceof DeviceActivationRejectedError) {
      await dependencies.clearPending();
      throw error;
    }
    throw new DeviceActivationRecoveryRequiredError(error);
  }
}
