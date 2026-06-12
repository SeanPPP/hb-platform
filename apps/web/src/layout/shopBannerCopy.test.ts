import { resolveShopBannerCopy } from './shopBannerCopy'

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

console.log('shopBannerCopy.test: ok')
