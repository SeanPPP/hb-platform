import {
  normalizePosIpadUpdatePolicy,
  type PosIpadUpdatePolicy,
  type PosIpadUpdatePolicyStorePort,
} from "../contracts/app-updates";

import type { SqliteConnectionPort } from "./types";

const UPDATE_POLICY_CACHE_KEY = "pos_ipad_update_policy_v1";

type SettingsRow = Readonly<{ setting_value: unknown }>;

/**
 * 更新策略是可公开、可重取的门禁数据；只缓存经契约校验的完整策略，绝不承载令牌、支付或设备凭据。
 */
export class PosIpadUpdatePolicyRepository
  implements PosIpadUpdatePolicyStorePort
{
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
  ) {}

  public async get(): Promise<PosIpadUpdatePolicy | null> {
    const row = await this.db.getFirst<SettingsRow>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?",
      [UPDATE_POLICY_CACHE_KEY],
    );
    if (!row || typeof row.setting_value !== "string") return null;
    try {
      return normalizePosIpadUpdatePolicy(JSON.parse(row.setting_value));
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
        [UPDATE_POLICY_CACHE_KEY, JSON.stringify(policy), this.nowIso()],
      );
    });
    return policy;
  }
}
