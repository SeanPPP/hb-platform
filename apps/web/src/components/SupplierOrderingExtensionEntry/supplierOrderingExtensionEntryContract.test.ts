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
  'statusSafariNotConnected',
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
  'safariInstallDescription',
  'safariInstallStepStoreTitle',
  'safariInstallStepStore',
  'safariInstallStepEnableTitle',
  'safariInstallStepEnable',
  'safariInstallStepWebsiteTitle',
  'safariInstallStepWebsite',
  'safariInstallRecheck',
  'safariInstallRecheckHint',
  'safariReadyDescription',
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
    && entry.includes("experience === 'ios-safari' && release?.safariStoreUrl && !installed")
    && entry.includes("t('supplierOrderingExtension.safariInstallStepStoreTitle')")
    && entry.includes("t('supplierOrderingExtension.safariInstallStepEnable')")
    && entry.includes("t('supplierOrderingExtension.safariInstallStepWebsite')"),
  'iOS Safari 仅在已配置商店地址时显示安装、启用和网站权限引导',
)
assert.ok(
  entry.includes('href={url}')
    && entry.includes('target="_blank"')
    && entry.includes('rel="noopener noreferrer"'),
  'Safari 商店地址必须渲染为安全、可点击的外部链接',
)
assert.ok(
  entry.includes("experience === 'ios-safari' && (!installed || isForced || isOptional)"),
  'Safari 已连接且版本最新时不得继续展示重复的下载入口',
)
assert.ok(
  entry.includes("experience === 'ios-safari' && installed && !isForced && !isOptional"),
  'Safari 已连接且版本最新时不得保留空的下载区域',
)
assert.ok(
  entry.includes("experience === 'ios-safari' && release.safariStoreUrl && !installed")
    && entry.includes("? t('supplierOrderingExtension.safariNotPublished')"),
  'Safari 商店地址为空时必须保留不可点击的未发布说明',
)
assert.ok(
  entry.includes('className="soe-safari-recheck"')
    && entry.includes("t('supplierOrderingExtension.safariInstallRecheck')")
    && entry.includes('onClick={runHandshake}'),
  'Safari 引导末尾必须提供就近的完成后重新检测操作',
)
assert.ok(
  entry.includes('<ol className="soe-safari-steps" role="list">'),
  'Safari/VoiceOver 必须保留三步引导的列表语义',
)
assert.ok(
  entry.includes("t('supplierOrderingExtension.statusSafariNotConnected')")
    && entry.includes('aria-live="polite"'),
  'Safari 未连接状态必须准确表述，并让辅助技术获知检测结果',
)
assert.equal(
  zhEntry?.openAssistant,
  '打开供应商下单助手',
  '成功操作不得再提示二次登录',
)
assert.equal(
  enEntry?.openAssistant,
  'Open Supplier Ordering Assistant',
  '英文成功操作不得再提示二次登录',
)
assert.match(
  String(zhEntry?.safariInstallStepEnable),
  /iOS\/iPadOS 18[\s\S]*设置 → Apps → Safari → 扩展[\s\S]*iOS\/iPadOS 17[\s\S]*设置 → Safari → 扩展/,
  '中文启用步骤必须同时覆盖 iOS/iPadOS 18 与 17',
)
assert.match(
  String(enEntry?.safariInstallStepEnable),
  /iOS\/iPadOS 18[\s\S]*Settings → Apps → Safari → Extensions[\s\S]*iOS\/iPadOS 17[\s\S]*Settings → Safari → Extensions/,
  '英文启用步骤必须同时覆盖 iOS/iPadOS 18 与 17',
)
assert.match(
  String(zhEntry?.safariInstallStepWebsite),
  /https:\/\/hotbargain\.vip\/shop/,
  '网站权限步骤必须返回精确的 /shop 地址',
)
assert.ok(
  entry.includes("supportsExtension && !(experience === 'ios-safari' && release?.safariStoreUrl && !installed) ? (")
    && entry.includes("t('supplierOrderingExtension.recheck')"),
  '桌面与已连接状态必须保留重新检测入口，未连接 Safari 只显示步骤内操作',
)
assert.match(
  entryCss,
  /\.soe-entry--mobile-nav[\s\S]*?\.soe-entry-trigger\.ant-btn[\s\S]*?min-height:\s*44px/,
  '移动导航入口必须提供至少 44px 的触摸高度',
)
assert.match(
  entryCss,
  /@media\s*\(any-pointer:\s*coarse\)[\s\S]*?\.soe-entry--desktop\s+\.soe-entry-trigger\.ant-btn[\s\S]*?min-height:\s*44px/,
  'iPad 等粗指针设备的宽屏入口必须提供至少 44px 的触摸高度',
)
assert.match(
  entryCss,
  /\.soe-safari-recheck\.ant-btn[\s\S]*?min-height:\s*44px/,
  'Safari 完成后重新检测按钮必须提供至少 44px 的触摸高度',
)

console.log('supplierOrderingExtensionEntryContract.test: ok')
