import { resolveShopPurchaseSalesTab } from './tabs'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

assertEqual(resolveShopPurchaseSalesTab(null, true), 'paste', '已授权且未指定标签时默认进入粘贴数据查看')
assertEqual(resolveShopPurchaseSalesTab('paste', true), 'paste', '已授权时保留粘贴数据查看')
assertEqual(resolveShopPurchaseSalesTab('store', true), 'store', '已授权时保留选择分店查看')
assertEqual(resolveShopPurchaseSalesTab('unknown', true), 'paste', '非法参数回落到默认标签')

assertEqual(resolveShopPurchaseSalesTab(null, false), 'store', '未授权时只能进入选择分店查看')
assertEqual(resolveShopPurchaseSalesTab('paste', false), 'store', '未授权时旧地址 ?tab=paste 不得打开货号销量')
assertEqual(resolveShopPurchaseSalesTab('store', false), 'store', '未授权时保留选择分店查看')

console.log('shopPurchaseSalesTabs.test: ok')
