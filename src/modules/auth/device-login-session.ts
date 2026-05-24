import type { DeviceProfile, PersistedDeviceSession } from "@/modules/device/types";

interface DeviceLoginSessionDependencies {
  clearAccountSession: () => Promise<void>;
  syncDeviceFromProfile: (profile: DeviceProfile) => Promise<PersistedDeviceSession>;
  validateDevice: () => Promise<boolean>;
}

interface StoredDeviceSessionDependencies {
  clearAccountSession: () => Promise<void>;
  validateDevice: () => Promise<boolean>;
}

export async function prepareDeviceLoginSession(
  profile: DeviceProfile,
  dependencies: DeviceLoginSessionDependencies
) {
  await dependencies.clearAccountSession();
  await dependencies.syncDeviceFromProfile(profile);
  return dependencies.validateDevice();
}

export async function prepareStoredDeviceSession(dependencies: StoredDeviceSessionDependencies) {
  await dependencies.clearAccountSession();
  return dependencies.validateDevice();
}
