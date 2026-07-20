import { readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function collectSourceFiles(directory: string): string[] {
  const entries = readdirSync(directory)
  const files: string[] = []

  for (const entry of entries) {
    const fullPath = path.join(directory, entry)
    const stat = statSync(fullPath)

    if (stat.isDirectory()) {
      files.push(...collectSourceFiles(fullPath))
      continue
    }

    if (/\.(tsx?|jsx?)$/.test(entry)) {
      files.push(fullPath)
    }
  }

  return files
}

const root = process.cwd()
const pagesDirectory = path.join(root, 'src/pages')
const pageReloadFiles = collectSourceFiles(pagesDirectory).filter((file) =>
  readFileSync(file, 'utf8').includes('window.location.reload'),
)

assert(
  pageReloadFiles.length === 0,
  `页面文件不应自动 hard reload: ${pageReloadFiles.map((file) => path.relative(root, file)).join(', ')}`,
)

const mobileLayoutSource = readFileSync(path.join(root, 'src/layout/MobileLayout.tsx'), 'utf8')
const adminLayoutSource = readFileSync(path.join(root, 'src/layout/AdminLayout.tsx'), 'utf8')
const shopLayoutSource = readFileSync(path.join(root, 'src/layout/ShopLayout.tsx'), 'utf8')
const shopCartDrawerSource = readFileSync(path.join(root, 'src/components/ShopCartDrawer.tsx'), 'utf8')
const errorBoundarySource = readFileSync(path.join(root, 'src/components/GlobalErrorBoundary.tsx'), 'utf8')
const containerDetailSource = readFileSync(path.join(root, 'src/pages/Warehouse/ContainerDetail/index.tsx'), 'utf8')

assert(
  mobileLayoutSource.includes('window.location.reload()'),
  '移动端手动刷新按钮应继续保留主动刷新能力',
)
assert(
  mobileLayoutSource.includes('<div className="mobile-content" key={location.pathname}>'),
  '移动端当前页面容器应按 pathname 设置 key，避免不同详情页复用同一组件实例',
)
assert(
  !adminLayoutSource.includes('openKeys={collapsed ? [] : openKeys}'),
  '桌面侧边栏折叠态不能受控传入空 openKeys，否则图标子菜单无法弹出',
)
assert(
  adminLayoutSource.includes('{...(!collapsed') &&
    adminLayoutSource.includes('openKeys,') &&
    adminLayoutSource.includes('onOpenChange: (keys) => setOpenKeys(keys as string[])'),
  '桌面侧边栏应只在展开态受控 openKeys，折叠态交给 AntD 管理弹出子菜单',
)
assert(
  errorBoundarySource.includes('window.location.reload()'),
  '错误恢复按钮应继续保留主动刷新能力',
)
assert(
  shopLayoutSource.includes("window.addEventListener('focus', refreshFocusedCart)") &&
    shopLayoutSource.includes("document.addEventListener('visibilitychange', refreshVisibleCart)"),
  '商城布局应在 Web 页面回到前台时刷新购物车，确保 PDA 加购后顶部购物车同步',
)
assert(
  shopLayoutSource.includes('getActiveStoreOrderCartSummary') &&
    shopLayoutSource.includes('const refreshCartSummary = useCallback') &&
    shopLayoutSource.includes('const refreshFullCart = useCallback') &&
    shopLayoutSource.includes('void refreshFullCart()') &&
    shopLayoutSource.includes('cartDrawerOpen ? refreshFullCart() : refreshCartSummary()') &&
    shopLayoutSource.includes('cartDrawerOpenRef.current ? refreshFullCart() : refreshCartSummary()'),
  '商城布局登录、切店和普通前台刷新应先拉购物车摘要，抽屉打开时前台刷新应保留全量明细',
)
assert(
  shopLayoutSource.includes('// 切换分店先清掉旧购物车') &&
    shopLayoutSource.includes('setCart(null)') &&
    shopLayoutSource.includes('cartDrawerOpenRef.current ? refreshFullCart() : refreshCartSummary()'),
  '商城布局切换分店时应先清空旧购物车，抽屉打开则直接加载新门店明细',
)
assert(
  shopLayoutSource.includes('selectedStoreCodeRef.current === storeCode'),
  '商城购物车刷新响应回来前应确认仍是当前门店，避免旧门店请求覆盖新门店购物车',
)
assert(
  shopCartDrawerSource.includes('const canSubmitCart = !isCartDetailLoading && cartItems.length > 0') &&
    shopCartDrawerSource.includes('disabled={!canSubmitCart || submitting}') &&
    shopCartDrawerSource.includes('disabled={!canSubmitCart || preorderBlocked}'),
  '购物车抽屉在 summary-only 或明细加载中时不能允许提交未展示明细的订单',
)
assert(
  !shopLayoutSource.includes('setInterval('),
  '商城购物车同步不应使用后台轮询，避免慢查询持续打到服务端',
)

const viewportHookMatch = containerDetailSource.match(/function useContainerDetailViewport\(\) \{[\s\S]*?\n\}/)
if (viewportHookMatch) {
  assert(
    !viewportHookMatch[0].includes('loadData'),
    '货柜明细横竖屏监听只能更新视口状态，不应触发 loadData',
  )
}
assert(
  /void loadHeader\(shouldShowInitialLoading\)[\s\S]{0,220}\}, \[active, containerGuid\]\)/.test(containerDetailSource),
  '货柜明细表头首次加载 effect 应只依赖 active 和 containerGuid，避免横竖屏切换重新加载',
)
assert(
  /void loadDetailChunk\(1, 'reset'\)[\s\S]{0,520}\}, \[active, activeLoadQueryKey\]\)/.test(containerDetailSource),
  '货柜明细远程分页加载 effect 应只依赖 active 和 activeLoadQueryKey，避免横竖屏切换重新加载',
)

console.log('noAutomaticReload.test: ok')
