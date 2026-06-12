export type ExternalQueryStoreResolution<TStore> =
  | { type: "ready" }
  | { type: "wait" }
  | { type: "select-store"; store: TStore }
  | { type: "store-not-found"; storeCode: string };

interface ExternalQueryStoreInput<TStore extends { storeCode: string }> {
  targetStoreCode?: string;
  selectedStoreCode?: string | null;
  stores: readonly TStore[];
  storesLoading: boolean;
}

export function resolveExternalQueryStore<TStore extends { storeCode: string }>({
  targetStoreCode,
  selectedStoreCode,
  stores,
  storesLoading,
}: ExternalQueryStoreInput<TStore>): ExternalQueryStoreResolution<TStore> {
  if (storesLoading) {
    return { type: "wait" };
  }

  if (!targetStoreCode) {
    return selectedStoreCode ? { type: "ready" } : { type: "wait" };
  }

  if (selectedStoreCode === targetStoreCode) {
    return { type: "ready" };
  }

  const targetStore = stores.find((item) => item.storeCode === targetStoreCode);
  if (targetStore) {
    return { type: "select-store", store: targetStore };
  }

  return { type: "store-not-found", storeCode: targetStoreCode };
}
