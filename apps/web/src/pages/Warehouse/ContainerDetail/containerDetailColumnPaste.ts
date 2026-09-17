/**
 * containerDetailColumnPaste — 货柜明细「粘贴 Excel 列数据」纯逻辑
 *
 * 职责边界：
 * - 把剪贴板文本解析成单列值（多列数据直接拒绝，避免错位写入）
 * - 按当前显示顺序，从起始行向下把每个值解析为目标列的行补丁
 * - 不触碰 React 状态和保存队列；页面层负责把结果送入自动保存或“保存明细”草稿通道
 */

import type { ContainerDetail } from '../../../types/container'
import { parseProductImportPasteText } from '../../DomesticPurchase/ProductImport/utils'
import {
  containsChineseText,
  getContainerDetailEnglishName,
  getContainerDetailVisibleOemPrice,
} from './containerDetailLogic'

export const CONTAINER_DETAIL_COLUMN_PASTE_KEYS = [
  'englishName',
  'packingQuantity',
  'unitVolume',
  'middlePackQuantity',
  'floatRate',
  'importPrice',
  'oemPrice',
  'remark',
] as const

export type ContainerDetailColumnPasteKey = typeof CONTAINER_DETAIL_COLUMN_PASTE_KEYS[number]

export type ContainerDetailColumnPasteField =
  | '英文名称'
  | '单件装箱数'
  | '单件体积'
  | '中包数'
  | '调整浮率'
  | '进口价格'
  | '贴牌价格'
  | '备注'

/**
 * 保存通道：
 * - pendingDraft：进口价格 / 零售价 / 英文名称，进入本地草稿，由“保存明细”统一落库
 * - autoSave：其余字段，进入自动保存队列
 * requiresCostParameters 为 true 的列会联动成本重算，缺少汇率/运费/总体积时页面层必须先拦截。
 */
export type ContainerDetailColumnPasteTarget = {
  field: ContainerDetailColumnPasteField
  channel: 'pendingDraft' | 'autoSave'
  requiresCostParameters: boolean
} & (
  | { kind: 'text' }
  | { kind: 'integer'; min?: number }
  | { kind: 'decimal'; precision: number; min?: number }
)

export const CONTAINER_DETAIL_COLUMN_PASTE_TARGETS: Record<ContainerDetailColumnPasteKey, ContainerDetailColumnPasteTarget> = {
  englishName: { field: '英文名称', kind: 'text', channel: 'pendingDraft', requiresCostParameters: false },
  packingQuantity: { field: '单件装箱数', kind: 'integer', min: 0, channel: 'autoSave', requiresCostParameters: true },
  unitVolume: { field: '单件体积', kind: 'decimal', precision: 3, min: 0, channel: 'autoSave', requiresCostParameters: true },
  middlePackQuantity: { field: '中包数', kind: 'integer', min: 0, channel: 'autoSave', requiresCostParameters: false },
  floatRate: { field: '调整浮率', kind: 'decimal', precision: 2, channel: 'autoSave', requiresCostParameters: true },
  importPrice: { field: '进口价格', kind: 'decimal', precision: 2, min: 0, channel: 'pendingDraft', requiresCostParameters: false },
  oemPrice: { field: '贴牌价格', kind: 'decimal', precision: 2, min: 0, channel: 'pendingDraft', requiresCostParameters: false },
  remark: { field: '备注', kind: 'text', channel: 'autoSave', requiresCostParameters: false },
}

export function isContainerDetailColumnPasteKey(value: unknown): value is ContainerDetailColumnPasteKey {
  return typeof value === 'string' && (CONTAINER_DETAIL_COLUMN_PASTE_KEYS as readonly string[]).includes(value)
}

export type ContainerDetailColumnPasteParseResult =
  | { ok: true; values: string[] }
  | { ok: false; reason: 'empty' | 'multiple_columns' }

/**
 * 把剪贴板文本解析为单列值。
 * - 复用 Excel 粘贴解析器，保留中间空单元格（对应 Excel 空格子），去掉末尾空行
 * - 除第一列外还有非空单元格时视为多列，拒绝粘贴
 */
export function parseContainerDetailColumnPasteText(text: string): ContainerDetailColumnPasteParseResult {
  const rows = parseProductImportPasteText(text ?? '')
  const hasExtraColumnData = rows.some((row) => row.slice(1).some((cell) => cell.trim() !== ''))
  if (hasExtraColumnData) return { ok: false, reason: 'multiple_columns' }

  const values = rows.map((row) => (row[0] ?? '').trim())
  while (values.length > 0 && values[values.length - 1] === '') {
    values.pop()
  }
  if (!values.length) return { ok: false, reason: 'empty' }
  return { ok: true, values }
}

export type ContainerDetailColumnPasteValueResult =
  | { ok: true; value: string | number }
  | { ok: false }

/** 单个单元格文本按目标列类型解析：数字列剥离货币符号和千分位，文本列做业务校验。 */
export function parseContainerDetailColumnPasteValue(
  columnKey: ContainerDetailColumnPasteKey,
  rawValue: string,
): ContainerDetailColumnPasteValueResult {
  const target = CONTAINER_DETAIL_COLUMN_PASTE_TARGETS[columnKey]
  const trimmed = rawValue.trim()
  if (!trimmed) return { ok: false }

  if (target.kind === 'text') {
    // 英文名称列沿用单元格编辑的校验：含中文的值不进入草稿。
    if (columnKey === 'englishName' && containsChineseText(trimmed)) return { ok: false }
    return { ok: true, value: trimmed }
  }

  const numericText = trimmed.replace(/[$¥￥€£₩₹,，\s]/g, '')
  if (!numericText) return { ok: false }
  const parsed = Number(numericText)
  if (!Number.isFinite(parsed)) return { ok: false }
  if (target.min != null && parsed < target.min) return { ok: false }

  if (target.kind === 'integer') {
    if (!Number.isInteger(parsed)) return { ok: false }
    return { ok: true, value: parsed }
  }
  return { ok: true, value: Number(parsed.toFixed(target.precision)) }
}

/** 取目标列当前显示值，用于跳过与现值相同的粘贴项，避免无意义的保存请求。 */
export function getContainerDetailColumnPasteCurrentValue(
  columnKey: ContainerDetailColumnPasteKey,
  row: ContainerDetail,
): string | number | undefined {
  switch (columnKey) {
    case 'englishName':
      return getContainerDetailEnglishName(row)?.trim() || undefined
    case 'oemPrice':
      return getContainerDetailVisibleOemPrice(row)
    case 'remark':
      return row.备注?.trim() || undefined
    default:
      return row[CONTAINER_DETAIL_COLUMN_PASTE_TARGETS[columnKey].field] as number | undefined
  }
}

export type ContainerDetailColumnPasteEntry = {
  row: ContainerDetail
  field: ContainerDetailColumnPasteField
  value: string | number
}

export type ContainerDetailColumnPastePlan = {
  entries: ContainerDetailColumnPasteEntry[]
  appliedCount: number
  unchangedCount: number
  skippedBlankCount: number
  invalidCount: number
  /** 无效值在粘贴数据中的行号（从 1 开始），用于提示用户回查 Excel。 */
  invalidRowNumbers: number[]
  /** 粘贴数据多于起始行之后的可见行数时，超出的行数。 */
  overflowCount: number
  error?: 'missing_target'
}

/**
 * 按当前显示顺序，从 startRowKey 所在行向下为每个粘贴值生成行补丁。
 * - 空单元格保持原值（不清空），与 Excel 行号一一对应
 * - 无效值、无 hguid（尚未落库）的行跳过并计数
 * - 与当前显示值相同的项不生成补丁
 */
export function buildContainerDetailColumnPastePlan(params: {
  columnKey: ContainerDetailColumnPasteKey
  values: readonly string[]
  rows: readonly ContainerDetail[]
  startRowKey: string
  getRowKey: (row: ContainerDetail) => string
}): ContainerDetailColumnPastePlan {
  const { columnKey, values, rows, startRowKey, getRowKey } = params
  const plan: ContainerDetailColumnPastePlan = {
    entries: [],
    appliedCount: 0,
    unchangedCount: 0,
    skippedBlankCount: 0,
    invalidCount: 0,
    invalidRowNumbers: [],
    overflowCount: 0,
  }
  const startIndex = rows.findIndex((row) => getRowKey(row) === startRowKey)
  if (startIndex < 0) {
    return { ...plan, error: 'missing_target' }
  }
  const field = CONTAINER_DETAIL_COLUMN_PASTE_TARGETS[columnKey].field

  values.forEach((rawValue, offset) => {
    const row = rows[startIndex + offset]
    if (!row) {
      plan.overflowCount += 1
      return
    }
    if (!rawValue.trim()) {
      plan.skippedBlankCount += 1
      return
    }
    const parsed = parseContainerDetailColumnPasteValue(columnKey, rawValue)
    if (!parsed.ok || !row.hguid) {
      plan.invalidCount += 1
      plan.invalidRowNumbers.push(offset + 1)
      return
    }
    if (getContainerDetailColumnPasteCurrentValue(columnKey, row) === parsed.value) {
      plan.unchangedCount += 1
      return
    }
    plan.entries.push({ row, field, value: parsed.value })
    plan.appliedCount += 1
  })

  return plan
}
