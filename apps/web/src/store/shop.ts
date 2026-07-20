import { create } from 'zustand'
import type { StoreOrderCart } from '../types/storeOrder'
import type { PreorderActivationSummary } from '../types/preorder'
import type { UserStoreDto } from '../types/user'

interface ShopState {
  userStores: UserStoreDto[]
  selectedStore: UserStoreDto | null
  cart: StoreOrderCart | null
  preorderActivations: PreorderActivationSummary[]
  preorderBlocked: boolean
  preorderGateLoading: boolean
  preorderGateError: boolean
  preorderGateRequestVersion: number
  setUserStores: (stores: UserStoreDto[]) => void
  setSelectedStore: (store: UserStoreDto | null) => void
  setCart: (cart: StoreOrderCart | null) => void
  setPreorderGate: (state: Pick<ShopState, 'preorderActivations' | 'preorderBlocked' | 'preorderGateLoading' | 'preorderGateError'>) => void
  beginPreorderGateRequest: () => number
  isPreorderGateRequestCurrent: (token: number) => boolean
  reset: () => void
}

export const useShopStore = create<ShopState>((set, get) => ({
  userStores: [],
  selectedStore: null,
  cart: null,
  preorderActivations: [],
  preorderBlocked: true,
  preorderGateLoading: true,
  preorderGateError: false,
  preorderGateRequestVersion: 0,
  setUserStores: (stores) =>
    set((state) => {
      const selectedStore = state.selectedStore
      const nextSelectedStore = selectedStore
        ? stores.find((item) => item.storeCode === selectedStore.storeCode) ?? null
        : state.selectedStore

      return {
        userStores: stores,
        selectedStore: nextSelectedStore,
        // 门店列表刷新若改变当前门店，必须立即废弃所有旧门禁请求。
        preorderGateRequestVersion: nextSelectedStore?.storeCode === selectedStore?.storeCode
          ? state.preorderGateRequestVersion
          : state.preorderGateRequestVersion + 1,
      }
    }),
  setSelectedStore: (selectedStore) => set((state) => ({
    selectedStore,
    // 即使发生 A→B→A，单调递增 token 也能阻止最早的 A 请求回写。
    preorderGateRequestVersion: state.preorderGateRequestVersion + 1,
  })),
  setCart: (cart) => set({ cart }),
  setPreorderGate: (state) => set(state),
  beginPreorderGateRequest: () => {
    let requestToken = 0
    set((state) => {
      requestToken = state.preorderGateRequestVersion + 1
      return { preorderGateRequestVersion: requestToken }
    })
    return requestToken
  },
  isPreorderGateRequestCurrent: (token) => get().preorderGateRequestVersion === token,
  reset: () =>
    set((state) => ({
      userStores: [],
      selectedStore: null,
      cart: null,
      preorderActivations: [],
      preorderBlocked: true,
      preorderGateLoading: true,
      preorderGateError: false,
      // reset 也只递增不归零，避免旧 token 与后续新请求发生碰撞。
      preorderGateRequestVersion: state.preorderGateRequestVersion + 1,
    })),
}))
