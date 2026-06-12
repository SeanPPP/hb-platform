import { resolveRouteKeepAliveState } from './adminKeepAlive'
import type { TabItem } from '../types/router'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function assertIncludes(values: string[], expected: string, message: string) {
  if (!values.includes(expected)) {
    throw new Error(`${message}: ${expected} not found in [${values.join(', ')}]`)
  }
}

const invoiceListTab: TabItem = {
  key: '/pos-admin/local-supplier-invoices',
  path: '/pos-admin/local-supplier-invoices',
  routePath: '/pos-admin/local-supplier-invoices',
  title: '分店进货单',
  keepAlive: true,
}

const invoiceEditTab: TabItem = {
  key: '/pos-admin/local-supplier-invoices/invoice-1',
  path: '/pos-admin/local-supplier-invoices/invoice-1',
  routePath: '/pos-admin/local-supplier-invoices/:id',
  title: '编辑进货单',
  keepAlive: true,
}

const state = resolveRouteKeepAliveState({
  routeTab: invoiceEditTab,
  tabs: [invoiceListTab],
  fallbackPathname: '/pos-admin/local-supplier-invoices/invoice-1',
})

assertEqual(
  state.activeCacheKey,
  '/pos-admin/local-supplier-invoices/invoice-1',
  '应用内 Tab 切换竞态中应优先用当前路由作为 KeepAlive active key',
)
assertIncludes(
  state.cacheKeys,
  '/pos-admin/local-supplier-invoices/invoice-1',
  '首次进入动态编辑页时 include 应包含当前路由缓存 key',
)

console.log('adminKeepAlive.test: ok')
