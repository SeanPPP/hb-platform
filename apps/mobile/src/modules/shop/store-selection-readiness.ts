function normalize(value?: string | null) {
  return value?.trim().toLocaleLowerCase() ?? "";
}

export function buildStoreSelectionScopeKey({
  storeCodes,
  useAllStores,
  userGuid,
}: {
  storeCodes: string[];
  useAllStores: boolean;
  userGuid: string;
}) {
  const normalizedCodes = storeCodes.map(normalize).filter(Boolean).sort();
  return `${normalize(userGuid)}:${useAllStores ? "all" : "assigned"}:${normalizedCodes.join(",")}`;
}

export function isStoreSelectionReadyForScope({
  currentScopeKey,
  deviceStoreCode,
  hydratedScopeKey,
  isDeviceMode,
  userGuid,
}: {
  currentScopeKey: string | null;
  deviceStoreCode?: string | null;
  hydratedScopeKey: string | null;
  isDeviceMode: boolean;
  userGuid?: string | null;
}) {
  if (isDeviceMode) {
    return Boolean(normalize(deviceStoreCode));
  }
  if (!normalize(userGuid)) {
    return true;
  }
  return Boolean(currentScopeKey && hydratedScopeKey === currentScopeKey);
}
