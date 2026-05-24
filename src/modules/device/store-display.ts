interface StoreLike {
  storeCode: string;
  storeName?: string | null;
}

interface ResolveDeviceStoreDisplayNameOptions {
  deviceStoreCode?: string | null;
  deviceStoreName?: string | null;
  stores: StoreLike[];
  fallback: string;
}

function hasUsefulName(name?: string | null, code?: string | null) {
  const normalizedName = name?.trim();
  return Boolean(normalizedName && normalizedName !== code?.trim());
}

export function resolveDeviceStoreDisplayName({
  deviceStoreCode,
  deviceStoreName,
  stores,
  fallback,
}: ResolveDeviceStoreDisplayNameOptions) {
  if (hasUsefulName(deviceStoreName, deviceStoreCode)) {
    return deviceStoreName!.trim();
  }

  const matchedStore = deviceStoreCode
    ? stores.find((store) => store.storeCode === deviceStoreCode)
    : undefined;
  if (hasUsefulName(matchedStore?.storeName, deviceStoreCode)) {
    return matchedStore!.storeName!.trim();
  }

  return deviceStoreCode?.trim() || fallback;
}
