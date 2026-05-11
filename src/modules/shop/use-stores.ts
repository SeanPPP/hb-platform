import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { getAllStores, getStoresByUserGuid } from "@/modules/shop/api";
import { useAuthStore } from "@/store/auth-store";
import { useCartStore } from "@/store/cart-store";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { STORE_SELECTION_STORAGE_KEY, type Store } from "@/modules/shop/types";

function normalizeStores(
  stores: Array<{ storeCode?: string; storeName?: string }> | null | undefined
): Store[] {
  return (stores ?? [])
    .filter((item): item is { storeCode: string; storeName?: string } => Boolean(item?.storeCode))
    .map((item) => ({
      storeCode: item.storeCode,
      storeName: item.storeName || item.storeCode,
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
  const userStores = useCartStore((state) => state.userStores);
  const selectedStore = useCartStore((state) => state.selectedStore);
  const setUserStores = useCartStore((state) => state.setUserStores);
  const setSelectedStore = useCartStore((state) => state.setSelectedStore);
  const setCartSummary = useCartStore((state) => state.setCartSummary);
  const [isHydratingSelection, setIsHydratingSelection] = useState(false);
  const shouldLoadAllStores =
    access.isAdmin || access.isWarehouseManager || isPrivilegedStoreViewer(user?.roleNames);

  const storesQuery = useQuery({
    queryKey: ["userStores", user?.userGUID, shouldLoadAllStores],
    enabled: Boolean(user?.userGUID),
    queryFn: async () => {
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
        const apiStores = sortStores(await getStoresByUserGuid(user!.userGUID));

        if (apiStores.length) {
          console.info("[useStores] loaded stores from user endpoint", {
            canReadStore: access.canReadStore,
            count: apiStores.length,
            endpoint: `/Users/guid/${user!.userGUID}/stores`,
            embeddedCount: embeddedStores.length,
            userGuid: user?.userGUID,
          });
          return apiStores;
        }

        if (embeddedStores.length) {
          console.warn("[useStores] user endpoint returned empty; fallback to embedded stores", {
            canReadStore: access.canReadStore,
            embeddedCount: embeddedStores.length,
            endpoint: `/Users/guid/${user!.userGUID}/stores`,
            permissions: user?.permissions,
            userGuid: user?.userGUID,
          });
          return embeddedStores;
        }

        console.warn("[useStores] no stores available from endpoint or embedded user data", {
          canReadStore: access.canReadStore,
          endpoint: `/Users/guid/${user!.userGUID}/stores`,
          permissions: user?.permissions,
          roleNames: user?.roleNames,
          userGuid: user?.userGUID,
        });
        return [];
      } catch (error) {
        if (embeddedStores.length) {
          console.warn("[useStores] user endpoint failed; fallback to embedded stores", {
            canReadStore: access.canReadStore,
            embeddedCount: embeddedStores.length,
            endpoint: `/Users/guid/${user!.userGUID}/stores`,
            error,
            userGuid: user?.userGUID,
          });
          return embeddedStores;
        }

        console.error("[useStores] failed to load stores", {
          canReadStore: access.canReadStore,
          endpoint: `/Users/guid/${user!.userGUID}/stores`,
          error,
          permissions: user?.permissions,
          roleNames: user?.roleNames,
          userGuid: user?.userGUID,
        });
        throw error;
      }
    },
  });

  useEffect(() => {
    if (!user?.userGUID) {
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
  }, [setCartSummary, setSelectedStore, setUserStores, storesQuery.data, storesQuery.isSuccess, user?.userGUID]);

  const selectedStoreCode = selectedStore?.storeCode ?? null;
  const embeddedStores = useMemo(() => sortStores(normalizeStores(user?.stores)), [user?.stores]);
  const debugInfo = useMemo(
    () => ({
      canReadStore: access.canReadStore,
      embeddedStoreCount: embeddedStores.length,
      enabled: Boolean(user?.userGUID),
      errorMessage: storesQuery.error instanceof Error ? storesQuery.error.message : null,
      fetchStatus: storesQuery.fetchStatus,
      hasUserGuid: Boolean(user?.userGUID),
      queryStatus: storesQuery.status,
      roleNames: user?.roleNames ?? [],
      storeCount: userStores.length,
      userGuid: user?.userGUID ?? "",
    }),
    [
      access.canReadStore,
      embeddedStores.length,
      storesQuery.error,
      storesQuery.fetchStatus,
      storesQuery.status,
      user?.roleNames,
      user?.userGUID,
      userStores.length,
    ]
  );

  const actions = useMemo(
    () => ({
      async selectStore(store: Store | null) {
        setSelectedStore(store);
        setCartSummary(null);

        if (store?.storeCode) {
          await AppAsyncStorage.setString(STORE_SELECTION_STORAGE_KEY, store.storeCode);
        } else {
          await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
        }
      },
    }),
    [setCartSummary, setSelectedStore]
  );

  return {
    stores: userStores,
    selectedStore,
    selectedStoreCode,
    isHydratingSelection,
    debugInfo,
    ...storesQuery,
    ...actions,
  };
}
