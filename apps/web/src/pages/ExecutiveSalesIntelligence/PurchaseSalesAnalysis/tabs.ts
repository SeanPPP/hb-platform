export type AdminPurchaseSalesTab = 'batch' | 'store'

export const ADMIN_PURCHASE_SALES_PATH = '/executive-sales-intelligence/purchase-sales-analysis'
export const ADMIN_PURCHASE_SALES_TAB_PARAM = 'tab'

export interface AdminPurchaseSalesTabAccess {
  canViewBatch: boolean
  canViewStore: boolean
}

/**
 * 解析「进货销量分析」当前标签。
 * 两个标签分别受批量货号销量、分店进货销量分析权限控制：只有一个权限时固定落到该标签，
 * 都有时默认批量货号销量，?tab=store 才进入分店进货销量分析；都没有时返回 null。
 */
export function resolveAdminPurchaseSalesTab(
  value: string | null | undefined,
  access: AdminPurchaseSalesTabAccess,
): AdminPurchaseSalesTab | null {
  if (!access.canViewBatch && !access.canViewStore) {
    return null
  }
  if (!access.canViewBatch) {
    return 'store'
  }
  if (!access.canViewStore) {
    return 'batch'
  }
  return value === 'store' ? 'store' : 'batch'
}

/** 旧地址重定向用：带上标签参数，历史书签能直接落到原页面对应的标签。 */
export function buildAdminPurchaseSalesTabPath(tab: AdminPurchaseSalesTab) {
  return `${ADMIN_PURCHASE_SALES_PATH}?${ADMIN_PURCHASE_SALES_TAB_PARAM}=${tab}`
}
