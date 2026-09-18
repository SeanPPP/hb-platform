import { registerPageMessages } from '../../i18n/registerPageMessages'
import en from './messages.en.json'
import zh from './messages.zh.json'

// 图表与前台页面文案随本组件所在的页面代码块懒加载，不进入首屏 i18n 包。
registerPageMessages({ zh, en })

export { default as PurchaseSalesDailyChart } from './PurchaseSalesDailyChart'
export { default as PurchaseSalesSparkline } from './PurchaseSalesSparkline'
export {
  buildPurchaseSalesTrendMetrics,
  toWholeQuantity,
  type PurchaseSalesTrendMetrics,
  type PurchaseSalesTrendRow,
} from './purchaseSalesTrend'
