import type { ProductMovementCredibility, ProductMovementSuggestion } from '../../../types/productMovementReport'

export const PRODUCT_MOVEMENT_SUGGESTIONS: ProductMovementSuggestion[] = [
  '需要订货',
  '需要备货',
  '需要清仓',
  '值得囤货',
  '好卖',
  '观察',
  '正常',
]

export const PRODUCT_MOVEMENT_CREDIBILITIES: ProductMovementCredibility[] = ['高', '中', '低']

export const PRODUCT_MOVEMENT_ACTION_HINTS: Record<string, string> = {
  需要备货: '需要备货：请检查货架和后仓；有货先上架，无货再订货。',
  需要订货: '需要订货：估算剩余量不足，请核对货架、后仓和进货单到货情况；不足再向总部/供应商补进。',
  需要清仓: '需要清仓：长期不动销，请检查陈列、价格和库存，考虑 markdown / clearance。',
  值得囤货: '值得囤货：热销且毛利较好，建议保持安全库存。',
  好卖: '好卖：商品动销较好，请保持关注，避免断货。',
  观察: '观察：数据或毛利异常，请先核对商品、成本、进货记录。',
}

/**
 * 建议计数卡片：卡片本身就是筛选入口，`key` 为空表示不按建议筛选。
 * 「正常」不单列卡片，它的含义是「无特殊动作」，从「全部商品」里就能看到。
 */
export const PRODUCT_MOVEMENT_SUGGESTION_CARDS: { key: string; label: string; hint: string }[] = [
  { key: '', label: '全部商品', hint: '不按系统建议筛选' },
  { key: '需要订货', label: '需要订货', hint: '剩余不足，核对后补进' },
  { key: '需要备货', label: '需要备货', hint: '先查货架和后仓' },
  { key: '需要清仓', label: '需要清仓', hint: '长期不动销，考虑降价' },
  { key: '值得囤货', label: '值得囤货', hint: '热销且毛利好，保安全库存' },
  { key: '好卖', label: '好卖', hint: '动销较好，避免断货' },
  { key: '观察', label: '观察', hint: '数据或毛利异常，先核对' },
]

/** 可卖天数低于该值判定为紧张，与后端 LowCoverDays 一致。 */
export const LOW_COVER_DAYS = 14

/** 可卖天数进度条的满格刻度，与后端 StockUpCoverDays 一致。 */
export const COVER_DAYS_FULL_SCALE = 30

/** 销售统计超过该天数未更新时，页头的更新时间要提示风险。 */
export const STALE_STATISTIC_DAYS = 1

/**
 * 可卖天数换算成进度条占比：无销量（天数为空）按满格处理，避免空条误读成缺货。
 */
export function getCoverDaysRatio(coverDays?: number | null) {
  if (typeof coverDays !== 'number' || !Number.isFinite(coverDays)) {
    return 1
  }

  if (coverDays <= 0) {
    return 0
  }

  return Math.min(coverDays / COVER_DAYS_FULL_SCALE, 1)
}

/** 可卖天数是否紧张；紧张时剩余量与进度条标红。 */
export function isCoverDaysTight(coverDays?: number | null) {
  return typeof coverDays === 'number' && Number.isFinite(coverDays) && coverDays <= LOW_COVER_DAYS
}

export function getSuggestionTagColor(suggestion?: string) {
  switch (suggestion) {
    case '需要订货':
      return 'red'
    case '需要备货':
      return 'orange'
    case '需要清仓':
      return 'volcano'
    case '值得囤货':
      return 'purple'
    case '好卖':
      return 'green'
    case '观察':
      return 'blue'
    default:
      return 'default'
  }
}

export function getCredibilityTagColor(credibility?: string) {
  switch (credibility) {
    case '高':
      return 'green'
    case '中':
      return 'gold'
    case '低':
      return 'red'
    default:
      return 'default'
  }
}

export function formatAud(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '--'
  }

  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
  }).format(value)
}

export function formatNumber(value?: number | null, fractionDigits = 0) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '--'
  }

  return value.toLocaleString('en-AU', {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  })
}

export function formatPercent(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '--'
  }

  return new Intl.NumberFormat('en-AU', {
    style: 'percent',
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(value)
}
