import * as SecureStore from "expo-secure-store";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import {
  createDeviceStorage,
  type DeviceStorageKeyValuePort,
} from "@/modules/device/device-storage-core";

function generateInstallationId() {
  return `hbmobile-${Date.now().toString(36)}-${Math.random()
    .toString(36)
    .slice(2, 10)}`;
}

const presentationStorage: DeviceStorageKeyValuePort = {
  getItem: (key) => AppAsyncStorage.getString(key),
  setItem: (key, value) => AppAsyncStorage.setString(key, value),
  removeItem: (key) => AppAsyncStorage.removeItem(key),
};

const sensitiveStorage: DeviceStorageKeyValuePort = {
  getItem: (key) => SecureStore.getItemAsync(key),
  setItem: async (key, value) => {
    await SecureStore.setItemAsync(key, value, {
      keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    });
  },
  removeItem: async (key) => {
    await SecureStore.deleteItemAsync(key);
  },
};

export const DeviceStorage = createDeviceStorage({
  presentation: presentationStorage,
  sensitive: sensitiveStorage,
  generateInstallationId,
});
