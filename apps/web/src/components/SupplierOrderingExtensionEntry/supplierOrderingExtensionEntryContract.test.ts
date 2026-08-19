import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const read = (path: string) => readFileSync(path, 'utf8')
const readJson = (path: string) => JSON.parse(read(path)) as Record<string, unknown>

const layout = read('src/layout/ShopLayout.tsx')
const entry = read('src/components/SupplierOrderingExtensionEntry/SupplierOrderingExtensionEntry.tsx')
const zh = readJson('src/i18n/locales/zh.json')
const en = readJson('src/i18n/locales/en.json')

assert.ok(
  layout.includes("import SupplierOrderingExtensionEntry from '../components/SupplierOrderingExtensionEntry'"),
  'ShopLayout 必须导入 SupplierOrderingExtensionEntry',
)
assert.ok(
  layout.includes("const isShopHomePage = location.pathname === '/shop'"),
  'isShopHomePage 必须保持精确等于 /shop',
)
assert.ok(
  layout.includes('{isShopHomePage ? <SupplierOrderingExtensionEntry /> : null}'),
  '入口必须以 exact /shop 条件挂载',
)

const preorderGateIndex = layout.indexOf('showPreorderGateAlert ? (')
const entryIndex = layout.indexOf('<SupplierOrderingExtensionEntry />')
const outletIndex = layout.indexOf('<Outlet />')
assert.ok(preorderGateIndex >= 0, '预订拦截提示块必须存在')
assert.ok(entryIndex >= 0, '扩展入口必须存在')
assert.ok(outletIndex > entryIndex, '扩展入口必须位于 Outlet 之前')
assert.ok(preorderGateIndex < entryIndex, '扩展入口必须位于预订拦截提示之后')

const requiredI18nKeys = [
  'name',
  'checking',
  'statusNotInstalled',
  'statusInstalled',
  'statusOptionalUpdate',
  'statusForcedUpdate',
  'installAssistant',
  'version',
  'recheck',
  'notPublished',
  'installEdge',
  'installChrome',
  'openAssistant',
  'openFailed',
  'mobileHint',
  'releaseUnavailable',
  'releaseNotes',
  'recommended',
]

assert.ok(
  entry.includes("t('supplierOrderingExtension.installAssistant')"),
  '未安装状态必须提供“安装订货助手”操作文案',
)

const zhEntry = zh.supplierOrderingExtension as Record<string, unknown> | undefined
const enEntry = en.supplierOrderingExtension as Record<string, unknown> | undefined
assert.ok(zhEntry, '中文语言包必须包含 supplierOrderingExtension')
assert.ok(enEntry, '英文语言包必须包含 supplierOrderingExtension')

for (const key of requiredI18nKeys) {
  assert.equal(typeof zhEntry?.[key], 'string', `zh.supplierOrderingExtension.${key} 必须为字符串`)
  assert.equal(typeof enEntry?.[key], 'string', `en.supplierOrderingExtension.${key} 必须为字符串`)
}

console.log('supplierOrderingExtensionEntryContract.test: ok')
