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
  观察: '观察：数据或毛利异常，请先核对商品、成本、进货记录。',
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
