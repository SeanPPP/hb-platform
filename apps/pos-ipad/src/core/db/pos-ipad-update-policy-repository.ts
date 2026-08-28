import {
  normalizePosIpadUpdatePolicy,
  type PosIpadUpdatePolicy,
  type PosIpadUpdatePolicyStorePort,
} from "../contracts/app-updates";
import {
  normalizeAppUpdateCacheScope,
  type AppUpdateCacheScope,
} from "../contracts/ota-app-updates";

import {
  createAppUpdateCacheKey,
  createStoredAppUpdateCacheScope,
  isExactRecord,
  matchesStoredAppUpdateCacheScope,
  type StoredAppUpdateCacheScope,
} from "./scoped-app-update-cache";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

const NATIVE_POLICY_VERSION = "native-v1";
const NATIVE_POLICY_CACHE_PREFIX =
  "pos_ipad_native_update_policy_v2";

type SettingsRow = Readonly<{ setting_value: unknown }>;

/**
 * 更新策略是可公开、可重取的门禁数据；只缓存经契约校验的完整策略，绝不承载令牌、支付或设备凭据。
 */
export class PosIpadUpdatePolicyRepository
  implements PosIpadUpdatePolicyStorePort
{
  private readonly scope: AppUpdateCacheScope;
  private readonly storedScope: StoredAppUpdateCacheScope;
  private readonly cacheKey: string;

  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
    scope: AppUpdateCacheScope,
  ) {
    this.scope = normalizeAppUpdateCacheScope(scope);
    this.storedScope = createStoredAppUpdateCacheScope(
      this.scope,
      NATIVE_POLICY_VERSION,
    );
    this.cacheKey = createAppUpdateCacheKey(
      NATIVE_POLICY_CACHE_PREFIX,
      this.scope,
      NATIVE_POLICY_VERSION,
    );
  }

  public async get(): Promise<PosIpadUpdatePolicy | null> {
    const row = await this.db.getFirst<SettingsRow>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?",
      [this.cacheKey],
    );
    if (!row || typeof row.setting_value !== "string") return null;
    try {
      const envelope: unknown = JSON.parse(row.setting_value);
      if (
        !isExactRecord(envelope, ["scope", "policy"]) ||
        !matchesStoredAppUpdateCacheScope(
          envelope.scope,
          this.storedScope,
        )
      ) {
        return null;
      }
      return normalizePosIpadUpdatePolicy(envelope.policy);
    } catch {
      // 损坏或越界缓存不能放行新交易；协调器会继续尝试远端刷新。
      return null;
    }
  }

  public async save(
    input: PosIpadUpdatePolicy,
  ): Promise<PosIpadUpdatePolicy> {
    const policy = normalizePosIpadUpdatePolicy(input);
    await this.db.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        `INSERT INTO app_settings (setting_key, setting_value, updated_at_iso)
         VALUES (?, ?, ?)
         ON CONFLICT(setting_key) DO UPDATE SET
           setting_value = excluded.setting_value,
           updated_at_iso = excluded.updated_at_iso`,
        [
          this.cacheKey,
          JSON.stringify({
            scope: this.storedScope,
            policy,
          }),
          this.nowIso(),
        ],
      );
    });
    return policy;
  }
}
