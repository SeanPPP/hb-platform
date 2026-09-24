import { create } from 'zustand'
import type { StoreOrderCart } from '../types/storeOrder'
import type { PreorderActivationSummary } from '../types/preorder'
import { getStoreSupplyWatchSummary } from '../services/supplyNoticeService'
import type { StoreSupplyWatchSummary } from '../types/supplyNotice'
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
  /** 关注恢复订货的汇总：提示条与入口角标共用；切店后重新拉取。 */
  supplyWatchSummary: StoreSupplyWatchSummary
  refreshSupplyWatchSummary: () => Promise<void>
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
  preorderBlocked: false,
  preorderGateLoading: true,
  preorderGateError: false,
  preorderGateRequestVersion: 0,
  supplyWatchSummary: { watchingCount: 0, restockedCount: 0 },
  refreshSupplyWatchSummary: async () => {
    const storeCode = get().selectedStore?.storeCode
    if (!storeCode) {
      set({ supplyWatchSummary: { watchingCount: 0, restockedCount: 0 } })
      return
    }
    try {
      const summary = await getStoreSupplyWatchSummary(storeCode)
      // 只接受当前分店的结果，切店过程中旧请求返回直接丢弃。
      if (get().selectedStore?.storeCode === storeCode) {
        set({ supplyWatchSummary: summary })
      }
    } catch {
      // 汇总只是提示，失败保持上一次的值即可。
    }
  },
  setUserStores: (stores) =>
    set((state) => {
      const selectedStore = state.selectedStore
      const nextSelectedStore = selectedStore
        ? stores.find((item) => item.storeCode === selectedStore.storeCode) ?? null
        : state.selectedStore
      const selectedStoreChanged = nextSelectedStore?.storeCode !== selectedStore?.storeCode

      return {
        userStores: stores,
        selectedStore: nextSelectedStore,
        // 门店列表刷新若改变当前门店，必须立即废弃所有旧门禁请求。
        preorderGateRequestVersion: selectedStoreChanged
          ? state.preorderGateRequestVersion + 1
          : state.preorderGateRequestVersion,
        // 门店失效时同步清除旧门店门禁，不能等 effect 发起下一次请求。
        ...(selectedStoreChanged ? {
          preorderActivations: [],
          preorderBlocked: false,
          preorderGateLoading: false,
          preorderGateError: false,
        } : {}),
      }
    }),
  setSelectedStore: (selectedStore) => set((state) => ({
    selectedStore,
    // 即使发生 A→B→A，单调递增 token 也能阻止最早的 A 请求回写。
    preorderGateRequestVersion: state.preorderGateRequestVersion + 1,
    // 切店立即清除上一家分店的阻塞和错误，检查完成前保持 fail-open。
    preorderActivations: [],
    preorderBlocked: false,
    preorderGateLoading: true,
    preorderGateError: false,
    supplyWatchSummary: { watchingCount: 0, restockedCount: 0 },
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
      preorderBlocked: false,
      preorderGateLoading: false,
      preorderGateError: false,
      supplyWatchSummary: { watchingCount: 0, restockedCount: 0 },
      // reset 也只递增不归零，避免旧 token 与后续新请求发生碰撞。
      preorderGateRequestVersion: state.preorderGateRequestVersion + 1,
    })),
}))
