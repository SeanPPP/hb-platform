import type { StoredMobileDeviceAccountBinding } from "./types";

interface DeviceAccountLoginDependencies {
  recoverPendingActivation(): Promise<unknown>;
  loadBinding(): Promise<StoredMobileDeviceAccountBinding | null>;
}

export async function loadDeviceAccountBindingForLogin(
  dependencies: DeviceAccountLoginDependencies,
) {
  // 服务端可能已提交而兼容 DeviceStorage 尚未落盘；必须先完成精确恢复。
  await dependencies.recoverPendingActivation();
  const binding = await dependencies.loadBinding();
  if (!binding) {
    throw new Error("DEVICE_ACCOUNT_BINDING_NOT_FOUND");
  }
  return binding;
}
