import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { getAllStores, getStoresByUserGuid } from "@/modules/shop/api";
import { useAuthStore } from "@/store/auth-store";
import { useCartStore } from "@/store/cart-store";
import { useDeviceStore } from "@/store/device-store";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { STORE_SELECTION_STORAGE_KEY, type Store } from "@/modules/shop/types";

function normalizeStores(
  stores: Array<{
    storeGUID?: string;
    storeCode?: string;
    storeName?: string;
    postcode?: string;
    stateCode?: string;
    isPrimary?: boolean;
    assignedAt?: string;
  }> | null | undefined
): Store[] {
  return (stores ?? [])
    .filter(
      (item): item is {
        storeGUID?: string;
        storeCode: string;
        storeName?: string;
        postcode?: string;
        stateCode?: string;
        isPrimary?: boolean;
        assignedAt?: string;
      } => Boolean(item?.storeCode),
    )
    .map((item) => ({
      storeGUID: item.storeGUID,
      storeCode: item.storeCode,
      storeName: item.storeName || item.storeCode,
      postcode: item.postcode,
      stateCode: item.stateCode,
      isPrimary: item.isPrimary,
      assignedAt: item.assignedAt,
    }));
}

function sortStores(stores: Store[]) {
  return stores
    .slice()
    .sort((left, right) =>
      (left.storeName || left.storeCode).localeCompare(right.storeName || right.storeCode, undefined, {
        sensitivity: "base",
      })
    );
}

function isPrivilegedStoreViewer(roleNames: string[] | undefined) {
  if (!roleNames?.length) {
    return false;
  }

  return roleNames.some((role) => {
    const normalizedRole = role.trim().toLowerCase();
    return (
      normalizedRole === "admin" ||
      normalizedRole === "管理员" ||
      normalizedRole === "warehousemanager" ||
      normalizedRole === "仓库经理"
    );
  });
}

export function useStores() {
  const user = useAuthStore((state) => state.user);
  const access = useAuthStore((state) => state.access);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const deviceSession = useDeviceStore((state) => state.session);
  const userStores = useCartStore((state) => state.userStores);
  const selectedStore = useCartStore((state) => state.selectedStore);
  const setUserStores = useCartStore((state) => state.setUserStores);
  const setSelectedStore = useCartStore((state) => state.setSelectedStore);
  const setCartSummary = useCartStore((state) => state.setCartSummary);
  const [isHydratingSelection, setIsHydratingSelection] = useState(false);
  const userGuid = user?.userGUID ?? null;
  const hasUserSession = Boolean(isAuthenticated && userGuid);
  const isDeviceMode = Boolean(
    deviceSession?.hardwareId &&
      deviceSession.authCode &&
      deviceSession.storeCode &&
      !hasUserSession
  );
  const deviceBoundStore = useMemo<Store | null>(
    () =>
      deviceSession?.storeCode
        ? {
            storeCode: deviceSession.storeCode,
            storeName: deviceSession.storeName || deviceSession.storeCode,
          }
        : null,
    [deviceSession?.storeCode, deviceSession?.storeName]
  );
  const shouldLoadAllStores =
    access.isAdmin || access.isWarehouseManager || isPrivilegedStoreViewer(user?.roleNames);

  const storesQuery = useQuery({
    queryKey: ["userStores", userGuid, shouldLoadAllStores, deviceSession?.storeCode],
    enabled: !isDeviceMode && Boolean(userGuid),
    queryFn: async () => {
      if (!userGuid) {
        return [];
      }

      const embeddedStores = sortStores(normalizeStores(user?.stores));

      if (shouldLoadAllStores) {
        const allStores = sortStores(await getAllStores());
        console.info("[useStores] loaded all stores", {
          canReadStore: access.canReadStore,
          count: allStores.length,
          shouldLoadAllStores,
          userGuid: user?.userGUID,
        });
        return allStores;
      }

      try {
        const apiStores = sortStores(await getStoresByUserGuid(userGuid));

        if (apiStores.length) {
          console.info("[useStores] loaded stores from user endpoint", {
            canReadStore: access.canReadStore,
            count: apiStores.length,
            endpoint: `/Users/guid/${userGuid}/stores`,
            embeddedCount: embeddedStores.length,
            userGuid,
          });
          return apiStores;
        }

        if (embeddedStores.length) {
          console.warn("[useStores] user endpoint returned empty; fallback to embedded stores", {
            canReadStore: access.canReadStore,
            embeddedCount: embeddedStores.length,
            endpoint: `/Users/guid/${userGuid}/stores`,
            permissions: user?.permissions,
            userGuid,
          });
          return embeddedStores;
        }

        console.warn("[useStores] no stores available from endpoint or embedded user data", {
          canReadStore: access.canReadStore,
          endpoint: `/Users/guid/${userGuid}/stores`,
          permissions: user?.permissions,
          roleNames: user?.roleNames,
          userGuid,
        });
        return [];
      } catch (error) {
        if (embeddedStores.length) {
          console.warn("[useStores] user endpoint failed; fallback to embedded stores", {
            canReadStore: access.canReadStore,
            embeddedCount: embeddedStores.length,
            endpoint: `/Users/guid/${userGuid}/stores`,
            error,
            userGuid,
          });
          return embeddedStores;
        }

        console.error("[useStores] failed to load stores", {
          canReadStore: access.canReadStore,
          endpoint: `/Users/guid/${userGuid}/stores`,
          error,
          permissions: user?.permissions,
          roleNames: user?.roleNames,
          userGuid,
        });
        throw error;
      }
    },
  });

  useEffect(() => {
    if (isDeviceMode) {
      if (!deviceBoundStore) {
        return;
      }

      setUserStores([deviceBoundStore]);
      setSelectedStore(deviceBoundStore);
      void AppAsyncStorage.setString(STORE_SELECTION_STORAGE_KEY, deviceBoundStore.storeCode);
      return;
    }

    if (!userGuid) {
      setUserStores([]);
      setSelectedStore(null);
      setCartSummary(null);
      return;
    }

    if (!storesQuery.isSuccess) {
      return;
    }

    let cancelled = false;

    async function syncStores() {
      setIsHydratingSelection(true);

      const stores = storesQuery.data ?? [];
      setUserStores(stores);

      const currentSelectedStore = useCartStore.getState().selectedStore;
      const persistedStoreCode = await AppAsyncStorage.getString(STORE_SELECTION_STORAGE_KEY);

      if (cancelled) {
        return;
      }

      const nextSelectedStore =
        (currentSelectedStore
          ? stores.find((item) => item.storeCode === currentSelectedStore.storeCode)
          : null) ??
        (persistedStoreCode ? stores.find((item) => item.storeCode === persistedStoreCode) : null) ??
        (stores.length === 1 ? stores[0] : null);

      setSelectedStore(nextSelectedStore ?? null);

      if (nextSelectedStore?.storeCode) {
        await AppAsyncStorage.setString(STORE_SELECTION_STORAGE_KEY, nextSelectedStore.storeCode);
      } else {
        await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
      }

      if (!cancelled) {
        setIsHydratingSelection(false);
      }
    }

    void syncStores();

    return () => {
      cancelled = true;
    };
  }, [deviceBoundStore, isDeviceMode, setCartSummary, setSelectedStore, setUserStores, storesQuery.data, storesQuery.isSuccess, userGuid]);

  const effectiveStores = isDeviceMode ? (deviceBoundStore ? [deviceBoundStore] : []) : userStores;
  const effectiveSelectedStore = isDeviceMode ? deviceBoundStore : selectedStore;
  const selectedStoreCode = effectiveSelectedStore?.storeCode ?? null;
  const embeddedStores = useMemo(() => sortStores(normalizeStores(user?.stores)), [user?.stores]);
  const debugInfo = useMemo(
    () => ({
      canReadStore: access.canReadStore,
      deviceStoreCode: deviceSession?.storeCode ?? null,
      embeddedStoreCount: embeddedStores.length,
      enabled: Boolean(userGuid),
      errorMessage: storesQuery.error instanceof Error ? storesQuery.error.message : null,
      fetchStatus: storesQuery.fetchStatus,
      hasUserGuid: Boolean(userGuid),
      queryStatus: storesQuery.status,
      roleNames: user?.roleNames ?? [],
      storeCount: effectiveStores.length,
      userGuid: userGuid ?? "",
    }),
    [
      access.canReadStore,
      deviceSession?.storeCode,
      embeddedStores.length,
      effectiveStores.length,
      storesQuery.error,
      storesQuery.fetchStatus,
      storesQuery.status,
      user?.roleNames,
      userGuid,
    ]
  );

  const actions = useMemo(
    () => ({
      async selectStore(store: Store | null) {
        if (isDeviceMode && deviceBoundStore) {
          setSelectedStore(deviceBoundStore);
          await AppAsyncStorage.setString(STORE_SELECTION_STORAGE_KEY, deviceBoundStore.storeCode);
          return;
        }

        setSelectedStore(store);
        setCartSummary(null);

        if (store?.storeCode) {
          await AppAsyncStorage.setString(STORE_SELECTION_STORAGE_KEY, store.storeCode);
        } else {
          await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
        }
      },
    }),
    [deviceBoundStore, isDeviceMode, setCartSummary, setSelectedStore]
  );

  return {
    stores: effectiveStores,
    selectedStore: effectiveSelectedStore,
    selectedStoreCode,
    isDeviceMode,
    deviceBoundStore,
    isHydratingSelection,
    debugInfo,
    ...storesQuery,
    ...actions,
  };
}
