import {
  normalizeAppUpdateCacheScope,
  normalizePosIpadOtaUpdatePolicy,
  type AppUpdateCacheScope,
  type PosIpadOtaUpdatePolicy,
  type PosIpadOtaUpdatePolicyStorePort,
} from "../contracts/ota-app-updates";

import {
  createAppUpdateCacheKey,
  createStoredAppUpdateCacheScope,
  isExactRecord,
  matchesStoredAppUpdateCacheScope,
  normalizePolicyVersion,
} from "./scoped-app-update-cache";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

const OTA_POINTER_PREFIX = "pos_ipad_ota_update_policy_v1:pointer";
const OTA_RECORD_PREFIX = "pos_ipad_ota_update_policy_v1:record";

type SettingsRow = Readonly<{ setting_value: unknown }>;

/**
 * pointer 只按四项设备 scope 定位最近策略；不可变 record key 额外包含 policyVersion，
 * 避免换门店、runtime、原生版本或策略版本后误读旧 OTA。
 */
export class PosIpadOtaUpdatePolicyRepository
  implements PosIpadOtaUpdatePolicyStorePort
{
  private readonly scope: AppUpdateCacheScope;
  private readonly pointerKey: string;

  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
    scope: AppUpdateCacheScope,
  ) {
    this.scope = normalizeAppUpdateCacheScope(scope);
    this.pointerKey = createAppUpdateCacheKey(
      OTA_POINTER_PREFIX,
      this.scope,
    );
  }

  public async get(): Promise<PosIpadOtaUpdatePolicy | null> {
    const pointer = await this.db.getFirst<SettingsRow>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?",
      [this.pointerKey],
    );
    if (!pointer || typeof pointer.setting_value !== "string") return null;

    let policyVersion: string;
    try {
      const parsed: unknown = JSON.parse(pointer.setting_value);
      if (!isExactRecord(parsed, ["policyVersion"])) return null;
      policyVersion = normalizePolicyVersion(parsed.policyVersion);
    } catch {
      return null;
    }

    const recordKey = createAppUpdateCacheKey(
      OTA_RECORD_PREFIX,
      this.scope,
      policyVersion,
    );
    const record = await this.db.getFirst<SettingsRow>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?",
      [recordKey],
    );
    if (!record || typeof record.setting_value !== "string") return null;
    try {
      const parsed: unknown = JSON.parse(record.setting_value);
      const expectedScope = createStoredAppUpdateCacheScope(
        this.scope,
        policyVersion,
      );
      if (
        !isExactRecord(parsed, ["scope", "policy"]) ||
        !matchesStoredAppUpdateCacheScope(
          parsed.scope,
          expectedScope,
        )
      ) {
        return null;
      }
      const policy = normalizePosIpadOtaUpdatePolicy(parsed.policy);
      return policy.policyVersion === policyVersion ? policy : null;
    } catch {
      return null;
    }
  }

  public async save(
    input: PosIpadOtaUpdatePolicy,
  ): Promise<PosIpadOtaUpdatePolicy> {
    const policy = normalizePosIpadOtaUpdatePolicy(input);
    const policyVersion = normalizePolicyVersion(policy.policyVersion);
    const recordKey = createAppUpdateCacheKey(
      OTA_RECORD_PREFIX,
      this.scope,
      policyVersion,
    );
    const storedScope = createStoredAppUpdateCacheScope(
      this.scope,
      policyVersion,
    );
    const updatedAt = this.nowIso();
    await this.db.withExclusiveTransaction(async (transaction) => {
      await upsertSetting(
        transaction,
        recordKey,
        JSON.stringify({ scope: storedScope, policy }),
        updatedAt,
      );
      await upsertSetting(
        transaction,
        this.pointerKey,
        JSON.stringify({ policyVersion }),
        updatedAt,
      );
    });
    return policy;
  }
}

function upsertSetting(
  db: SqliteConnectionPort,
  key: string,
  value: string,
  updatedAtIso: string,
): Promise<unknown> {
  return db.run(
    `INSERT INTO app_settings (setting_key, setting_value, updated_at_iso)
     VALUES (?, ?, ?)
     ON CONFLICT(setting_key) DO UPDATE SET
       setting_value = excluded.setting_value,
       updated_at_iso = excluded.updated_at_iso`,
    [key, value, updatedAtIso],
  );
}
