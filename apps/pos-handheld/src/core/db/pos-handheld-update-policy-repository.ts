import {
  normalizePosHandheldUpdatePolicy,
  type PosHandheldUpdatePolicy,
  type PosHandheldUpdatePolicyStorePort,
} from "../contracts/app-updates";
import {
  normalizeNativeAppUpdateCacheScope,
  type AppUpdateCacheScope,
  type NativeAppUpdateCacheScope,
} from "../contracts/ota-app-updates";

import {
  createAppUpdateCacheKey,
  createStoredAppUpdateCacheScope,
  isExactRecord,
  matchesStoredAppUpdateCacheScope,
  type StoredAppUpdateCacheScope,
} from "./scoped-app-update-cache";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

const NATIVE_POLICY_VERSION = "native-v3";
const NATIVE_POLICY_CACHE_PREFIX =
  "pos_handheld_native_update_policy_v4";

type SettingsRow = Readonly<{ setting_value: unknown }>;

/**
 * 更新策略是可公开、可重取的门禁数据；只缓存经契约校验的完整策略，绝不承载令牌、支付或设备凭据。
 */
export class PosHandheldUpdatePolicyRepository
  implements PosHandheldUpdatePolicyStorePort
{
  private readonly scope: NativeAppUpdateCacheScope;
  private readonly storedScope: StoredAppUpdateCacheScope;
  private readonly cacheKey: string;

  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
    scope: AppUpdateCacheScope,
  ) {
    this.scope = normalizeNativeAppUpdateCacheScope(scope);
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

  public async get(): Promise<PosHandheldUpdatePolicy | null> {
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
      const policy = normalizePosHandheldUpdatePolicy(envelope.policy);
      return isCachedNativePolicyApplicable(policy, this.scope)
        ? policy
        : null;
    } catch {
      // 损坏或越界缓存不能放行新交易；协调器会继续尝试远端刷新。
      return null;
    }
  }

  public async save(
    input: PosHandheldUpdatePolicy,
  ): Promise<PosHandheldUpdatePolicy> {
    const policy = normalizePosHandheldUpdatePolicy(input);
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

function isCachedNativePolicyApplicable(
  policy: PosHandheldUpdatePolicy,
  scope: NativeAppUpdateCacheScope,
): boolean {
  if (policy.platform !== scope.platform) return false;
  // none 不携带 target；optional 与 required 都必须确认目标确实新于当前安装。
  if (policy.state === "none") return true;
  if (policy.latestVersion === null || policy.latestBuild === null) {
    return false;
  }

  const versionComparison = compareVersions(
    scope.installedVersion,
    policy.latestVersion,
  );
  if (versionComparison !== null && versionComparison > 0) return false;
  if (versionComparison !== 0) return true;

  const installedBuild = parseBuild(scope.installedBuild);
  const targetBuild = parseBuild(policy.latestBuild);
  return (
    installedBuild === null ||
    targetBuild === null ||
    installedBuild < targetBuild
  );
}

function compareVersions(left: string, right: string): number | null {
  const leftParts = parseVersion(left);
  const rightParts = parseVersion(right);
  if (leftParts === null || rightParts === null) {
    return left === right ? 0 : null;
  }
  const length = Math.max(leftParts.length, rightParts.length);
  for (let index = 0; index < length; index += 1) {
    const difference = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (difference !== 0) return difference > 0 ? 1 : -1;
  }
  return 0;
}

function parseVersion(value: string): readonly number[] | null {
  const normalized = value.replace(/^v/iu, "");
  if (!/^\d+(?:\.\d+){0,3}$/u.test(normalized)) return null;
  const parts = normalized.split(".").map(Number);
  return parts.every(Number.isSafeInteger) ? parts : null;
}

function parseBuild(value: string): number | null {
  if (!/^\d+$/u.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : null;
}
