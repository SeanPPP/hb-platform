import { readFileSync } from 'node:fs'
import {
  PRODUCT_MOVEMENT_ACTION_HINTS,
  PRODUCT_MOVEMENT_SUGGESTION_CARDS,
  formatAud,
  formatPercent,
  getCoverDaysRatio,
  getCredibilityTagColor,
  getSuggestionTagColor,
  isCoverDaysTight,
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

// 可卖天数进度条：30 天满格，14 天及以下判定紧张；无销量时按满格，避免空条被误读成缺货。
assertEqual(getCoverDaysRatio(15), 0.5, '可卖 15 天应占进度条一半')
assertEqual(getCoverDaysRatio(45), 1, '超过 30 天的可卖天数按满格封顶')
assertEqual(getCoverDaysRatio(-3), 0, '估算剩余为负时进度条应为空')
assertEqual(getCoverDaysRatio(null), 1, '近 30 天无销量时进度条按满格处理')
assertEqual(isCoverDaysTight(14), true, '可卖 14 天应判定为紧张')
assertEqual(isCoverDaysTight(14.1), false, '可卖超过 14 天不应判定为紧张')
assertEqual(isCoverDaysTight(null), false, '无销量不应判定为紧张')

// 建议卡片即筛选入口：第一张为「全部」，其余每张都要有对应的完整动作说明作为提示。
assertEqual(PRODUCT_MOVEMENT_SUGGESTION_CARDS[0].key, '', '第一张卡片应为不筛选的全部商品')
assertEqual(
  PRODUCT_MOVEMENT_SUGGESTION_CARDS.some((card) => card.key === '好卖'),
  true,
  '去掉建议下拉后，好卖必须仍能通过卡片单独筛选',
)

// 店长动作列已取消，页面不应再渲染该字段。
assertEqual(pageSource.includes('storeManagerAction'), false, '商品经营分析页面不应再展示店长动作列')
assertEqual(pageSource.includes('imageUrl'), true, '商品经营分析页面应展示商品图片')
assertEqual(serviceSource.includes('record.imageUrl ?? record.ImageUrl'), true, '服务层应兼容大小写的图片字段')

assertEqual(
  serviceSource.includes('record.snapshotGeneratedAtUtc ?? record.SnapshotGeneratedAtUtc'),
  true,
  '服务层应透传快照生成时间',
)
assertEqual(pageSource.includes('数据生成于'), true, '走快照时页面应提示数据生成时间，避免误读为实时数据')

console.log('productMovementReport.logic.test: ok')
