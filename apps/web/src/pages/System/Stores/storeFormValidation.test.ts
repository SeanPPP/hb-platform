import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

function assertIncludes(source: string, expected: string, label: string) {
  if (!source.includes(expected)) {
    throw new Error(`${label}. Missing: ${expected}`)
  }
}

function assertOccurrenceAtLeast(source: string, expected: string, count: number, label: string) {
  const actual = source.split(expected).length - 1
  if (actual < count) {
    throw new Error(`${label}. Expected at least ${count}, actual ${actual}. Missing: ${expected}`)
  }
}

const pageSource = readFileSync(resolve('src/pages/System/Stores/index.tsx'), 'utf8')
const detailPageSource = readFileSync(resolve('src/pages/System/Stores/Detail.tsx'), 'utf8')
const zhSource = readFileSync(resolve('src/i18n/locales/zh.json'), 'utf8')
const enSource = readFileSync(resolve('src/i18n/locales/en.json'), 'utf8')

assertIncludes(
  pageSource,
  "max: 20",
  '分店编辑表单应在前端限制联系电话最大长度，避免提交后才收到 400',
)
assertIncludes(
  pageSource,
  "t('system.stores.contactPhoneMaxLength'",
  '联系电话长度校验应使用分店模块自己的友好提示文案',
)
assertIncludes(
  zhSource,
  '"contactPhoneMaxLength": "联系电话不能超过 20 个字符"',
  '中文文案应明确说明联系电话最大长度',
)
assertIncludes(
  enSource,
  '"contactPhoneMaxLength": "Contact phone cannot exceed 20 characters"',
  '英文文案应明确说明联系电话最大长度',
)
assertIncludes(
  pageSource,
  "dataIndex: 'abn'",
  '分店列表应显示 ABN 列，方便列表直接核对商业号码',
)
assertOccurrenceAtLeast(
  pageSource,
  'name="abn"',
  2,
  '创建和编辑分店表单应提供 ABN 输入项',
)
assertIncludes(
  pageSource,
  "t('system.stores.abn')",
  'ABN 展示和表单标签应使用分店模块统一文案',
)
assertIncludes(
  pageSource,
  'detailStore.abn',
  '列表页内详情弹窗应展示 ABN 字段',
)
assertIncludes(
  detailPageSource,
  'store.abn',
  '独立分店详情页应展示 ABN 字段',
)
assertIncludes(
  detailPageSource,
  "t('system.stores.abn')",
  '独立分店详情页 ABN 标签应使用分店模块统一文案',
)
assertIncludes(
  zhSource,
  '"abn": "ABN"',
  '中文文案应包含 ABN 标签',
)
assertIncludes(
  zhSource,
  '"abnMaxLength": "ABN 不能超过 20 个字符"',
  '中文文案应明确说明 ABN 最大长度',
)
assertIncludes(
  enSource,
  '"abn": "ABN"',
  '英文文案应包含 ABN 标签',
)
assertIncludes(
  enSource,
  '"abnMaxLength": "ABN cannot exceed 20 characters"',
  '英文文案应明确说明 ABN 最大长度',
)

console.log('storeFormValidation.test: ok')
