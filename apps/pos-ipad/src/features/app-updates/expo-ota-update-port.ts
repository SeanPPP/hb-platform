import {
  normalizePosIpadOtaUpdatePolicy,
  type PosIpadOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";

export type ExpoOtaUpdateManifest = Readonly<{
  id?: unknown;
  runtimeVersion?: unknown;
  metadata?: unknown;
  updateGroupId?: unknown;
}>;

export interface ExpoOtaUpdatesRuntimePort {
  setUpdateRequestHeadersOverride(
    headers: Record<string, string> | null,
  ): void;
  checkForUpdateAsync(): Promise<
    Readonly<{
      isAvailable: boolean;
      manifest?: ExpoOtaUpdateManifest | undefined;
    }>
  >;
  fetchUpdateAsync(): Promise<
    Readonly<{
      isNew: boolean;
      manifest?: ExpoOtaUpdateManifest | undefined;
    }>
  >;
  reloadAsync(): Promise<void>;
}

export type ExpoOtaUpdateApplyResult =
  | Readonly<{ state: "reloaded"; reason: null }>
  | Readonly<{
      state: "unavailable";
      reason:
        | "no-update"
        | "updates-disabled"
        | "not-available"
        | "not-new";
    }>
  | Readonly<{
      state: "rejected";
      reason:
        | "runtime-mismatch"
        | "update-id-mismatch"
        | "selection-changed"
        | "restart-unsafe"
        | "manifest-invalid"
        | "channel-override-failed"
        | "channel-clear-failed"
        | "update-check-failed";
    }>;

export type ExpoOtaBeforeReloadDecision =
  | true
  | "selection-changed"
  | "restart-unsafe";

export type ExpoOtaUpdatePortOptions = Readonly<{
  enabled: boolean;
  runtimeVersion: string | null;
  updates: ExpoOtaUpdatesRuntimePort;
}>;

/**
 * channel override 只在后台策略命中后短暂生效；check 与 fetch 返回的
 * runtimeVersion/update id 都必须匹配策略，任一不一致都不能 reload。
 */
export class ExpoOtaUpdatePort {
  public constructor(private readonly options: ExpoOtaUpdatePortOptions) {
    if (!options.enabled) return;
    try {
      // request header override 会被 expo-updates 持久化；冷启动先清掉上次进程遗留的分店 channel。
      options.updates.setUpdateRequestHeadersOverride(null);
    } catch {
      // 原生模块暂不可用时不能让应用启动失败；NEVER 仍阻止后台自行检查遗留 channel。
    }
  }

  public async apply(
    input: PosIpadOtaUpdatePolicy,
    beforeReload: () =>
      | ExpoOtaBeforeReloadDecision
      | Promise<ExpoOtaBeforeReloadDecision> = () => true,
  ): Promise<ExpoOtaUpdateApplyResult> {
    const policy = normalizePosIpadOtaUpdatePolicy(input);
    if (policy.state === "none") {
      return Object.freeze({ state: "unavailable", reason: "no-update" });
    }
    if (!this.options.enabled) {
      return Object.freeze({
        state: "unavailable",
        reason: "updates-disabled",
      });
    }
    if (
      this.options.runtimeVersion === null ||
      this.options.runtimeVersion !== policy.runtimeVersion
    ) {
      return Object.freeze({
        state: "rejected",
        reason: "runtime-mismatch",
      });
    }

    let readyToReload = false;
    let preReloadResult: Exclude<
      ExpoOtaUpdateApplyResult,
      Readonly<{ state: "reloaded"; reason: null }>
    > | null = null;
    let channelClearFailed = false;
    try {
      try {
        this.options.updates.setUpdateRequestHeadersOverride({
          "expo-channel-name": policy.channel,
        });
      } catch {
        // 原生 setter 可能先持久化再抛错，因此仍须进入 finally 尝试清理。
        preReloadResult = Object.freeze({
          state: "rejected",
          reason: "channel-override-failed",
        });
      }

      if (preReloadResult === null) {
        const checked = await this.options.updates.checkForUpdateAsync();
        if (!checked.isAvailable) {
          preReloadResult = Object.freeze({
            state: "unavailable",
            reason: "not-available",
          });
        } else {
          const checkedManifest = verifyManifest(
            checked.manifest,
            policy.runtimeVersion,
            policy.iosUpdateId,
          );
          if (checkedManifest) {
            preReloadResult = checkedManifest;
          } else {
            const fetched = await this.options.updates.fetchUpdateAsync();
            if (!fetched.isNew) {
              preReloadResult = Object.freeze({
                state: "unavailable",
                reason: "not-new",
              });
            } else {
              const fetchedManifest = verifyManifest(
                fetched.manifest,
                policy.runtimeVersion,
                policy.iosUpdateId,
              );
              if (fetchedManifest) {
                preReloadResult = fetchedManifest;
              } else {
                readyToReload = true;
              }
            }
          }
        }
      }
    } catch {
      // 原生 check/fetch 异常也转为稳定失败，确保 finally 的 channel 清理结果可以优先裁决。
      preReloadResult = Object.freeze({
        state: "rejected",
        reason: "update-check-failed",
      });
    } finally {
      // 避免一次分店定向检查把 channel 泄漏到后续策略或其他登录门店。
      try {
        this.options.updates.setUpdateRequestHeadersOverride(null);
      } catch {
        channelClearFailed = true;
      }
    }

    if (channelClearFailed) {
      // 未确认持久化 override 已清除前绝不能 reload，避免跨分店复用 channel。
      return Object.freeze({
        state: "rejected",
        reason: "channel-clear-failed",
      });
    }
    if (preReloadResult !== null) {
      return preReloadResult;
    }

    if (readyToReload) {
      const approval = beforeReload();
      const selectionStillCurrent =
        isPromiseLike(approval) ? await approval : approval;
      if (selectionStillCurrent !== true) {
        return Object.freeze({
          state: "rejected",
          reason: selectionStillCurrent,
        });
      }
      await this.options.updates.reloadAsync();
      return Object.freeze({ state: "reloaded", reason: null });
    }
    return Object.freeze({
      state: "rejected",
      reason: "manifest-invalid",
    });
  }
}

function isPromiseLike<T>(
  value: T | Promise<T>,
): value is Promise<T> {
  return (
    typeof value === "object" &&
    value !== null &&
    "then" in value &&
    typeof value.then === "function"
  );
}

function verifyManifest(
  manifest: ExpoOtaUpdateManifest | undefined,
  expectedRuntimeVersion: string,
  expectedUpdateId: string,
): Extract<ExpoOtaUpdateApplyResult, { state: "rejected" }> | null {
  if (!manifest || typeof manifest !== "object") {
    return Object.freeze({
      state: "rejected",
      reason: "manifest-invalid",
    });
  }
  if (manifest.runtimeVersion !== expectedRuntimeVersion) {
    return Object.freeze({
      state: "rejected",
      reason: "runtime-mismatch",
    });
  }
  if (
    typeof manifest.id !== "string" ||
    manifest.id.toLowerCase() !== expectedUpdateId
  ) {
    return Object.freeze({
      state: "rejected",
      reason: "update-id-mismatch",
    });
  }
  return null;
}
