import { readFileSync } from 'node:fs'
import type { ContainerDetail } from '../../../types/container'
import {
  CONTAINER_DETAIL_COLUMN_PASTE_KEYS,
  buildContainerDetailColumnPastePlan,
  getContainerDetailColumnPasteCurrentValue,
  parseContainerDetailColumnPasteText,
  parseContainerDetailColumnPasteValue,
} from './containerDetailColumnPaste'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}\nexpected: ${String(expected)}\nactual: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\nexpected: ${expectedJson}\nactual: ${actualJson}`)
  }
}

function makeRow(overrides: Partial<ContainerDetail> & { id: number }): ContainerDetail {
  return {
    hguid: `hguid-${overrides.id}`,
    是否新商品: true,
    ...overrides,
  } as ContainerDetail
}

const rowKey = (row: ContainerDetail) => row.hguid || String(row.id)

// ---- 剪贴板文本解析 ----

assertDeepEqual(
  parseContainerDetailColumnPasteText('12.5\r\n\r\n$ 13,000.25\r\n\r\n'),
  { ok: true, values: ['12.5', '', '$ 13,000.25'] },
  '单列文本应保留中间空单元格并去掉末尾空行',
)

assertDeepEqual(
  parseContainerDetailColumnPasteText('1\t2\n3\t4\n'),
  { ok: false, reason: 'multiple_columns' },
  '多列数据必须拒绝，避免错位写入',
)

assertDeepEqual(
  parseContainerDetailColumnPasteText('1\t\n2\t\n'),
  { ok: true, values: ['1', '2'] },
  '尾随空制表符不应被当作多列',
)

assertDeepEqual(
  parseContainerDetailColumnPasteText('\n\n'),
  { ok: false, reason: 'empty' },
  '只有空行时应返回 empty',
)

assertDeepEqual(
  parseContainerDetailColumnPasteText('"Xmas Water Lantern\nChurch"\nSanta\n'),
  { ok: true, values: ['Xmas Water Lantern\nChurch', 'Santa'] },
  'Excel 引号包裹的单元格内换行应保留在同一个值内',
)

// ---- 单值解析 ----

assertDeepEqual(parseContainerDetailColumnPasteValue('importPrice', '$ 21.239'), { ok: true, value: 21.24 }, '进口价格应剥离货币符号并保留两位小数')
assertDeepEqual(parseContainerDetailColumnPasteValue('oemPrice', '1,299.5'), { ok: true, value: 1299.5 }, '零售价应剥离千分位')
assertDeepEqual(parseContainerDetailColumnPasteValue('oemPrice', '-3'), { ok: false }, '负数零售价应视为无效')
assertDeepEqual(parseContainerDetailColumnPasteValue('unitVolume', '0.12345'), { ok: true, value: 0.123 }, '单件体积保留三位小数')
assertDeepEqual(parseContainerDetailColumnPasteValue('packingQuantity', '12.0'), { ok: true, value: 12 }, '整数列接受 12.0 这类 Excel 输出')
assertDeepEqual(parseContainerDetailColumnPasteValue('packingQuantity', '12.5'), { ok: false }, '整数列拒绝小数')
assertDeepEqual(parseContainerDetailColumnPasteValue('middlePackQuantity', 'abc'), { ok: false }, '非数字应视为无效')
assertDeepEqual(parseContainerDetailColumnPasteValue('floatRate', '1.3'), { ok: true, value: 1.3 }, '调整浮率应按两位小数解析')
assertDeepEqual(parseContainerDetailColumnPasteValue('englishName', 'Xmas Lantern'), { ok: true, value: 'Xmas Lantern' }, '英文名称接受纯英文')
assertDeepEqual(parseContainerDetailColumnPasteValue('englishName', 'Xmas 灯笼'), { ok: false }, '英文名称含中文应视为无效')
assertDeepEqual(parseContainerDetailColumnPasteValue('remark', '  易碎 '), { ok: true, value: '易碎' }, '备注应去除首尾空白')
assertDeepEqual(parseContainerDetailColumnPasteValue('remark', '   '), { ok: false }, '空白文本不应生成值')

// ---- 当前值读取 ----

assertEqual(
  getContainerDetailColumnPasteCurrentValue('oemPrice', makeRow({ id: 1, 是否新商品: false, 贴牌价格: 10, warehouseOEMPrice: 12 })),
  12,
  '已有商品的零售价当前值应取仓库实时价',
)
assertEqual(
  getContainerDetailColumnPasteCurrentValue('englishName', makeRow({ id: 2, 商品信息: { 英文名称: 'Santa' } as ContainerDetail['商品信息'] })),
  'Santa',
  '英文名称当前值应回退到商品信息',
)

// ---- 粘贴计划 ----

const rows = [
  makeRow({ id: 1, 进口价格: 21.24 }),
  makeRow({ id: 2, 进口价格: 18.69 }),
  makeRow({ id: 3, 进口价格: 18.69 }),
  makeRow({ id: 4, 进口价格: 32.44 }),
  makeRow({ id: 5, hguid: undefined, 进口价格: 70.61 }),
]

const plan = buildContainerDetailColumnPastePlan({
  columnKey: 'importPrice',
  values: ['22', '', 'abc', '32.44', '71', '99'],
  rows,
  startRowKey: 'hguid-1',
  getRowKey: rowKey,
})
assertEqual(plan.error, undefined, '起始行存在时不应报错')
assertDeepEqual(
  plan.entries.map((entry) => [entry.row.hguid, entry.field, entry.value]),
  [['hguid-1', '进口价格', 22]],
  '只有有效且与当前值不同、并且已落库的行才生成补丁',
)
assertEqual(plan.appliedCount, 1, '应用数应为 1')
assertEqual(plan.skippedBlankCount, 1, '空单元格应跳过并计数')
assertEqual(plan.invalidCount, 2, '非法值与无 hguid 行都应计入无效')
assertDeepEqual(plan.invalidRowNumbers, [3, 5], '无效行号应对应粘贴数据中的行号')
assertEqual(plan.unchangedCount, 1, '与当前值相同的项应计入未变更')
assertEqual(plan.overflowCount, 1, '超出列表末尾的行应计数')

const middlePlan = buildContainerDetailColumnPastePlan({
  columnKey: 'remark',
  values: ['A', 'B'],
  rows,
  startRowKey: 'hguid-3',
  getRowKey: rowKey,
})
assertDeepEqual(
  middlePlan.entries.map((entry) => [entry.row.hguid, entry.value]),
  [['hguid-3', 'A'], ['hguid-4', 'B']],
  '应从起始行开始按显示顺序向下填充',
)

const missingPlan = buildContainerDetailColumnPastePlan({
  columnKey: 'remark',
  values: ['A'],
  rows,
  startRowKey: 'hguid-404',
  getRowKey: rowKey,
})
assertEqual(missingPlan.error, 'missing_target', '起始行不在列表中时应返回 missing_target')
assertEqual(missingPlan.entries.length, 0, '起始行缺失时不应生成补丁')

// ---- 页面接线契约 ----

const pageSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.tsx', 'utf8')
CONTAINER_DETAIL_COLUMN_PASTE_KEYS.forEach((columnKey) => {
  assert(
    pageSource.includes(`onPaste={(event) => handleEditableCellPaste(row, '${columnKey}', event)}`),
    `${columnKey} 列的可编辑单元格应接入列粘贴处理`,
  )
})
assert(
  pageSource.includes("if (parsed.values.length <= 1) return")
    && pageSource.includes('event.currentTarget.blur()')
    && pageSource.includes('applyContainerDetailColumnPaste(columnKey, parsed.values, rowKey(row))'),
  '单元格粘贴应只拦截多行数据，且先结束当前单元格编辑再批量写入',
)
assert(
  pageSource.includes("if (target.channel === 'pendingDraft') {")
    && pageSource.includes('markPendingDetailPatches(entries.map(')
    && pageSource.includes('enqueueAutoSavePatches(entries.map('),
  '列粘贴必须复用“保存明细”草稿与自动保存两条既有落库通道',
)
assert(
  pageSource.includes('target.requiresCostParameters')
    && pageSource.includes('showCostRecalculateWarning(getContainerDetailCostMissingFields(container))'),
  '联动成本的列在粘贴前必须检查货柜成本参数',
)

console.log('containerDetailColumnPaste tests passed')
