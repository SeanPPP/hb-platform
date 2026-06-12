import { AppAsyncStorage } from "@/shared/storage/async-storage";
import type { PersistedDeviceSession } from "@/modules/device/types";

const DEVICE_INSTALLATION_ID_KEY = "hbweb_device_installation_id";
const DEVICE_SESSION_KEY = "hbweb_device_session";

function generateInstallationId() {
  return `hbmobile-${Date.now().toString(36)}-${Math.random()
    .toString(36)
    .slice(2, 10)}`;
}

export const DeviceStorage = {
  async getInstallationId() {
    const existingValue = await AppAsyncStorage.getString(DEVICE_INSTALLATION_ID_KEY);
    if (existingValue) {
      return existingValue;
    }

    const nextValue = generateInstallationId();
    await AppAsyncStorage.setString(DEVICE_INSTALLATION_ID_KEY, nextValue);
    return nextValue;
  },

  async getSession() {
    return AppAsyncStorage.getObject<PersistedDeviceSession>(DEVICE_SESSION_KEY);
  },

  async setSession(session: PersistedDeviceSession) {
    await AppAsyncStorage.setObject(DEVICE_SESSION_KEY, session);
  },

  async clearSession() {
    await AppAsyncStorage.removeItem(DEVICE_SESSION_KEY);
  },
};
