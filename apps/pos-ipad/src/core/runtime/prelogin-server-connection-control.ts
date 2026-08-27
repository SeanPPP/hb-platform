import {
  hasPendingLocalData,
  type SettingsPendingDataSnapshot,
} from "../../features/settings/settings-presenter";
import { normalizePublicRuntimeApiBaseUrl } from "../security/pos-public-runtime-configuration";

export type PreloginServerConnectionChangeResult =
  | Readonly<{ status: "completed"; apiBaseUrl: string }>
  | Readonly<{
      status: "blocked";
      reason: "pending-local-data" | "candidate-unreachable";
    }>;

export type PreloginServerConnectionDependencies = Readonly<{
  currentApiBaseUrl: string;
  trustedApiOrigins: readonly string[];
  allowSwitchWithPendingLocalData?: boolean;
  runExclusive<T>(operation: () => Promise<T>): Promise<T>;
  readPendingData(
    signal: AbortSignal,
  ): Promise<SettingsPendingDataSnapshot>;
  probe(healthUrl: string, signal: AbortSignal): Promise<boolean>;
  save(apiBaseUrl: string): Promise<void>;
  runSwitchGuarded?<T>(operation: () => Promise<T>): Promise<
    | Readonly<{ blocked: true }>
    | Readonly<{ blocked: false; value: T }>
  >;
  hasRegistrationRecoveryRisk?(): Promise<boolean>;
}>;

/**
 * 未登录设备只暴露服务器连接控制面；地址白名单、待处理数据和候选 health
 * 必须全部通过后才落 Keychain，调用方随后负责重建完整 POS runtime。
 */
export class PreloginServerConnectionControl {
  private readonly trustedOrigins: ReadonlySet<string>;

  public constructor(
    private readonly input: PreloginServerConnectionDependencies,
  ) {
    this.trustedOrigins = new Set(input.trustedApiOrigins);
  }

  public getCurrentApiBaseUrl(): string {
    return this.input.currentApiBaseUrl;
  }

  public async test(
    candidate: string,
    signal: AbortSignal,
  ): Promise<boolean> {
    const apiBaseUrl = this.normalize(candidate);
    throwIfAborted(signal);
    return this.input.probe(`${apiBaseUrl}/api/v1/health`, signal);
  }

  public async change(
    candidate: string,
    signal: AbortSignal,
  ): Promise<PreloginServerConnectionChangeResult> {
    const apiBaseUrl = this.normalize(candidate);
    if (
      !this.input.runSwitchGuarded ||
      !this.input.hasRegistrationRecoveryRisk
    ) {
      return registrationRecoveryBlocked();
    }
    const guarded = await this.input.runSwitchGuarded(() =>
      this.input.runExclusive(async () => {
      throwIfAborted(signal);
      if (await this.registrationRecoveryBlocks(signal)) {
        return registrationRecoveryBlocked();
      }
      const pending = await this.input.readPendingData(signal);
      throwIfAborted(signal);
      if (
        hasPendingLocalData(pending) &&
        this.input.allowSwitchWithPendingLocalData !== true
      ) {
        return Object.freeze({
          status: "blocked" as const,
          reason: "pending-local-data" as const,
        });
      }
      const reachable = await this.input.probe(
        `${apiBaseUrl}/api/v1/health`,
        signal,
      );
      throwIfAborted(signal);
      if (!reachable) {
        return Object.freeze({
          status: "blocked" as const,
          reason: "candidate-unreachable" as const,
        });
      }
      if (await this.registrationRecoveryBlocks(signal)) {
        return registrationRecoveryBlocked();
      }
      await this.input.save(apiBaseUrl);
      // Keychain 写入是切换的提交点；提交后即使界面卸载，也必须如实返回成功。
      return Object.freeze({ status: "completed" as const, apiBaseUrl });
      }),
    );
    return guarded.blocked ? registrationRecoveryBlocked() : guarded.value;
  }

  private async registrationRecoveryBlocks(
    signal: AbortSignal,
  ): Promise<boolean> {
    const read = this.input.hasRegistrationRecoveryRisk;
    if (!read) return true;
    try {
      throwIfAborted(signal);
      const blocked = await read.call(this.input);
      throwIfAborted(signal);
      return blocked;
    } catch (error) {
      if (signal.aborted) throw error;
      return true;
    }
  }

  private normalize(candidate: string): string {
    return normalizePublicRuntimeApiBaseUrl(
      candidate,
      this.trustedOrigins,
    );
  }
}

function registrationRecoveryBlocked(): PreloginServerConnectionChangeResult {
  return Object.freeze({
    status: "blocked",
    reason: "pending-local-data",
  });
}

function throwIfAborted(signal: AbortSignal): void {
  if (signal.aborted) {
    throw Object.assign(new Error("Server connection operation aborted."), {
      name: "AbortError",
    });
  }
}
