/**
 * 商城导航的增量文案：随 ShopLayout 代码块懒注册，不进入首屏 i18n 包（首屏体积预算已接近上限）。
 * 新增商城导航项时优先加在这里，而不是主文案包。
 */
export const shopNavMessages = {
  zh: {
    shop: {
      purchaseSalesAnalysis: '进货销量分析',
      supplyWatches: '我关注的商品',
      supplyWatchesSubtitle: '关注暂停供货的商品，恢复订货时在这里提醒你。',
      supplyRestockedBanner: '你关注的 {{count}} 个商品已恢复订货',
      supplyRestockedBannerAction: '查看',
    },
  },
  en: {
    shop: {
      purchaseSalesAnalysis: 'Purchase & Sales',
      supplyWatches: 'Watched',
      supplyWatchesSubtitle: 'Watch paused products and get told here once they are orderable again.',
      supplyRestockedBanner: '{{count}} watched product(s) are back in stock',
      supplyRestockedBannerAction: 'View',
    },
  },
}
