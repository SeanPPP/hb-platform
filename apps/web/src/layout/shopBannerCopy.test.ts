import { resolveShopBannerCopy } from './shopBannerCopy'
import en from '../i18n/locales/en.json'
import zh from '../i18n/locales/zh.json'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const bestSellersCopy = resolveShopBannerCopy('/shop/best-sellers')
assertEqual(bestSellersCopy.titleKey, 'shop.bestSellers', '热销商品页标题应使用热销商品文案')
assertEqual(
  bestSellersCopy.subtitleKey,
  'shop.bestSellersBannerSubtitle',
  '热销商品页副标题不应复用历史订单文案',
)

const comingSoonCopy = resolveShopBannerCopy('/shop/coming-soon')
assertEqual(comingSoonCopy.titleKey, 'shop.comingSoon', '即将上新页标题应使用即将上新文案')
assertEqual(
  comingSoonCopy.subtitleKey,
  'shop.comingSoonBannerSubtitle',
  '即将上新页副标题不应复用历史订单文案',
)

const ordersCopy = resolveShopBannerCopy('/shop/orders')
assertEqual(ordersCopy.titleKey, 'shop.orderHistory', '历史订单页标题应保持历史订单文案')
assertEqual(ordersCopy.subtitleKey, 'shop.ordersBannerSubtitle', '历史订单页副标题应保持订单汇总文案')

const orderDetailCopy = resolveShopBannerCopy('/shop/orders/order-1')
assertEqual(orderDetailCopy.titleKey, 'shop.orderHistory', '历史订单详情页标题应保持历史订单文案')
assertEqual(orderDetailCopy.subtitleKey, 'shop.ordersBannerSubtitle', '历史订单详情页副标题应保持订单汇总文案')

const preorderCopy = resolveShopBannerCopy('/shop/preorders/preorder-1')
assertEqual(preorderCopy.titleKey, 'shop.preorderTitle', '预订货页标题应使用预订货文案')
assertEqual(
  preorderCopy.subtitleKey,
  'shop.preorderBannerSubtitle',
  '预订货页副标题应使用预订货说明文案',
)
assertEqual(en.shop.preorderTitle, 'Preorder', '英文预订货标题资源应存在')
assertEqual(
  en.shop.preorderBannerSubtitle,
  "Enter pack quantities for this period based on each item's MOQ, then submit to continue to the next period.",
  '英文预订货副标题资源应完整',
)
assertEqual(zh.shop.preorderTitle, '预订货', '中文预订货标题资源应存在')
assertEqual(
  zh.shop.preorderBannerSubtitle,
  '按最小订货量填写本期份数，提交后继续处理下一期。',
  '中文预订货副标题资源应与页面语义一致',
)

console.log('shopBannerCopy.test: ok')
