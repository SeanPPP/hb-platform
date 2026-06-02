export const STORE_ORDER_SYNC_AUTH_EXPIRED_MESSAGE = '登录已过期，请重新登录'

export interface StoreOrderSyncSessionGuardOptions {
  refreshSession: () => Promise<boolean>
  clearAuth: () => void
  redirectToLogin: (target: string) => void
  currentPath?: string
}

export async function ensureStoreOrderSyncSession({
  refreshSession,
  clearAuth,
  redirectToLogin,
  currentPath = '/warehouse/store-orders',
}: StoreOrderSyncSessionGuardOptions) {
  const refreshed = await refreshSession()
  if (refreshed) {
    return true
  }

  clearAuth()
  redirectToLogin(`/login?redirect=${encodeURIComponent(currentPath)}`)
  return false
}
