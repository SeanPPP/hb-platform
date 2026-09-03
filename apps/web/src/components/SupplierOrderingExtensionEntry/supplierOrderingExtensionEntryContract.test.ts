import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const read = (path: string) => readFileSync(path, 'utf8')
const readJson = (path: string) => JSON.parse(read(path)) as Record<string, unknown>

const layout = read('src/layout/ShopLayout.tsx')
const entry = read('src/components/SupplierOrderingExtensionEntry/SupplierOrderingExtensionEntry.tsx')
const entryCss = read('src/components/SupplierOrderingExtensionEntry/supplierOrderingExtension.css')
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
  layout.includes('isShopHomePage && !isMobileShopLayout')
    && layout.includes('<SupplierOrderingExtensionEntry presentation="desktop" />'),
  '宽屏入口必须以 exact /shop 和桌面布局条件挂载',
)

const preorderGateIndex = layout.indexOf('showPreorderGateAlert ? (')
const entryIndex = layout.indexOf('<SupplierOrderingExtensionEntry presentation="desktop" />')
const outletIndex = layout.indexOf('<Outlet />')
const routeBoundaryIndex = layout.indexOf('<RouteLoadBoundary resetKey={location.pathname}>')
const desktopLanguageIndex = layout.indexOf('<LanguageSwitch className="shop-header-language"')
const desktopAccountDropdownIndex = layout.indexOf('<Dropdown', desktopLanguageIndex)
const desktopLogoutIndex = layout.indexOf("key: 'logout'", desktopAccountDropdownIndex)
const desktopLogoutLabelIndex = layout.indexOf("label: t('layout.logout', 'Log Out')", desktopLogoutIndex)
const desktopLogoutHandlerIndex = layout.indexOf('onClick: () => void handleLogout()', desktopLogoutIndex)
assert.ok(preorderGateIndex >= 0, '预订拦截提示块必须存在')
assert.ok(routeBoundaryIndex >= 0 && routeBoundaryIndex < outletIndex, 'Shop 页面内容必须由局部路由加载边界承载')
assert.ok(entryIndex >= 0, '扩展入口必须存在')
assert.ok(desktopLanguageIndex >= 0, '桌面端语言切换必须存在')
assert.ok(desktopAccountDropdownIndex >= 0, '桌面端账户下拉菜单必须存在')
assert.ok(
  desktopLogoutIndex >= 0
    && desktopLogoutLabelIndex > desktopLogoutIndex
    && desktopLogoutHandlerIndex > desktopLogoutIndex,
  '桌面端账户下拉菜单必须保留退出登录入口及处理函数',
)
assert.ok(entryIndex < desktopLanguageIndex, '扩展入口必须位于桌面端语言切换之前')
assert.ok(desktopLanguageIndex < desktopAccountDropdownIndex, '账户下拉菜单必须位于桌面端语言切换之后')
assert.ok(entryIndex < preorderGateIndex, '扩展入口不得继续渲染在商品内容区')
assert.ok(outletIndex > preorderGateIndex, 'Outlet 必须保留在预订拦截提示之后')

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
  'installSafari',
  'openAssistant',
  'openFailed',
  'mobileHint',
  'releaseUnavailable',
  'releaseNotes',
  'recommended',
  'unsupportedShort',
  'desktopSafariUnsupported',
  'desktopBrowserUnsupported',
  'iosBrowserUnsupported',
  'androidUnsupported',
  'safariNotPublished',
  'safariInstallIntro',
  'safariInstallStepStore',
  'safariInstallStepEnable',
  'safariInstallStepWebsite',
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

assert.ok(
  layout.includes("const SHOP_MOBILE_LAYOUT_QUERY = '(max-width: 768px)'")
    && layout.includes('window.matchMedia(SHOP_MOBILE_LAYOUT_QUERY)'),
  '商城入口布局判断必须与 CSS 的 768px 断点一致',
)
assert.ok(
  layout.includes('isShopHomePage && isMobileShopLayout')
    && layout.includes('<SupplierOrderingExtensionEntry presentation="mobile-nav" />'),
  '窄屏 /shop 必须在移动导航挂载单一订货助手入口',
)
assert.ok(
  entry.includes('resolveExtensionInstallExperience')
    && entry.includes("experience === 'desktop-edge'")
    && entry.includes("experience === 'desktop-chrome'")
    && entry.includes("experience === 'ios-safari'"),
  '安装弹窗必须按统一安装体验分类渲染对应入口',
)
assert.ok(
  entry.includes("t('supplierOrderingExtension.desktopSafariUnsupported')")
    && entry.includes("t('supplierOrderingExtension.desktopBrowserUnsupported')")
    && entry.includes("t('supplierOrderingExtension.iosBrowserUnsupported')")
    && entry.includes("t('supplierOrderingExtension.androidUnsupported')"),
  '所有不支持环境必须显示对应文案',
)
assert.ok(
  entry.includes('soe-safari-guide')
    && entry.includes("experience === 'ios-safari' && release?.safariStoreUrl")
    && entry.includes("t('supplierOrderingExtension.safariInstallStepEnable')")
    && entry.includes("t('supplierOrderingExtension.safariInstallStepWebsite')"),
  'iOS Safari 仅在已配置商店地址时显示安装、启用和网站权限引导',
)
assert.ok(
  entry.includes('supportsExtension ? (')
    && entry.includes("t('supplierOrderingExtension.recheck')"),
  '只有受支持环境可以显示重新检测按钮',
)
assert.match(
  entryCss,
  /\.soe-entry--mobile-nav[\s\S]*?\.soe-entry-trigger\.ant-btn[\s\S]*?min-height:\s*44px/,
  '移动导航入口必须提供至少 44px 的触摸高度',
)

console.log('supplierOrderingExtensionEntryContract.test: ok')
