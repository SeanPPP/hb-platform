export type AndroidVendorIntentScannerProfile = Readonly<{
  id: string;
  broadcastAction: string;
  barcodeExtraKey: string;
}>;

/**
 * 未来厂商原生模块只需实现此边界；core 不声明广播权限，也不操作输入焦点。
 */
export interface AndroidVendorIntentScannerAdapterPort {
  requiredPermissionsFor(
    profile: AndroidVendorIntentScannerProfile,
  ): readonly string[];
  registerBroadcastReceiver(
    profile: AndroidVendorIntentScannerProfile,
    onBarcode: (value: string) => void,
  ): () => void;
}

export type AndroidVendorIntentScannerPort = Readonly<{
  status: "disabled" | "configured";
  requiredPermissions: readonly string[];
  start(): () => void;
}>;

export type AndroidVendorIntentScannerOptions = Readonly<{
  profile?: AndroidVendorIntentScannerProfile | null;
  adapter?: AndroidVendorIntentScannerAdapterPort;
  onBarcode(value: string): void;
}>;

const NO_PERMISSIONS: readonly string[] = Object.freeze([]);
const stopNoop = (): void => {};

/**
 * 当前应用未注入 profile 或原生 adapter，因此始终走 disabled/no-op。
 * 只有未来同时提供经过配置的 profile 与真实 adapter，才会建立广播注册。
 */
export function createAndroidVendorIntentScanner(
  options: AndroidVendorIntentScannerOptions,
): AndroidVendorIntentScannerPort {
  const profile = normalizeProfile(options.profile);
  if (!profile || !options.adapter) {
    return Object.freeze({
      status: "disabled",
      requiredPermissions: NO_PERMISSIONS,
      start: () => stopNoop,
    });
  }

  const requiredPermissions = Object.freeze([
    ...options.adapter.requiredPermissionsFor(profile),
  ]);
  return Object.freeze({
    status: "configured",
    requiredPermissions,
    start: () =>
      options.adapter!.registerBroadcastReceiver(
        profile,
        options.onBarcode,
      ),
  });
}

function normalizeProfile(
  profile: AndroidVendorIntentScannerProfile | null | undefined,
): AndroidVendorIntentScannerProfile | null {
  if (!profile) return null;
  const id = requiredToken(profile.id);
  const broadcastAction = requiredToken(profile.broadcastAction);
  const barcodeExtraKey = requiredToken(profile.barcodeExtraKey);
  if (!id || !broadcastAction || !barcodeExtraKey) return null;
  return Object.freeze({ id, broadcastAction, barcodeExtraKey });
}

function requiredToken(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 256 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    return null;
  }
  return normalized;
}
