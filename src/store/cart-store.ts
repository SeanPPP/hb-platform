import { create } from "zustand";
import type { Store, StoreOrderCart } from "@/modules/shop/types";

interface CartStoreState {
  userStores: Store[];
  selectedStore: Store | null;
  cartSummary: StoreOrderCart | null;
  setUserStores: (stores: Store[]) => void;
  setSelectedStore: (store: Store | null) => void;
  setCartSummary: (cartSummary: StoreOrderCart | null) => void;
  reset: () => void;
}

export const useCartStore = create<CartStoreState>((set) => ({
  userStores: [],
  selectedStore: null,
  cartSummary: null,
  setUserStores: (stores) =>
    set((state) => {
      const selectedStore = state.selectedStore
        ? stores.find((item) => item.storeCode === state.selectedStore?.storeCode) ?? null
        : null;

      return {
        userStores: stores,
        selectedStore,
      };
    }),
  setSelectedStore: (selectedStore) => set({ selectedStore }),
  setCartSummary: (cartSummary) => set({ cartSummary }),
  reset: () =>
    set({
      userStores: [],
      selectedStore: null,
      cartSummary: null,
    }),
}));
