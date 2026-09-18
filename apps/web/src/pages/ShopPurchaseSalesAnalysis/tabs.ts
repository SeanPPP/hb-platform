export type ShopPurchaseSalesTab = 'paste' | 'store'

export const SHOP_PURCHASE_SALES_TAB_PARAM = 'tab'

/**
 * 解析地址栏里的标签页参数。
 * 「粘贴数据查看」复用货号销量页，需要前台货号销量权限；未授权时一律回落到「选择分店查看」，
 * 参数缺失或非法时，已授权用户默认进入粘贴数据查看。
 */
export function resolveShopPurchaseSalesTab(value: string | null | undefined, canViewPasteTab: boolean): ShopPurchaseSalesTab {
  if (!canViewPasteTab) {
    return 'store'
  }
  return value === 'store' ? 'store' : 'paste'
}
