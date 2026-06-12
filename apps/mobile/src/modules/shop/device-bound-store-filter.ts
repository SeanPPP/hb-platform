type StoreFilterField = "storeCode" | "branchCode";

interface DeviceBoundStoreOptions {
  isDeviceMode: boolean;
  selectedStoreCode?: string | null;
  storeField: StoreFilterField;
}

export function getDeviceBoundStoreCode({
  isDeviceMode,
  selectedStoreCode,
}: {
  isDeviceMode: boolean;
  selectedStoreCode?: string | null;
}) {
  return isDeviceMode && selectedStoreCode ? selectedStoreCode : undefined;
}

export function bindDeviceStoreFilter<TFilters extends object>(
  filters: TFilters,
  options: DeviceBoundStoreOptions
): TFilters & Partial<Record<StoreFilterField, string | undefined>> {
  const deviceBoundStoreCode = getDeviceBoundStoreCode(options);
  if (!deviceBoundStoreCode) {
    return filters as TFilters & Partial<Record<StoreFilterField, string | undefined>>;
  }

  return {
    ...filters,
    [options.storeField]: deviceBoundStoreCode,
  } as TFilters & Partial<Record<StoreFilterField, string | undefined>>;
}
