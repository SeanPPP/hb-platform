import {
  ALL_PRODUCTS_FILTER_KEY,
  buildFilterCategoryOptions,
  buildFilterCategoryTreeOptions,
  hasExecutedCategoryProductQuery,
  resolveCategoryProductFilterMode,
  UNCATEGORIZED_PRODUCTS_FILTER_KEY,
} from './categoryProductFilters'
import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const allMode = resolveCategoryProductFilterMode(ALL_PRODUCTS_FILTER_KEY)
assertEqual(allMode.type, 'all', '默认 ALL 选项应查询全部商品')
assertEqual(allMode.categoryGuid, undefined, 'ALL 查询不应携带分类 GUID')

const emptyMode = resolveCategoryProductFilterMode(undefined)
assertEqual(emptyMode.type, 'uncategorized', '清空分类筛选应查询分类为空商品')
assertEqual(emptyMode.categoryGuid, undefined, '空分类查询不应携带普通分类 GUID')

const uncategorizedMode = resolveCategoryProductFilterMode(UNCATEGORIZED_PRODUCTS_FILTER_KEY)
assertEqual(uncategorizedMode.type, 'uncategorized', '空分类哨兵值应查询分类为空商品')

const categoryMode = resolveCategoryProductFilterMode('cat-guid-1')
assertEqual(categoryMode.type, 'category', '普通分类 GUID 应查询该分类商品')
assertEqual(categoryMode.categoryGuid, 'cat-guid-1', '普通分类查询应保留分类 GUID')

const allKey: string = ALL_PRODUCTS_FILTER_KEY
const uncategorizedKey: string = UNCATEGORIZED_PRODUCTS_FILTER_KEY
assert(
  allKey !== uncategorizedKey,
  'ALL 和空分类必须使用不同哨兵值，避免清空下拉时误查全部商品',
)

assertEqual(hasExecutedCategoryProductQuery(null), false, 'null 应表示尚未执行商品查询')
assertEqual(hasExecutedCategoryProductQuery(allMode), true, '显式 ALL 模式应表示已经执行商品查询')
assertEqual(hasExecutedCategoryProductQuery(emptyMode), true, '显式空分类模式应表示已经执行商品查询')

const filterOptions = buildFilterCategoryOptions([], ((key: string, fallback?: string) => fallback ?? key) as never)
assert(
  filterOptions.some((option) => option.value === UNCATEGORIZED_PRODUCTS_FILTER_KEY && option.label === '未分类商品'),
  '分类筛选选项应包含未分类商品快捷选项',
)

const categoryTree: WarehouseCategoryNode[] = [
  {
    categoryGUID: 'cat-home',
    categoryName: 'Home',
    chineseName: '家居',
    isActive: true,
    children: [
      {
        categoryGUID: 'cat-laundry',
        categoryName: 'Laundry',
        chineseName: '洗衣',
        isActive: true,
        children: [],
      },
    ],
  },
]
const defaultLanguageOptions = buildFilterCategoryOptions(categoryTree, ((key: string, fallback?: string) => fallback ?? key) as never)
const chineseOptions = buildFilterCategoryOptions(categoryTree, ((key: string, fallback?: string) => fallback ?? key) as never, 'zh')
const englishOptions = buildFilterCategoryOptions(categoryTree, ((key: string, fallback?: string) => fallback ?? key) as never, 'en')
assertEqual(defaultLanguageOptions[2]?.label, 'Home / 家居', '不传语言时分类筛选应保留旧的中英组合显示，兼容分类管理页')
assertEqual(chineseOptions[2]?.label, '家居', '中文语言下分类筛选应只显示中文名称')
assertEqual(chineseOptions[3]?.label, '-- 洗衣', '中文语言下子分类筛选应只显示中文名称并保留层级前缀')
assertEqual(englishOptions[2]?.label, 'Home', '英文语言下分类筛选应只显示英文名称')
assertEqual(englishOptions[3]?.label, '-- Laundry', '英文语言下子分类筛选应只显示英文名称并保留层级前缀')

const categoryTreeOptions = buildFilterCategoryTreeOptions(
  categoryTree,
  ((key: string, fallback?: string) => fallback ?? key) as never,
  'zh',
)
assertEqual(categoryTreeOptions[0]?.value, ALL_PRODUCTS_FILTER_KEY, '树形分类筛选首项应保留全部商品快捷选项')
assertEqual(categoryTreeOptions[1]?.value, UNCATEGORIZED_PRODUCTS_FILTER_KEY, '树形分类筛选第二项应保留未分类快捷选项')
assertEqual(categoryTreeOptions[2]?.title, '家居', '树形分类父级节点应按当前语言显示分类名')
assertEqual(categoryTreeOptions[2]?.children?.[0]?.title, '洗衣', '树形分类子节点应使用真实 children，不再拼接层级前缀')
assert(
  !categoryTreeOptions[2]?.children?.[0]?.title.includes('--'),
  '树形分类子节点标题不应继续使用 -- 伪缩进',
)
assert(
  categoryTreeOptions[2]?.children?.[0]?.searchText.includes('home') &&
    categoryTreeOptions[2]?.children?.[0]?.searchText.includes('家居') &&
    categoryTreeOptions[2]?.children?.[0]?.searchText.includes('laundry') &&
    categoryTreeOptions[2]?.children?.[0]?.searchText.includes('洗衣'),
  '树形分类搜索字段应包含父级路径和当前节点中英文名称',
)

console.log('categoryProductFilters.test: ok')
