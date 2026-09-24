import { readFileSync } from 'node:fs'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const source = readFileSync('src/pages/ExecutiveSalesIntelligence/CompactSalesBoard/index.tsx', 'utf8')
const styles = readFileSync('src/pages/ExecutiveSalesIntelligence/CompactSalesBoard/styles.module.css', 'utf8')

assert(source.includes("'/api/react/v1/dashboard/compact-sales-board'" ) === false, '页面必须经 salesDashboardService 调用接口')
assert(source.includes('getCompactSalesBoard'), '页面必须使用 Compact Sales Board 服务')
assert(source.includes('onKeyDown'), '可点击表格行必须支持键盘操作')
assert(source.includes("aria-label=\"清除筛选\""), '清除筛选按钮必须有可访问标签')
assert(source.includes("aria-label=\"强制刷新销售看板\""), '刷新图标按钮必须有可访问标签')
assert(source.includes('maxSalesDateRangeDays'), '日期范围必须有显式上限')
// 数据口径与销售明细对齐：快捷区间同一套规则（ISO 周、截止今天），上限共用 731 天，不能选未来日期。
assert(source.includes('quickDateSelection(range)'), '快捷区间必须复用销售明细的 quickDateSelection')
assert(source.includes('const maxSalesDateRangeDays = MAX_REPORT_DAYS'), '区间上限必须与销售明细共用 MAX_REPORT_DAYS')
assert(source.includes('disabledDate={isDisabledDate}'), '日期面板必须禁用未来日期与超长区间')
assert(source.includes('· 含提示'), '统计可读但带提示（对账未通过、历史缺口）时必须提示用户')
assert(source.includes('loadError'), '页面必须显示加载错误状态')
assert(source.includes('forceRefresh'), '页面必须向服务传递强制刷新状态')
assert(source.includes('alt={record.productName ?? record.itemNumber ?? record.productCode}'), '商品图片必须提供描述性替代文本')
assert(source.includes('setBoard(emptyBoard)'), '筛选请求失败时必须清空旧看板结果')
assert(source.includes("useKeepAliveContext"), 'KeepAlive 页面必须读取 active 上下文')
assert(source.includes('if (!active)'), '页面隐藏时不得继续加载')
assert(source.includes('boardRequestAbortRef.current?.abort()'), '新请求或页面隐藏时必须中止正在进行的请求')
assert(source.includes('setCacheState(\'fresh\')'), '强制刷新失败后不得保持刷新中状态')
assert(source.includes("type CacheState = 'cached' | 'fresh' | 'refreshing' | 'error'"), '缓存状态必须区分刷新成功与错误')
assert(source.includes("setCacheState('fresh')\n        // 中文注释"), '网络成功后必须从强制刷新状态切回最新查询')
assert(source.includes("setCacheState('error')"), '请求错误必须记录错误状态')
assert(source.includes('{loadError\n              ? <span className={styles.statusError}>'), '错误提示存在时不得同时显示误导性的统计新鲜度')

// 联动筛选：授权范围与选中项分开传，服务端据此让各栏不被自身选中项收窄。
assert(source.includes('branchCodes: managedStoreCodes'), 'branchCodes 只能传授权分店范围')
assert(source.includes('selectedBranchCode: filterState.branch?.code'), '选中的分店必须单独传给服务端')
assert(source.includes('selectedChinaSupplierCode: filterState.supplier?.code'), '选中的国内供应商必须单独传给服务端')
assert(source.includes('selectedProductCode: filterState.product?.code'), '选中的商品必须单独传给服务端')
assert(source.includes('resolveSupplierToggle('), '改选供应商时必须按归属解除不相容的商品选择')
assert(source.includes('shouldHandleEscape(event.target)'), 'Esc 清除筛选不得抢占输入框和弹层的 Esc')

// 局部加载：旧数据保留、行保持可点击，只有依赖已变化的栏显示加载条。
assert(source.includes('loadedKeys?.stores !== panelKeys.stores'), '分店栏只在其依赖变化时显示加载')
assert(source.includes('loadedKeys?.suppliers !== panelKeys.suppliers'), '供应商栏只在其依赖变化时显示加载')
assert(source.includes('loadedKeys?.products !== panelKeys.products'), '商品栏只在其依赖变化时显示加载')
assert(!source.includes('disabled={loading}'), '加载中不得禁用日期、分页等交互，新请求会中止旧请求')
assert(!source.includes("'aria-disabled'"), '加载中表格行保持可点击，不得整表禁用')

// 商品明细排序：受控排序并交给服务端对全部结果排序。
assert(source.includes('sortOrder: toAntdSortOrder(productSort, \'amount\')'), '商品金额列必须使用受控排序')
assert(source.includes('sortOrder: toAntdSortOrder(productSort, \'quantity\')'), '商品数量列必须使用受控排序')
assert(source.includes('sortOrder: toAntdSortOrder(productSort, \'unitPrice\')'), '商品单价列必须使用受控排序')
assert(source.includes('sortOrder: toAntdSortOrder(productSort, \'itemNumber\')'), '商品货号列必须使用受控排序')
assert(source.includes('sortField: filterState.productSort.field'), '商品排序字段必须传给服务端')
assert(source.includes('setProductSort(resolveProductSort(current?.columnKey, current?.order))\n    setPageIndex(1)'), '切换排序后必须回到第一页')
assert(source.includes('onCompositionStart'), '商品搜索必须等待中文输入法组词结束')

assert(styles.includes('.panelLoading .progress'), '局部加载必须有顶部进度条')
assert(styles.includes(':focus-visible'), '可点击行必须有可见焦点状态')
assert(styles.includes('.selectedRow td:first-child'), '选中行必须有维度色的左侧标识')
assert(styles.includes('@media (max-width: 720px)'), '页面必须覆盖窄视口布局')
assert(styles.includes('@media (prefers-reduced-motion: reduce)'), '进度条与骨架动画必须尊重减少动态效果设置')

console.log('compactSalesBoard source contract: ok')
