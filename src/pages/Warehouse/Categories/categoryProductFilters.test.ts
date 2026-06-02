import {
  ALL_PRODUCTS_FILTER_KEY,
  hasExecutedCategoryProductQuery,
  resolveCategoryProductFilterMode,
  UNCATEGORIZED_PRODUCTS_FILTER_KEY,
} from './categoryProductFilters'

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

console.log('categoryProductFilters.test: ok')
