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
        | "channel-restore-failed"
        | "update-check-failed"
        | "cancelled";
    }>;

export class MobileOtaUpdatePort {
  private readyTarget: Readonly<{
    identity: string;
    previousChannel: string;
    targetChannel: string;
  }> | null = null;
  private currentChannel: string;

  public constructor(private readonly options: Readonly<{
    enabled: boolean;
    runtimeVersion: string | null;
    currentChannel: string;
    updates: MobileOtaUpdatesRuntimePort;
  }>) {
    this.currentChannel = options.currentChannel;
  }

  public async download(
    decision: MobileOtaUpdateDecision,
    guard: Readonly<{ isCurrent(): boolean }> = { isCurrent: () => true },
  ): Promise<MobileOtaDownloadResult> {
    this.readyTarget = null;
    if (decision.state === "none" || !decision.releaseChannel || !decision.updateId) {
      return Object.freeze({ state: "unavailable", reason: "no-update" });
    }
    if (!this.options.enabled) {
      return Object.freeze({ state: "unavailable", reason: "updates-disabled" });
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
    let overrideAttempted = false;
    const previousChannel = this.currentChannel;
    try {
      overrideAttempted = true;
      try {
        this.options.updates.setUpdateRequestHeadersOverride({
          "expo-channel-name": decision.releaseChannel,
        });
        this.currentChannel = decision.releaseChannel;
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
    }

    try {
      if (overrideAttempted) {
        // 下载后先恢复当前 channel；用户选择“稍后”时，冷启动不能提前采用可选目标。
        this.options.updates.setUpdateRequestHeadersOverride({
          "expo-channel-name": previousChannel,
        });
        this.currentChannel = previousChannel;
      }
    } catch {
      return Object.freeze({ state: "rejected", reason: "channel-restore-failed" });
    }

    if (result) return result;
    if (!ready) {
      return Object.freeze({ state: "rejected", reason: "manifest-invalid" });
    }

    // Expo 会把下载时的 request headers 写入 update；确认 reload 时再恢复该目标 channel。
    this.readyTarget = Object.freeze({
      identity: targetIdentity(decision),
      previousChannel,
      targetChannel: decision.releaseChannel,
    });
    return Object.freeze({ state: "downloaded", reason: null });
  }

  public isReady(decision: MobileOtaUpdateDecision) {
    return this.readyTarget?.identity === targetIdentity(decision);
  }

  public async reload() {
    const readyTarget = this.readyTarget;
    if (!readyTarget) {
      throw new Error("Mobile OTA update is not ready to reload");
    }
    // 同一下载最多触发一次 reload；失败后必须重新校验策略和已下载目标。
    this.readyTarget = null;
    try {
      this.options.updates.setUpdateRequestHeadersOverride({
        "expo-channel-name": readyTarget.targetChannel,
      });
      this.currentChannel = readyTarget.targetChannel;
      await this.options.updates.reloadAsync();
    } catch (error) {
      try {
        this.options.updates.setUpdateRequestHeadersOverride({
          "expo-channel-name": readyTarget.previousChannel,
        });
        this.currentChannel = readyTarget.previousChannel;
      } catch {
        throw new Error("Mobile OTA reload and channel restore both failed", {
          cause: error,
        });
      }
      throw error;
    }
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
