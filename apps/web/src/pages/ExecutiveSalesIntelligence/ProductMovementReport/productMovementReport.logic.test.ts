import { readFileSync } from 'node:fs'
import {
  PRODUCT_MOVEMENT_ACTION_HINTS,
  formatAud,
  formatPercent,
  getCredibilityTagColor,
  getSuggestionTagColor,
} from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

assertEqual(getSuggestionTagColor('需要订货'), 'red', '需要订货应使用高优先级颜色')
assertEqual(getSuggestionTagColor('需要备货'), 'orange', '需要备货应使用提醒颜色')
assertEqual(getSuggestionTagColor('需要清仓'), 'volcano', '需要清仓应突出清仓风险')
assertEqual(getCredibilityTagColor('低'), 'red', '低可信度应使用红色')
assertEqual(formatAud(1234.5), '$1,234.50', 'AUD 金额应按澳洲格式展示')
assertEqual(formatPercent(0.356), '35.6%', '毛利率应按百分比展示')
assertEqual(
  PRODUCT_MOVEMENT_ACTION_HINTS['需要备货'].includes('有货先上架，无货再订货'),
  true,
  '需要备货动作必须区分货架/后仓与订货',
)
assertEqual(
  PRODUCT_MOVEMENT_ACTION_HINTS['需要订货'].includes('进货单到货情况'),
  true,
  '需要订货动作必须提醒核对进货单到货情况',
)

const pageSource = readFileSync(
  'src/pages/ExecutiveSalesIntelligence/ProductMovementReport/index.tsx',
  'utf8',
)
const serviceSource = readFileSync('src/services/productMovementReportService.ts', 'utf8')

assertEqual(
  pageSource.includes('getProductMovementStoreOptions()'),
  true,
  '商品经营分析页面应使用报表专用分店选项接口',
)
assertEqual(
  pageSource.includes('getActiveStores'),
  false,
  '商品经营分析页面不应依赖需要 Stores.View 的通用分店接口',
)
assertEqual(
  serviceSource.includes('/store-options'),
  true,
  '商品经营分析服务应提供同权限的分店选项请求',
)

console.log('productMovementReport.logic.test: ok')
