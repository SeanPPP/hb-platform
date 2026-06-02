import { ensureStoreOrderSyncSession } from './syncSessionGuard'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

let clearCount = 0
let redirectedTo = ''

assertEqual(
  await ensureStoreOrderSyncSession({
    refreshSession: async () => true,
    clearAuth: () => {
      clearCount += 1
    },
    redirectToLogin: (target) => {
      redirectedTo = target
    },
  }),
  true,
  '刷新成功时应允许继续同步',
)
assertEqual(clearCount, 0, '刷新成功时不应清理登录状态')
assertEqual(redirectedTo, '', '刷新成功时不应跳转登录页')

assertEqual(
  await ensureStoreOrderSyncSession({
    refreshSession: async () => false,
    clearAuth: () => {
      clearCount += 1
    },
    redirectToLogin: (target) => {
      redirectedTo = target
    },
    currentPath: '/warehouse/store-orders?status=1',
  }),
  false,
  '刷新失败时应阻止同步',
)
assertEqual(clearCount, 1, '刷新失败时应清理登录状态')
assertEqual(
  redirectedTo,
  '/login?redirect=%2Fwarehouse%2Fstore-orders%3Fstatus%3D1',
  '刷新失败时应带当前页面跳转登录页',
)
