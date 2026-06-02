function normalizeStoreCode(storeCode?: string | null) {
  const normalized = storeCode?.trim();
  return normalized ? normalized : null;
}

export function shouldClearActiveCartMutation(
  currentStoreCode?: string | null,
  mutationStoreCode?: string | null
) {
  return normalizeStoreCode(currentStoreCode) === normalizeStoreCode(mutationStoreCode);
}
