import type { TabItem } from '../types/router'

interface ResolveRouteKeepAliveStateInput {
  routeTab: TabItem | null
  tabs: TabItem[]
  fallbackPathname: string
}

interface RouteKeepAliveState {
  activeCacheKey: string
  cacheKeys: string[]
}

export function resolveRouteKeepAliveState({
  routeTab,
  tabs,
  fallbackPathname,
}: ResolveRouteKeepAliveStateInput): RouteKeepAliveState {
  const cacheKeySet = new Set(tabs.filter((item) => item.keepAlive).map((item) => item.key))

  if (routeTab?.keepAlive) {
    cacheKeySet.add(routeTab.key)
  }

  return {
    activeCacheKey: routeTab?.key || fallbackPathname,
    cacheKeys: Array.from(cacheKeySet),
  }
}
