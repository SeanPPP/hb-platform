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
assert(source.includes("aria-label=\"清除筛选\""), '清除筛选图标按钮必须有可访问标签')
assert(source.includes("aria-label=\"强制刷新销售看板\""), '刷新图标按钮必须有可访问标签')
assert(source.includes('maxSalesDateRangeDays'), '日期范围必须有显式上限')
assert(source.includes('loadError'), '页面必须显示加载错误状态')
assert(source.includes('forceRefresh'), '页面必须向服务传递强制刷新状态')
assert(source.includes('alt={record.productName ?? record.itemNumber ?? record.productCode}'), '商品图片必须提供描述性替代文本')
assert(source.includes('setBoard(emptyBoard)'), '筛选请求失败时必须清空旧看板结果')
assert(source.includes("useKeepAliveContext"), 'KeepAlive 页面必须读取 active 上下文')
assert(source.includes('if (!active)'), '页面隐藏时不得继续加载')
assert(source.includes('boardRequestAbortRef.current?.abort()'), '页面隐藏时必须中止正在进行的请求')
assert(source.includes('setCacheState(\'fresh\')'), '强制刷新失败后不得保持刷新中状态')
assert(source.includes("type CacheState = 'cached' | 'fresh' | 'refreshing' | 'error'"), '缓存状态必须区分刷新成功与错误')
assert(source.includes("setCacheState('fresh')\n        // 中文注释"), '网络成功后必须从强制刷新状态切回最新查询')
assert(source.includes("setCacheState('error')"), '请求错误必须记录错误状态')
assert(source.includes('{!loadError && <Tag color={cacheState === \'cached\''), '错误提示存在时不得同时显示误导性的 freshness 标签')
assert(source.includes("'aria-disabled': disabled"), '加载中表格行必须暴露禁用状态')
assert(source.includes('tabIndex: disabled ? -1 : 0'), '加载中表格行必须从键盘焦点序列移除')
assert(source.includes('...(!disabled ? {'), '加载中表格行不得绑定点击或键盘选择处理')
assert(source.includes('styles.disabledRow'), '加载中表格行必须移除可点击视觉样式')
assert(styles.includes('.disabledRow'), '加载中表格行必须有禁用样式')
assert(styles.includes(':focus-visible'), '可点击行必须有可见焦点状态')
assert(styles.includes('@media (max-width: 720px)'), '页面必须覆盖窄视口布局')

console.log('compactSalesBoard source contract: ok')
