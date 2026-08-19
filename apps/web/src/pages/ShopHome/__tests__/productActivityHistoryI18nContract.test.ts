import en from '../../../i18n/locales/en.json'
import zh from '../../../i18n/locales/zh.json'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const requiredKeys = [
  'title',
  'productName',
  'itemNumber',
  'store',
  'lastArrival',
  'latestOrder',
  'latestShipment',
  'salesSinceArrival',
  'notRealtime',
  'filter',
  'filterAll',
  'filterOrder',
  'filterSales',
  'date',
  'type',
  'typeOrder',
  'typeSales',
  'typeSubtotal',
  'orderNo',
  'orderQuantity',
  'shipQuantity',
  'outboundDate',
  'salesQuantity',
  'averagePrice',
  'status',
  'lastOrder',
  'orderLabel',
  'sendLabel',
  'salesLabel',
  'entryTitle',
  'entryAria',
  'empty',
  'loadFailed',
  'retry',
]

for (const [locale, messages] of Object.entries({ en, zh })) {
  const namespace = messages.shop.productActivityHistory as unknown as Record<string, unknown>
  assertEqual(typeof namespace, 'object', `${locale} 商品活动历史必须使用独立命名空间`)

  for (const key of requiredKeys) {
    assertEqual(
      typeof namespace[key],
      'string',
      `${locale} shop.productActivityHistory.${key} 必须是字符串`,
    )
  }
}

console.log('productActivityHistoryI18nContract.test: ok')
