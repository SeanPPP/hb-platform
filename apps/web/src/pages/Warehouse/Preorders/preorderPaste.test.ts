import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import {
  applyPreorderPasteTextChange,
  canSavePreorderTemplate,
  parsePreorderPaste,
  removePreorderPasteItem,
} from './preorderPaste'

const formatZhPasteMessage = (key: 'missingFields' | 'invalidMoq' | 'moqConflict' | 'empty', values?: { lineNumber?: number; itemNumber?: string }) => {
  if (key === 'missingFields') return `第 ${values?.lineNumber} 行缺少货号或最小订货量`
  if (key === 'invalidMoq') return `第 ${values?.lineNumber} 行最小订货量必须为正整数`
  if (key === 'moqConflict') return `第 ${values?.lineNumber} 行货号 ${values?.itemNumber} 与前面行的最小订货量冲突`
  return '请粘贴至少一行“货号 + 最小订货量”'
}

const parsed = parsePreorderPaste('货号\t最小订货量\nABC-1\t6\nabc-1\t6\nXYZ\t12', formatZhPasteMessage)
assert.deepEqual(parsed.errors, [])

const translatedInvalid = parsePreorderPaste('ABC-1\t0', (key, values) => `${key}:${values?.lineNumber ?? ''}`)
assert.deepEqual(translatedInvalid.errors, ['invalidMoq:1'], '粘贴校验错误必须允许由当前语言包生成')
assert.deepEqual(parsed.rows.map(({ itemNumber, minimumOrderQuantity }) => [itemNumber, minimumOrderQuantity]), [
  ['ABC-1', 6],
  ['abc-1', 6],
  ['XYZ', 12],
])

const duplicate = parsePreorderPaste('ABC\t6\nABC\t6', formatZhPasteMessage)
assert.equal(duplicate.rows.length, 1)
assert.deepEqual(duplicate.errors, [])

const conflict = parsePreorderPaste('ABC\t6\nABC\t12\nBAD\t0', formatZhPasteMessage)
assert.equal(conflict.rows.length, 1)
assert.equal(conflict.errors.length, 2)

const loadedItems = [{ itemNumber: 'EXISTING' }]
const loadedTemplate = { text: '', items: loadedItems, errors: ['old error'] }

// 程序加载已有模板时 pasteText 仍为空，不应误清已加载商品。
assert.equal(applyPreorderPasteTextChange(loadedTemplate, ''), loadedTemplate)

// 用户实际修改粘贴文本后，旧预览和旧错误必须同时失效。
assert.deepEqual(applyPreorderPasteTextChange(loadedTemplate, 'NEW\t12'), {
  text: 'NEW\t12',
  items: [],
  errors: [],
})

const resolvedItems = [
  { lineNumber: 2, valid: true, productCode: 'PRODUCT-1' },
  { lineNumber: 3, valid: false, productCode: undefined },
]
const resolutionErrors = ['第 3 行：货号无法匹配']

// 只要当前还有解析错误，即使存在有效商品也不能保存。
assert.equal(canSavePreorderTemplate(resolvedItems, resolutionErrors), false)
const afterInvalidRowRemoved = removePreorderPasteItem(
  { items: resolvedItems, errors: resolutionErrors },
  3,
  '第 3 行',
)
assert.deepEqual(afterInvalidRowRemoved, {
  items: [{ lineNumber: 2, valid: true, productCode: 'PRODUCT-1' }],
  errors: [],
})
assert.equal(canSavePreorderTemplate(afterInvalidRowRemoved.items, afterInvalidRowRemoved.errors), true)

// 修改原始文本后旧商品被清空，不能借旧预览继续保存。
const changedPaste = applyPreorderPasteTextChange(
  { text: 'OLD\t6', items: resolvedItems, errors: [] },
  'NEW\t12',
)
assert.equal(canSavePreorderTemplate(changedPaste.items, changedPaste.errors), false)

const preorderPageSource = readFileSync('src/pages/Warehouse/Preorders/index.tsx', 'utf8')
const activationDetailSource = readFileSync('src/pages/Warehouse/Preorders/ActivationDetail.tsx', 'utf8')
const zhLocale = JSON.parse(readFileSync('src/i18n/locales/zh.json', 'utf8')) as Record<string, unknown>
const enLocale = JSON.parse(readFileSync('src/i18n/locales/en.json', 'utf8')) as Record<string, unknown>
const saveTemplateSource = preorderPageSource.slice(
  preorderPageSource.indexOf('const saveTemplate = async'),
  preorderPageSource.indexOf('const openActivation = async'),
)

// 当前仍显示粘贴错误时，保存入口必须显式阻止请求。
assert.match(saveTemplateSource, /pasteErrors\.length/)
// 移除预览行必须经过统一 helper，同步清理该行错误。
assert.match(preorderPageSource, /onClick=\{\(\) => \{\s*const nextState = removePreorderPasteItem/s)

const getLocaleValue = (locale: Record<string, unknown>, key: string) => key.split('.').reduce<unknown>((value, segment) => {
  if (!value || typeof value !== 'object') return undefined
  return (value as Record<string, unknown>)[segment]
}, locale)
const requiredKeys = new Set([
  'warehouse.preorders.title',
  'warehouse.preorders.activationStatus.Scheduled',
  'warehouse.preorders.activationStatus.Active',
  'warehouse.preorders.activationStatus.Closed',
  'warehouse.preorders.activationStatus.Cancelled',
  'warehouse.preorders.orderStatus.Submitted',
  'warehouse.preorders.orderStatus.Processing',
  'warehouse.preorders.orderStatus.Completed',
  'warehouse.preorders.orderStatus.Cancelled',
])
for (const source of [preorderPageSource, activationDetailSource]) {
  for (const match of source.matchAll(/t\('(warehouse\.preorders\.[^']+)'/g)) requiredKeys.add(match[1])
}
for (const key of requiredKeys) {
  assert.equal(typeof getLocaleValue(zhLocale, key), 'string', `zh 缺少 ${key}`)
  assert.equal(typeof getLocaleValue(enLocale, key), 'string', `en 缺少 ${key}`)
}
for (const [source, copies] of [
  [preorderPageSource, ['模板名称', '创建模板', '解析并预览', '激活新一期']],
  [activationDetailSource, ['批次详情加载失败', '订单状态已更新', '导出 Excel', '延长有效期']],
] as const) {
  for (const copy of copies) {
    assert(!source.includes(`'${copy}'`) && !source.includes(`>${copy}<`), `Preorder 页面仍硬编码文案：${copy}`)
  }
}
console.log('preorderPaste tests passed')
