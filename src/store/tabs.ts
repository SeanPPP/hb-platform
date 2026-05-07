import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { TabItem } from '../types/router'
import { getAffixTabs } from '../router/routes'

interface TabsState {
  activeKey: string
  tabs: TabItem[]
  setActiveKey: (key: string) => void
  ensureTab: (tab: TabItem) => void
  updateTabTitle: (key: string, title: string) => void
  removeTab: (key: string) => string
  removeOtherTabs: (key: string) => string[]
  removeLeftTabs: (key: string) => string[]
  removeRightTabs: (key: string) => string[]
  resetTabs: () => void
}

const defaultTabs = getAffixTabs()
const defaultActiveKey = defaultTabs[0]?.key ?? '/dashboard'

export const useTabsStore = create<TabsState>()(
  persist(
    (set, get) => ({
      activeKey: defaultActiveKey,
      tabs: defaultTabs,
      setActiveKey: (key) => set({ activeKey: key }),
      ensureTab: (tab) => {
        const exists = get().tabs.some((item) => item.key === tab.key)
        if (exists) {
          set({ activeKey: tab.key })
          return
        }

        set({
          tabs: [...get().tabs, tab],
          activeKey: tab.key,
        })
      },
      updateTabTitle: (key, title) => {
        set({
          tabs: get().tabs.map((item) => (item.key === key ? { ...item, title } : item)),
        })
      },
      removeTab: (key) => {
        const currentTabs = get().tabs
        const target = currentTabs.find((item) => item.key === key)

        if (!target || target.affix) {
          return get().activeKey
        }

        const nextTabs = currentTabs.filter((item) => item.key !== key)
        const removedIndex = currentTabs.findIndex((item) => item.key === key)
        const nextActiveKey =
          get().activeKey === key
            ? nextTabs[removedIndex]?.key || nextTabs[removedIndex - 1]?.key || defaultActiveKey
            : get().activeKey

        set({
          tabs: nextTabs.length > 0 ? nextTabs : defaultTabs,
          activeKey: nextActiveKey,
        })

        return nextActiveKey
      },
      removeOtherTabs: (key) => {
        const keepKeys = new Set([...defaultTabs.map((item) => item.key), key])
        const removedKeys = get()
          .tabs.filter((item) => !keepKeys.has(item.key))
          .map((item) => item.key)

        set({
          tabs: get().tabs.filter((item) => keepKeys.has(item.key)),
          activeKey: key,
        })

        return removedKeys
      },
      removeLeftTabs: (key) => {
        const currentTabs = get().tabs
        const index = currentTabs.findIndex((item) => item.key === key)
        if (index <= 0) {
          return []
        }

        const removedKeys = currentTabs
          .slice(0, index)
          .filter((item) => !item.affix)
          .map((item) => item.key)

        set({
          tabs: currentTabs.filter((item, itemIndex) => item.affix || itemIndex >= index),
          activeKey: key,
        })

        return removedKeys
      },
      removeRightTabs: (key) => {
        const currentTabs = get().tabs
        const index = currentTabs.findIndex((item) => item.key === key)
        if (index < 0) {
          return []
        }

        const removedKeys = currentTabs
          .slice(index + 1)
          .filter((item) => !item.affix)
          .map((item) => item.key)

        set({
          tabs: currentTabs.filter((item, itemIndex) => item.affix || itemIndex <= index),
          activeKey: key,
        })

        return removedKeys
      },
      resetTabs: () =>
        set({
          tabs: defaultTabs,
          activeKey: defaultActiveKey,
        }),
    }),
    {
      name: 'react-vite-admin-tabs-v2',
      partialize: (state) => ({
        tabs: state.tabs,
        activeKey: state.activeKey,
      }),
    },
  ),
)
