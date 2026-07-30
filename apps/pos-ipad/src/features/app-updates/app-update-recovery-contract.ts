import type {
  RuntimeBackendState,
  RuntimeDeviceState,
} from "@/core/runtime/pos-runtime";

export const UPDATE_RECOVERY_SNAPSHOT_UNAVAILABLE =
  "UPDATE_RECOVERY_SNAPSHOT_UNAVAILABLE";

export type AppUpdateRecoveryRuntimeSnapshot = Readonly<{
  appVersion: string;
  buildNumber: string;
  runtimeVersion: string;
  channel: string;
  apiOrigin: string;
}>;

export type AppUpdateRecoverySnapshot =
  AppUpdateRecoveryRuntimeSnapshot &
    Readonly<{
      backendState: RuntimeBackendState;
      deviceState: RuntimeDeviceState;
    }>;

export type AppUpdateRecoveryRuntimePort = Readonly<{
  readSnapshot(): Promise<AppUpdateRecoveryRuntimeSnapshot>;
}>;

export function createAppUpdateRecoveryRuntimeSnapshot(
  input: Readonly<{
    appVersion: unknown;
    buildNumber: unknown;
    runtimeVersion: unknown;
    channel: unknown;
    apiOrigin: unknown;
  }>,
): AppUpdateRecoveryRuntimeSnapshot {
  return Object.freeze({
    appVersion: displayValue(input.appVersion),
    buildNumber: displayValue(input.buildNumber),
    runtimeVersion: displayValue(input.runtimeVersion),
    channel: displayValue(input.channel),
    apiOrigin: displayValue(input.apiOrigin),
  });
}

export function combineAppUpdateRecoverySnapshot(
  runtime: AppUpdateRecoveryRuntimeSnapshot,
  operational: Readonly<{
    backendState: RuntimeBackendState;
    deviceState: RuntimeDeviceState;
  }>,
): AppUpdateRecoverySnapshot {
  return Object.freeze({
    ...runtime,
    backendState: operational.backendState,
    deviceState: operational.deviceState,
  });
}

export function serializeAppUpdateRecoverySnapshot(
  snapshot: AppUpdateRecoverySnapshot,
): string {
  // 精确重建白名单对象，禁止未来 runtime 扩展字段被意外带入支持导出。
  return JSON.stringify(
    {
      appVersion: snapshot.appVersion,
      buildNumber: snapshot.buildNumber,
      runtimeVersion: snapshot.runtimeVersion,
      channel: snapshot.channel,
      apiOrigin: snapshot.apiOrigin,
      backendState: snapshot.backendState,
      deviceState: snapshot.deviceState,
    },
    null,
    2,
  );
}

function displayValue(value: unknown): string {
  return typeof value === "string" && value.trim()
    ? value.trim()
    : "unknown";
}
