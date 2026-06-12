import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

function assertIncludes(source: string, expected: string, label: string) {
  if (!source.includes(expected)) {
    throw new Error(`${label}. Missing: ${expected}`)
  }
}

const pageSource = readFileSync(resolve('src/pages/System/Stores/index.tsx'), 'utf8')
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

console.log('storeFormValidation.test: ok')
