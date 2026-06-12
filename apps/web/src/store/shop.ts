import { create } from 'zustand'
import type { StoreOrderCart } from '../types/storeOrder'
import type { UserStoreDto } from '../types/user'

interface ShopState {
  userStores: UserStoreDto[]
  selectedStore: UserStoreDto | null
  cart: StoreOrderCart | null
  setUserStores: (stores: UserStoreDto[]) => void
  setSelectedStore: (store: UserStoreDto | null) => void
  setCart: (cart: StoreOrderCart | null) => void
  reset: () => void
}

export const useShopStore = create<ShopState>((set) => ({
  userStores: [],
  selectedStore: null,
  cart: null,
  setUserStores: (stores) =>
    set((state) => {
      const selectedStore = state.selectedStore
      const nextSelectedStore = selectedStore
        ? stores.find((item) => item.storeCode === selectedStore.storeCode) ?? null
        : state.selectedStore

      return {
        userStores: stores,
        selectedStore: nextSelectedStore,
      }
    }),
  setSelectedStore: (selectedStore) => set({ selectedStore }),
  setCart: (cart) => set({ cart }),
  reset: () =>
    set({
      userStores: [],
      selectedStore: null,
      cart: null,
    }),
}))
