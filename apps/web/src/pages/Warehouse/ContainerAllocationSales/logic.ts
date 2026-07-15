import dayjs, { type Dayjs } from 'dayjs'
import type {
  ContainerAllocationSalesBranch,
  ContainerAllocationSalesQuery,
  ContainerAllocationSalesSortDirection,
} from '../../../types/containerAllocationSales'

interface TablePaginationLike {
  current?: number
  pageSize?: number
}

interface TableSorterLike {
  field?: string | number
  order?: 'ascend' | 'descend' | null
}

export function buildRangeByWeeks(arrivalDate: string, weeks: number, today = dayjs()) {
  const start = dayjs(arrivalDate).startOf('day')
  const requestedEnd = start.add(weeks * 7 - 1, 'day')
  const end = requestedEnd.isAfter(today, 'day') ? today.startOf('day') : requestedEnd
  return {
    startDate: start.format('YYYY-MM-DD'),
    endDate: end.format('YYYY-MM-DD'),
  }
}

export function buildQuickRangeQuery(arrivalDate: string, weeks: number, today = dayjs()) {
  return {
    ...buildRangeByWeeks(arrivalDate, weeks, today),
    pageNumber: 1,
  }
}

export function buildBranchQuery(productCode: string, startDate: string, endDate: string) {
  return { productCode, startDate, endDate }
}

export function buildContainerAllocationSalesQuery(
  committed: ContainerAllocationSalesQuery,
  overrides: ContainerAllocationSalesQuery = {},
) {
  return { ...committed, ...overrides }
}

export function isCustomEndDateDisabled(current: Dayjs, arrivalDate: string, today = dayjs()) {
  const start = dayjs(arrivalDate).startOf('day')
  return current.isBefore(start, 'day') || current.isAfter(today, 'day')
}

export function mapTableChangeToQuery(
  pagination: TablePaginationLike,
  sorter: TableSorterLike,
) {
  // AntD 取消排序时仍可能保留 field，必须以 order 是否存在作为有效排序依据。
  const hasActiveSort = sorter.order === 'ascend' || sorter.order === 'descend'
  return {
    pageNumber: pagination.current ?? 1,
    pageSize: pagination.pageSize ?? 20,
    sortBy: hasActiveSort && typeof sorter.field === 'string' ? sorter.field : 'productCode',
    sortDirection: (sorter.order === 'descend' ? 'desc' : 'asc') as ContainerAllocationSalesSortDirection,
  }
}

export function getPaginatedRowNumber(pageNumber: number, pageSize: number, rowIndex: number) {
  // 服务端分页场景下按全表位置编号，避免每页都从 1 重新开始。
  return (Math.max(1, pageNumber) - 1) * Math.max(1, pageSize) + rowIndex + 1
}

type ContainerAllocationSalesBranchSortField =
  | 'branchCode'
  | 'branchName'
  | 'isActive'
  | 'allocationQuantity'
  | 'allocationImportAmount'
  | 'salesQuantity'
  | 'salesAmount'
  | 'averageSalesPrice'
  | 'grossMarginRate'

const BRANCH_TEXT_COLLATOR = new Intl.Collator('zh-CN', {
  numeric: true,
  sensitivity: 'base',
})

export function compareContainerAllocationSalesBranches(
  left: ContainerAllocationSalesBranch,
  right: ContainerAllocationSalesBranch,
  field: ContainerAllocationSalesBranchSortField,
  sortOrder?: 'ascend' | 'descend' | null,
) {
  const leftValue = left[field]
  const rightValue = right[field]

  // AntD 会在降序时翻转比较结果，因此这里先反向返回，确保空统计值始终排在有效值后。
  if (leftValue == null && rightValue == null) return 0
  if (leftValue == null) return sortOrder === 'descend' ? -1 : 1
  if (rightValue == null) return sortOrder === 'descend' ? 1 : -1

  if (typeof leftValue === 'string' && typeof rightValue === 'string') {
    return BRANCH_TEXT_COLLATOR.compare(leftValue, rightValue)
  }
  if (typeof leftValue === 'boolean' && typeof rightValue === 'boolean') {
    return Number(leftValue) - Number(rightValue)
  }
  return Number(leftValue) - Number(rightValue)
}

const TABLE_ROW_INTERACTIVE_SELECTOR = [
  'a',
  'button',
  'input',
  'select',
  'textarea',
  'summary',
  '[role="button"]',
  '[role="link"]',
  '[contenteditable="true"]',
  '[tabindex]:not([tabindex="-1"])',
  '[data-row-click-ignore]',
].join(',')

export function shouldTriggerTableRowClick(target: unknown, currentTarget: unknown) {
  if (!target || typeof target !== 'object') return true
  const closest = (target as { closest?: (selector: string) => unknown }).closest
  if (typeof closest !== 'function') return true

  // 命中行内独立交互控件时由控件自行处理；普通单元格内容才触发整行行为。
  const interactiveTarget = closest.call(target, TABLE_ROW_INTERACTIVE_SELECTOR)
  return interactiveTarget == null || interactiveTarget === currentTarget
}

export function getGrossMarginDisplay(metric: {
  grossMarginRate: number | null
  isGrossMarginComplete: boolean | null
}) {
  if (metric.isGrossMarginComplete === false) return '成本缺失'
  if (metric.grossMarginRate == null) return '-'
  return `${(metric.grossMarginRate * 100).toFixed(2)}%`
}

export function formatAustralianCurrency(value: number | null | undefined) {
  if (value == null) return '-'
  return `$${value.toLocaleString('en-AU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`
}

const STATISTIC_MESSAGE_AMOUNT_PATTERN =
  /(商品金额|分店营业额|金额差|未匹配供应商金额)\s+(-?\d+(?:,\d{3})*(?:\.\d+)?)/g

function incrementDecimalDigits(value: string) {
  const digits = value.split('')
  for (let index = digits.length - 1; index >= 0; index -= 1) {
    if (digits[index] === '9') {
      digits[index] = '0'
      continue
    }
    digits[index] = String.fromCharCode(digits[index].charCodeAt(0) + 1)
    return digits.join('')
  }
  return `1${digits.join('')}`
}

function formatExactDecimalAmount(rawAmount: string) {
  const normalized = rawAmount.replace(/,/g, '')
  const negative = normalized.startsWith('-')
  const unsigned = negative ? normalized.slice(1) : normalized
  const [rawInteger, fraction = ''] = unsigned.split('.')
  let integer = rawInteger.replace(/^0+(?=\d)/, '') || '0'
  let cents = `${fraction}00`.slice(0, 2)

  // 第三位小数按十进制字符串进位，避免超长 .NET decimal 被 JS Number 截断精度。
  if ((fraction[2] ?? '0') >= '5') {
    const rounded = incrementDecimalDigits(`${integer}${cents}`)
    integer = rounded.slice(0, -2) || '0'
    cents = rounded.slice(-2).padStart(2, '0')
  }

  const groupedInteger = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ',')
  // 四舍五入后为零时移除负号，避免对账提示出现误导性的 -0.00。
  const sign = negative && (integer !== '0' || cents !== '00') ? '-' : ''
  return `${sign}${groupedInteger}.${cents}`
}

export function formatStatisticMessageAmounts(message: string | null | undefined) {
  if (!message) return message

  // 仅格式化对账文案中的明确金额字段，数量、日期和诊断说明保持后端原文。
  return message.replace(STATISTIC_MESSAGE_AMOUNT_PATTERN, (_match, label: string, rawAmount: string) => {
    return `${label} ${formatExactDecimalAmount(rawAmount)}`
  })
}

interface ContainerAllocationSalesViewStateInput {
  canQuery: boolean
  total: number
  statisticStatus: string
  allocationQuantity: number
  salesQuantity: number | null
  search: string
}

interface ContainerAllocationSalesAutoLoadInput {
  active: boolean
  requestedContainerGuid: string
  loadedContainerGuid: string | null
}

export function shouldLoadContainerAllocationSales(input: ContainerAllocationSalesAutoLoadInput) {
  return Boolean(
    input.active &&
    input.requestedContainerGuid &&
    input.requestedContainerGuid !== input.loadedContainerGuid,
  )
}

export function getContainerAllocationSalesViewState(input: ContainerAllocationSalesViewStateInput) {
  if (!input.canQuery) {
    return {
      showTable: false,
      emptyDescription: null,
      showNoStatistics: false,
    }
  }

  if (input.total === 0) {
    return {
      showTable: false,
      emptyDescription: input.search.trim() ? '未找到匹配商品' : '暂无货柜商品',
      showNoStatistics: false,
    }
  }

  return {
    showTable: true,
    emptyDescription: null,
    // 统计未刷新时销售字段为空，不能把“未知”误判成“没有数据”。
    showNoStatistics:
      input.statisticStatus === 'Fresh' &&
      input.allocationQuantity === 0 &&
      (input.salesQuantity ?? 0) === 0,
  }
}
