/**
 * 商城导航的增量文案：随 ShopLayout 代码块懒注册，不进入首屏 i18n 包（首屏体积预算已接近上限）。
 * 新增商城导航项时优先加在这里，而不是主文案包。
 */
export const shopNavMessages = {
  zh: { shop: { purchaseSalesAnalysis: '进货销量分析' } },
  en: { shop: { purchaseSalesAnalysis: 'Purchase & Sales' } },
}
