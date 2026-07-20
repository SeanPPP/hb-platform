export interface ShopBannerCopy {
  titleKey: string
  titleFallback: string
  subtitleKey: string
  subtitleFallback: string
}

const orderHistoryBannerCopy: ShopBannerCopy = {
  titleKey: 'shop.orderHistory',
  titleFallback: '历史订单',
  subtitleKey: 'shop.ordersBannerSubtitle',
  subtitleFallback: '查看分店提交过的订单、状态、数量和金额汇总。',
}

export function resolveShopBannerCopy(pathname: string): ShopBannerCopy {
  if (pathname.startsWith('/shop/preorders/')) {
    return {
      titleKey: 'shop.preorderTitle',
      titleFallback: 'Preorder 预订货',
      subtitleKey: 'shop.preorderBannerSubtitle',
      subtitleFallback: '按最小订货量填写本期份数，提交后继续处理下一期。',
    }
  }

  if (pathname.startsWith('/shop/best-sellers')) {
    return {
      titleKey: 'shop.bestSellers',
      titleFallback: '热销商品',
      subtitleKey: 'shop.bestSellersBannerSubtitle',
      subtitleFallback: '查看热销商品销量、销售额和排名汇总。',
    }
  }

  if (pathname.startsWith('/shop/coming-soon')) {
    return {
      titleKey: 'shop.comingSoon',
      titleFallback: '即将上新',
      subtitleKey: 'shop.comingSoonBannerSubtitle',
      subtitleFallback: '查看即将到货货柜，快速筛选补货和新品。',
    }
  }

  return orderHistoryBannerCopy
}
