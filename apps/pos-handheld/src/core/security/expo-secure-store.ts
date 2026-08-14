import * as ExpoSecureStore from "expo-secure-store";

import type { SecureStorePort, SecureStoreWriteOptions } from "./secure-storage";

export class ExpoSecureStoreAdapter implements SecureStorePort {
  public get(key: string): Promise<string | null> {
    return ExpoSecureStore.getItemAsync(key);
  }

  public async set(key: string, value: string, options: SecureStoreWriteOptions): Promise<void> {
    await ExpoSecureStore.setItemAsync(key, value, {
      keychainAccessible: options.requireThisDeviceOnly
        ? ExpoSecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY
        : ExpoSecureStore.WHEN_UNLOCKED
    });
  }

  public remove(key: string): Promise<void> {
    return ExpoSecureStore.deleteItemAsync(key);
  }
}
