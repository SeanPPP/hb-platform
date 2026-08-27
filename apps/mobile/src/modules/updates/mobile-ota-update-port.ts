import type { MobileOtaUpdateDecision } from "./mobile-ota-update";

export type MobileOtaUpdateManifest = Readonly<{
  id?: unknown;
  runtimeVersion?: unknown;
}>;

export interface MobileOtaUpdatesRuntimePort {
  setUpdateRequestHeadersOverride(headers: Record<string, string> | null): void;
  checkForUpdateAsync(): Promise<Readonly<{
    isAvailable: boolean;
    manifest?: MobileOtaUpdateManifest;
  }>>;
  fetchUpdateAsync(): Promise<Readonly<{
    isNew: boolean;
    manifest?: MobileOtaUpdateManifest;
  }>>;
  reloadAsync(): Promise<void>;
}

export type MobileOtaDownloadResult =
  | Readonly<{ state: "downloaded"; reason: null }>
  | Readonly<{
      state: "unavailable";
      reason: "no-update" | "updates-disabled" | "not-available" | "not-new";
    }>
  | Readonly<{
      state: "rejected";
      reason:
        | "runtime-mismatch"
        | "update-id-mismatch"
        | "manifest-invalid"
        | "channel-override-failed"
        | "channel-clear-failed"
        | "update-check-failed"
        | "cancelled";
    }>;

export class MobileOtaUpdatePort {
  private readyTargetIdentity: string | null = null;
  private startupClearFailed = false;

  public constructor(private readonly options: Readonly<{
    enabled: boolean;
    runtimeVersion: string | null;
    updates: MobileOtaUpdatesRuntimePort;
  }>) {
    if (!options.enabled) return;
    try {
      // request header override 由原生层持久化；冷启动先清除上次异常退出的目标 channel。
      options.updates.setUpdateRequestHeadersOverride(null);
    } catch {
      this.startupClearFailed = true;
    }
  }

  public async download(
    decision: MobileOtaUpdateDecision,
    guard: Readonly<{ isCurrent(): boolean }> = { isCurrent: () => true },
  ): Promise<MobileOtaDownloadResult> {
    this.readyTargetIdentity = null;
    if (decision.state === "none" || !decision.releaseChannel || !decision.updateId) {
      return Object.freeze({ state: "unavailable", reason: "no-update" });
    }
    if (!this.options.enabled) {
      return Object.freeze({ state: "unavailable", reason: "updates-disabled" });
    }
    if (this.startupClearFailed) {
      try {
        // 原生 header 清理可能是瞬时失败；每次用户重试都先恢复安全基线。
        this.options.updates.setUpdateRequestHeadersOverride(null);
        this.startupClearFailed = false;
      } catch {
        return Object.freeze({ state: "rejected", reason: "channel-clear-failed" });
      }
    }
    if (
      !this.options.runtimeVersion
      || this.options.runtimeVersion !== decision.runtimeVersion
    ) {
      return Object.freeze({ state: "rejected", reason: "runtime-mismatch" });
    }
    if (!guard.isCurrent()) {
      return Object.freeze({ state: "rejected", reason: "cancelled" });
    }

    let result: MobileOtaDownloadResult | null = null;
    let ready = false;
    let clearFailed = false;
    try {
      try {
        this.options.updates.setUpdateRequestHeadersOverride({
          "expo-channel-name": decision.releaseChannel,
        });
      } catch {
        result = Object.freeze({
          state: "rejected",
          reason: "channel-override-failed",
        });
      }

      if (!result && !guard.isCurrent()) {
        result = Object.freeze({ state: "rejected", reason: "cancelled" });
      }
      if (!result) {
        const checked = await this.options.updates.checkForUpdateAsync();
        if (!guard.isCurrent()) {
          result = Object.freeze({ state: "rejected", reason: "cancelled" });
        } else if (!checked.isAvailable) {
          result = Object.freeze({ state: "unavailable", reason: "not-available" });
        } else {
          result = verifyManifest(
            checked.manifest,
            decision.runtimeVersion,
            decision.updateId,
          );
        }
      }
      if (!result) {
        const fetched = await this.options.updates.fetchUpdateAsync();
        if (!guard.isCurrent()) {
          result = Object.freeze({ state: "rejected", reason: "cancelled" });
        } else if (!fetched.isNew) {
          result = Object.freeze({ state: "unavailable", reason: "not-new" });
        } else {
          result = verifyManifest(
            fetched.manifest,
            decision.runtimeVersion,
            decision.updateId,
          );
          ready = result === null;
        }
      }
    } catch {
      result = Object.freeze({ state: "rejected", reason: "update-check-failed" });
    } finally {
      try {
        this.options.updates.setUpdateRequestHeadersOverride(null);
      } catch {
        clearFailed = true;
        this.startupClearFailed = true;
      }
    }

    if (clearFailed) {
      return Object.freeze({ state: "rejected", reason: "channel-clear-failed" });
    }
    if (result) return result;
    if (!ready) {
      return Object.freeze({ state: "rejected", reason: "manifest-invalid" });
    }
    this.readyTargetIdentity = targetIdentity(decision);
    return Object.freeze({ state: "downloaded", reason: null });
  }

  public isReady(decision: MobileOtaUpdateDecision) {
    return this.readyTargetIdentity === targetIdentity(decision);
  }

  public async reload() {
    if (!this.readyTargetIdentity) {
      throw new Error("Mobile OTA update is not ready to reload");
    }
    // 同一下载最多触发一次 reload；失败后必须重新校验策略和已下载目标。
    this.readyTargetIdentity = null;
    await this.options.updates.reloadAsync();
  }
}

function targetIdentity(decision: MobileOtaUpdateDecision) {
  return JSON.stringify([
    decision.policyVersion,
    decision.releaseChannel,
    decision.runtimeVersion,
    decision.updateId,
    decision.updateGroupId,
  ]);
}

function verifyManifest(
  manifest: MobileOtaUpdateManifest | undefined,
  expectedRuntimeVersion: string,
  expectedUpdateId: string,
): Extract<MobileOtaDownloadResult, { state: "rejected" }> | null {
  if (!manifest || typeof manifest !== "object") {
    return Object.freeze({ state: "rejected", reason: "manifest-invalid" });
  }
  if (manifest.runtimeVersion !== expectedRuntimeVersion) {
    return Object.freeze({ state: "rejected", reason: "runtime-mismatch" });
  }
  if (
    typeof manifest.id !== "string"
    || manifest.id.toLowerCase() !== expectedUpdateId.toLowerCase()
  ) {
    return Object.freeze({ state: "rejected", reason: "update-id-mismatch" });
  }
  return null;
}
