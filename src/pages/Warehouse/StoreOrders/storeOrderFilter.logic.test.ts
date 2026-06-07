import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: boolean, message: string) {
  if (!condition) {
    throw new Error(message)
  }
}

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/index.tsx')
const source = readFileSync(pageFile, 'utf8')

assert(
  source.includes('const storeFilterOptions = useMemo('),
  '分店筛选选项应先构建稳定排序结果',
)
assert(
  source.includes("localeCompare(right.name || '', 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })"),
  '分店筛选选项应按分店名称优先排序',
)
assert(
  source.includes("label: `${item.code} - ${item.name}`"),
  '分店筛选 label 应保留 code - name 以支持代码和名称搜索',
)
assert(source.includes('showSearch'), '分店筛选 Select 应支持输入搜索')
assert(
  source.includes('optionFilterProp="label"'),
  '分店筛选 Select 应基于 label 过滤',
)
assert(
  source.includes('filterOption={filterStoreOption}'),
  '分店筛选 Select 应使用关键字过滤函数',
)

console.log('storeOrderFilter.logic.test: ok')
