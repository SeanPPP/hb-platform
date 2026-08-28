import type { SecureStorePort } from "../security/secure-storage";

import type { DatabaseKeyProviderPort } from "@hb/pos-db/core/db/types";

const databaseKeyName = "hbpos.ipad.sqlcipher-key.v1";
const thisDeviceOnly = { requireThisDeviceOnly: true };

/**
 * SQLCipher 密钥仅保存在此设备的 Keychain。App 数据删除或 Keychain 丢失时，
 * 按新设备流程重新审批；绝不把密钥放入 AsyncStorage 或日志。
 */
export class KeychainDatabaseKeyProvider implements DatabaseKeyProviderPort {
  public constructor(
    private readonly secureStore: SecureStorePort,
    private readonly createRandomKey: () => Promise<string>,
  ) {}

  public async getOrCreateDatabaseKey(): Promise<string> {
    const existing = await this.secureStore.get(databaseKeyName);
    if (existing) {
      return existing;
    }

    const key = await this.createRandomKey();
    if (!key) {
      throw new Error("Unable to generate a SQLCipher database key.");
    }
    await this.secureStore.set(databaseKeyName, key, thisDeviceOnly);
    return key;
  }
}
