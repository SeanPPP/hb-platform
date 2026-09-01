import * as SecureStore from "expo-secure-store";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import {
  createDeviceAccountStorage,
  type DeviceAccountKeyValueStorage,
} from "./device-account-storage";

const secureStorageAdapter: DeviceAccountKeyValueStorage = {
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

const presentationStorageAdapter: DeviceAccountKeyValueStorage = {
  getItem: (key) => AppAsyncStorage.getString(key),
  setItem: (key, value) => AppAsyncStorage.setString(key, value),
  removeItem: (key) => AppAsyncStorage.removeItem(key),
};

export const DeviceAccountStorage = createDeviceAccountStorage({
  secure: secureStorageAdapter,
  presentation: presentationStorageAdapter,
});
