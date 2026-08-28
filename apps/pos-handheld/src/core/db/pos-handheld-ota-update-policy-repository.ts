import {
  isTrustedPosHandheldOtaChannel,
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

const OTA_POINTER_PREFIX = "pos_handheld_ota_update_policy_v4:pointer";
const OTA_RECORD_PREFIX = "pos_handheld_ota_update_policy_v4:record";

type SettingsRow = Readonly<{ setting_value: unknown }>;
type OtaPolicyTargetIdentity = Readonly<{
  policyVersion: string;
  releaseChannel: string | null;
  updateId: string | null;
  updateGroupId: string | null;
}>;

/**
 * pointer 固定到完整 EAS/Expo 目标身份；不可变 record key 同时包含
 * policyVersion、release channel、update ID 与 group ID，禁止 legacy/release channel 跨 scope 复用。
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

    let target: OtaPolicyTargetIdentity;
    try {
      const parsed: unknown = JSON.parse(pointer.setting_value);
      target = normalizeTargetIdentity(parsed);
    } catch {
      return null;
    }

    const recordKey = createOtaRecordKey(this.scope, target);
    const record = await this.db.getFirst<SettingsRow>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?",
      [recordKey],
    );
    if (!record || typeof record.setting_value !== "string") return null;
    try {
      const parsed: unknown = JSON.parse(record.setting_value);
      const expectedScope = createStoredAppUpdateCacheScope(
        this.scope,
        target.policyVersion,
      );
      if (
        !isExactRecord(parsed, ["scope", "target", "policy"]) ||
        !matchesStoredAppUpdateCacheScope(
          parsed.scope,
          expectedScope,
        ) ||
        !targetIdentitiesEqual(
          normalizeTargetIdentity(parsed.target),
          target,
        )
      ) {
        return null;
      }
      const policy = normalizePosHandheldOtaUpdatePolicy(parsed.policy);
      if (
        !targetIdentitiesEqual(createTargetIdentity(policy), target)
      ) {
        return null;
      }
      return isCachedOtaPolicyApplicable(policy, this.scope) ? policy : null;
    } catch {
      return null;
    }
  }

  public async save(
    input: PosHandheldOtaUpdatePolicy,
  ): Promise<PosHandheldOtaUpdatePolicy> {
    const policy = normalizePosHandheldOtaUpdatePolicy(input);
    const target = createTargetIdentity(policy);
    const recordKey = createOtaRecordKey(this.scope, target);
    const storedScope = createStoredAppUpdateCacheScope(
      this.scope,
      target.policyVersion,
    );
    const updatedAt = this.nowIso();
    await this.db.withExclusiveTransaction(async (transaction) => {
      await upsertSetting(
        transaction,
        recordKey,
        JSON.stringify({ scope: storedScope, target, policy }),
        updatedAt,
      );
      await upsertSetting(
        transaction,
        this.pointerKey,
        JSON.stringify(target),
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
      !isTrustedPosHandheldOtaChannel(
        policy.channel,
        scope.configuredChannel,
        scope.platform,
      )) ||
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
    !isTrustedPosHandheldOtaChannel(
      policy.channel,
      scope.configuredChannel,
      scope.platform,
    ) ||
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

function createTargetIdentity(
  policy: PosHandheldOtaUpdatePolicy,
): OtaPolicyTargetIdentity {
  return Object.freeze({
    policyVersion: normalizePolicyVersion(policy.policyVersion),
    releaseChannel: policy.channel,
    updateId: policy.updateId,
    updateGroupId: policy.updateGroupId,
  });
}

function normalizeTargetIdentity(input: unknown): OtaPolicyTargetIdentity {
  if (
    !isExactRecord(input, [
      "policyVersion",
      "releaseChannel",
      "updateId",
      "updateGroupId",
    ])
  ) {
    throw new TypeError("Handheld OTA cache target identity is invalid.");
  }
  const policyVersion = normalizePolicyVersion(input.policyVersion);
  const releaseChannel = normalizeNullableTargetToken(
    input.releaseChannel,
    128,
  );
  const updateId = normalizeNullableTargetToken(input.updateId, 256);
  const updateGroupId = input.updateGroupId === null
    ? null
    : normalizeTargetUuid(input.updateGroupId);
  if (
    (updateId === null) !== (updateGroupId === null) ||
    (policyVersion === "none" && updateId !== null)
  ) {
    throw new TypeError("Handheld OTA cache target identity is invalid.");
  }
  return Object.freeze({
    policyVersion,
    releaseChannel,
    updateId,
    updateGroupId,
  });
}

function createOtaRecordKey(
  scope: OtaAppUpdateCacheScope,
  target: OtaPolicyTargetIdentity,
): string {
  const base = createAppUpdateCacheKey(
    OTA_RECORD_PREFIX,
    scope,
    target.policyVersion,
  );
  return `${base}:${[
    target.releaseChannel,
    target.updateId,
    target.updateGroupId,
  ].map(targetKeyPart).join(":")}`;
}

function targetKeyPart(value: string | null): string {
  return encodeURIComponent(
    value === null ? "target:null" : `target:value:${value}`,
  );
}

function targetIdentitiesEqual(
  left: OtaPolicyTargetIdentity,
  right: OtaPolicyTargetIdentity,
): boolean {
  return (
    left.policyVersion === right.policyVersion &&
    left.releaseChannel === right.releaseChannel &&
    left.updateId === right.updateId &&
    left.updateGroupId === right.updateGroupId
  );
}

function normalizeNullableTargetToken(
  value: unknown,
  maximum: number,
): string | null {
  if (value === null) return null;
  if (typeof value !== "string") {
    throw new TypeError("Handheld OTA cache target identity is invalid.");
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)
  ) {
    throw new TypeError("Handheld OTA cache target identity is invalid.");
  }
  return normalized;
}

function normalizeTargetUuid(value: unknown): string {
  const normalized = normalizeNullableTargetToken(value, 36);
  if (
    normalized === null ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      normalized,
    )
  ) {
    throw new TypeError("Handheld OTA cache target identity is invalid.");
  }
  return normalized.toLowerCase();
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
