import type { Store } from "@/modules/shop/types";

export type StoreScopeInput = {
  stores: Store[];
  isDeviceMode: boolean;
  deviceBoundStore?: Store | null;
};

export function getAssignedStoresForSession({
  stores,
  isDeviceMode,
  deviceBoundStore,
}: StoreScopeInput) {
  if (isDeviceMode) {
    return deviceBoundStore ? [deviceBoundStore] : [];
  }

  return stores;
}

export function getManageableStoresForSession({
  stores,
  isDeviceMode,
  deviceBoundStore,
  isAdmin,
}: StoreScopeInput & { isAdmin: boolean }) {
  if (isDeviceMode) {
    return deviceBoundStore ? [deviceBoundStore] : [];
  }

  if (isAdmin) {
    return stores;
  }

  // 账号模式下后端用 isPrimary=true 标记可修改分店，false 仅用于查看。
  return stores.filter((store) => store.isPrimary === true);
}

export function isStoreManageable(storeCode: string | null | undefined, manageableStores: Store[]) {
  if (!storeCode) {
    return false;
  }

  return manageableStores.some((store) => store.storeCode === storeCode);
}

export function resolveScopedStoreCode({
  currentStoreCode,
  persistedStoreCode,
  deviceBoundStoreCode,
  isDeviceMode,
  stores,
}: {
  currentStoreCode?: string | null;
  persistedStoreCode?: string | null;
  deviceBoundStoreCode?: string | null;
  isDeviceMode: boolean;
  stores: Store[];
}) {
  if (isDeviceMode) {
    return deviceBoundStoreCode ?? null;
  }

  if (currentStoreCode && stores.some((store) => store.storeCode === currentStoreCode)) {
    return currentStoreCode;
  }

  if (persistedStoreCode && stores.some((store) => store.storeCode === persistedStoreCode)) {
    return persistedStoreCode;
  }

  return stores.length === 1 ? stores[0].storeCode : null;
}
