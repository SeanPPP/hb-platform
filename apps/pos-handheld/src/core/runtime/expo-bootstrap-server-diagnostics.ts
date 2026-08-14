import Constants from "expo-constants";

import { ExpoSecureStoreAdapter } from "../security/expo-secure-store";
import {
  normalizeTrustedApiOrigins,
  PosPublicRuntimeConfigurationStore,
} from "../security/pos-public-runtime-configuration";

import {
  createBootstrapServerDiagnostics,
  type BootstrapServerDiagnostics,
} from "./bootstrap-server-diagnostics";
import { createSettingsApiHealthProbe } from "./expo-settings-configuration";
import { resolveHbposApiUrl } from "./runtime-config";

export type { BootstrapServerDiagnostics } from "./bootstrap-server-diagnostics";

type BootstrapExtra = Readonly<{
  hbpos?: Readonly<{
    apiBaseUrl?: string;
    trustedApiOrigins?: readonly string[];
  }>;
}>;

/**
 * 完整 runtime 尚未可用时只开放受信地址的只读探测；永久切换仍由已打开
 * 本地账本的注册运行时完成，避免在无法检查待补传数据时绕过安全门禁。
 */
export async function loadExpoBootstrapServerDiagnostics(): Promise<BootstrapServerDiagnostics> {
  const extra = Constants.expoConfig?.extra as BootstrapExtra | undefined;
  const trustedApiOrigins = normalizeTrustedApiOrigins([
    ...(extra?.hbpos?.trustedApiOrigins ?? []),
    ...(extra?.hbpos?.apiBaseUrl
      ? [extra.hbpos.apiBaseUrl]
      : [resolveHbposApiUrl(undefined)]),
  ]);
  const store = new PosPublicRuntimeConfigurationStore(
    new ExpoSecureStoreAdapter(),
    trustedApiOrigins,
  );
  const persisted = await store.load();
  const currentApiBaseUrl = resolveHbposApiUrl(
    persisted.apiBaseUrl ?? extra?.hbpos?.apiBaseUrl,
  );
  const probe = createSettingsApiHealthProbe((url, init) =>
    fetch(url, init),
  );

  return createBootstrapServerDiagnostics({
    currentApiBaseUrl,
    trustedApiOrigins,
    probe,
  });
}
