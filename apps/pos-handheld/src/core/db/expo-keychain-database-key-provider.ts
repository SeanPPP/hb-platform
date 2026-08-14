import * as Crypto from "expo-crypto";

import type { SecureStorePort } from "../security/secure-storage";

import { KeychainDatabaseKeyProvider } from "./keychain-database-key-provider";

/** 生产组合：随机 SQLCipher 密钥由 Expo Crypto 生成，持久化仍由 Keychain 适配器完成。 */
export function createExpoKeychainDatabaseKeyProvider(
  secureStore: SecureStorePort,
): KeychainDatabaseKeyProvider {
  return new KeychainDatabaseKeyProvider(secureStore, async () => {
    const bytes = await Crypto.getRandomBytesAsync(32);
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
  });
}
