import * as Crypto from "expo-crypto";
import * as SecureStore from "expo-secure-store";

export interface StaffBarcodeQueueStorage {
  get: (actorGuid: string, storeCode: string, operationScope?: string) => Promise<string | null>;
  set: (actorGuid: string, storeCode: string, value: string | null, operationScope?: string) => Promise<void>;
}

async function storageKey(actorGuid: string, storeCode: string, operationScope = "batch") {
  const scope = await Crypto.digestStringAsync(
    Crypto.CryptoDigestAlgorithm.SHA256,
    `${actorGuid.trim()}\u0000${storeCode.trim().toUpperCase()}\u0000${operationScope.trim()}`
  );
  return `hb.staff-cashier-print.v1.${scope}`;
}

export const staffBarcodeQueueSecureStorage: StaffBarcodeQueueStorage = {
  async get(actorGuid, storeCode, operationScope) {
    return SecureStore.getItemAsync(await storageKey(actorGuid, storeCode, operationScope));
  },
  async set(actorGuid, storeCode, value, operationScope) {
    const key = await storageKey(actorGuid, storeCode, operationScope);
    if (value === null) {
      await SecureStore.deleteItemAsync(key);
      return;
    }
    await SecureStore.setItemAsync(key, value, {
      keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    });
  },
};
