import {
  normalizeOtaAppUpdateCacheScope,
  normalizePosHandheldOtaUpdatePolicy,
  type AppUpdateCacheScope,
  type OtaAppUpdateCacheScope,
  type PosHandheldOtaUpdatePolicy,
  type PosHandheldOtaUpdatePolicyStorePort,
} from "../contracts/ota-app-updates";

import {
  createAppUpdateCacheKey,
  createStoredAppUpdateCacheScope,
  isExactRecord,
  matchesStoredAppUpdateCacheScope,
  normalizePolicyVersion,
} from "./scoped-app-update-cache";
import type { SqliteConnectionPort } from "./types";

const OTA_POINTER_PREFIX = "pos_handheld_ota_update_policy_v3:pointer";
const OTA_RECORD_PREFIX = "pos_handheld_ota_update_policy_v3:record";

type SettingsRow = Readonly<{ setting_value: unknown }>;

/**
 * pointer 固定到完整 EAS/Expo 启动身份；不可变 record key 额外包含 policyVersion，
 * 避免换 project、channel、runtime 或已安装 update 后误读旧 OTA。
 */
export class PosHandheldOtaUpdatePolicyRepository
  implements PosHandheldOtaUpdatePolicyStorePort
{
  private readonly scope: OtaAppUpdateCacheScope;
  private readonly pointerKey: string;

  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
    scope: AppUpdateCacheScope,
  ) {
    this.scope = normalizeOtaAppUpdateCacheScope(scope);
    this.pointerKey = createAppUpdateCacheKey(
      OTA_POINTER_PREFIX,
      this.scope,
    );
  }

  public async get(): Promise<PosHandheldOtaUpdatePolicy | null> {
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
      const policy = normalizePosHandheldOtaUpdatePolicy(parsed.policy);
      if (policy.policyVersion !== policyVersion) return null;
      return isCachedOtaPolicyApplicable(policy, this.scope) ? policy : null;
    } catch {
      return null;
    }
  }

  public async save(
    input: PosHandheldOtaUpdatePolicy,
  ): Promise<PosHandheldOtaUpdatePolicy> {
    const policy = normalizePosHandheldOtaUpdatePolicy(input);
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

function isCachedOtaPolicyApplicable(
  policy: PosHandheldOtaUpdatePolicy,
  scope: OtaAppUpdateCacheScope,
): boolean {
  if (
    policy.appKey !== scope.appKey ||
    policy.platform !== scope.platform ||
    (policy.projectName !== null &&
      policy.projectName !== scope.projectName) ||
    (policy.channel !== null &&
      policy.channel !== scope.configuredChannel) ||
    (policy.runtimeVersion !== null &&
      policy.runtimeVersion !== scope.runtimeVersion)
  ) {
    return false;
  }
  if (policy.state === "none") return true;
  if (
    scope.projectName === null ||
    scope.configuredChannel === null ||
    policy.projectName !== scope.projectName ||
    policy.channel !== scope.configuredChannel ||
    policy.runtimeVersion !== scope.runtimeVersion
  ) {
    return false;
  }
  if (
    scope.currentUpdateId !== null &&
    updateIdentifiersEqual(scope.currentUpdateId, policy.updateId)
  ) {
    return false;
  }
  return (
    scope.currentUpdateGroupId === null ||
    scope.currentUpdateGroupId !== policy.updateGroupId
  );
}

function updateIdentifiersEqual(left: string, right: string): boolean {
  const uuidPattern =
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
  return uuidPattern.test(left) && uuidPattern.test(right)
    ? left.toLowerCase() === right.toLowerCase()
    : left === right;
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
