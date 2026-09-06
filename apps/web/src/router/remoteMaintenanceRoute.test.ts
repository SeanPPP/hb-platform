import type { CurrentUser } from '../types/auth'

// routes 初始化 i18n 时会读取浏览器语言；测试只提供最小只读存根，不写入持久化存储。
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: { getItem: () => null },
})

const { buildAccess } = await import('../utils/access')
const { buildMenus, getCurrentRoute, resolveRoute } = await import('./routes')

function createUser(roleNames: string[]): CurrentUser {
  return {
    userGUID: 'remote-maintenance-test',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames,
    storeNames: [],
  }
}

function assert(condition: unknown, message: string) {
  if (!condition) throw new Error(message)
}

function includesMenuPath(items: unknown[] | undefined, path: string): boolean {
  return Boolean(items?.some((item) => {
    if (!item || typeof item !== 'object') return false
    const node = item as { key?: unknown; children?: unknown[] }
    return node.key === path || includesMenuPath(node.children, path)
  }))
}

const route = resolveRoute('/system/remote-maintenance')
assert(route?.meta.accessKey === 'isAdmin', 'remote maintenance route must be admin-only')
assert(route?.meta.keepAlive === false, 'credentials page must unmount when navigation leaves it')

const adminAccess = buildAccess(createUser(['Admin']))
const regularAccess = buildAccess(createUser(['StoreManager']))
assert(getCurrentRoute('/system/remote-maintenance', adminAccess)?.element === route?.element, 'admin should resolve the remote maintenance page')
assert(getCurrentRoute('/system/remote-maintenance', regularAccess)?.element !== route?.element, 'non-admin should resolve the forbidden page')
assert(includesMenuPath(buildMenus(adminAccess), '/system/remote-maintenance'), 'admin menu should include remote maintenance')
assert(!includesMenuPath(buildMenus(regularAccess), '/system/remote-maintenance'), 'non-admin menu should hide remote maintenance')

console.log('remoteMaintenanceRoute.test: ok')
