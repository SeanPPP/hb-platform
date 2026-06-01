export interface ScanLookupPayload {
  barcode: string;
  storeCode?: string;
}

export function buildScanLookupPayload(
  barcode: string,
  storeCode?: string | null
): ScanLookupPayload {
  const normalizedStoreCode = storeCode?.trim();

  return {
    barcode,
    ...(normalizedStoreCode ? { storeCode: normalizedStoreCode } : {}),
  };
}
