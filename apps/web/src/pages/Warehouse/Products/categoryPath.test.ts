import {
  buildWarehouseCategoryLookup,
  getWarehouseProductCategoryTooltip,
} from './categoryPath'
import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import type { WarehouseProductListItem } from '../../../services/warehouseProductService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const categories: WarehouseCategoryNode[] = [
  {
    categoryGUID: 'cat-home',
    categoryName: 'Home',
    chineseName: '家居',
    isActive: true,
    children: [
      {
        categoryGUID: 'cat-bath',
        categoryName: 'Bath',
        chineseName: '浴室',
        isActive: true,
        children: [],
      },
      {
        categoryGUID: 'cat-laundry',
        categoryName: 'Laundry',
        chineseName: '洗衣',
        isActive: true,
        children: [],
      },
      {
        categoryGUID: 'cat-tools',
        categoryName: 'Tools',
        isActive: true,
        children: [],
      },
      {
        categoryGUID: 'cat-cn-only',
        categoryName: '',
        chineseName: '中文分类',
        isActive: true,
        children: [],
      },
    ],
  },
  {
    categoryGUID: 'cat-promo',
    categoryName: 'Promotion',
    chineseName: '促销',
    isActive: true,
    children: [
      {
        categoryGUID: 'cat-bath-promo',
        categoryName: 'Bath',
        chineseName: '浴室',
        isActive: true,
        children: [],
      },
    ],
  },
]

const lookup = buildWarehouseCategoryLookup(categories)

assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: 'Bath' } as WarehouseProductListItem,
    lookup,
  ),
  '家居 > 浴室；促销 > 浴室',
  '只有分类名称时应从分类树反查默认中文完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: 'Bath' } as WarehouseProductListItem,
    lookup,
    'en',
  ),
  'Home > Bath；Promotion > Bath',
  '只有分类名称时应支持反查英文完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryName: 'Bath',
      warehouseCategoryGUID: 'cat-bath',
    } as WarehouseProductListItem,
    lookup,
  ),
  '家居 > 浴室',
  '商品带分类 GUID 时应优先使用 GUID 对应中文完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryName: 'Bath',
      warehouseCategoryGUID: 'cat-bath',
      categoryPath: 'Backend / 后端 > Full / 完整 > Path / 路径',
    } as WarehouseProductListItem,
    lookup,
    'en',
  ),
  'Home > Bath',
  '商品带分类 GUID 时应优先使用分类树路径保持语言一致',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryPath: 'Backend / 后端 > Full / 完整 > Path / 路径',
    } as WarehouseProductListItem,
    lookup,
  ),
  '后端 > 完整 > 路径',
  '没有分类 GUID 和名称时中文界面应本地化后端完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryPath: 'Home / 家居 > Laundry / 洗衣',
    } as WarehouseProductListItem,
    lookup,
    'en',
  ),
  'Home > Laundry',
  '没有分类 GUID 和名称时英文界面应本地化后端完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: 'Tools', warehouseCategoryGUID: 'cat-tools' } as WarehouseProductListItem,
    lookup,
    'zh',
  ),
  '家居 > Tools',
  '中文界面遇到缺中文名节点时应回退英文名',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: '中文分类', warehouseCategoryGUID: 'cat-cn-only' } as WarehouseProductListItem,
    lookup,
    'en',
  ),
  'Home > 中文分类',
  '英文界面遇到缺英文名节点时应回退中文名',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: 'Unknown' } as WarehouseProductListItem,
    lookup,
  ),
  'Unknown',
  '分类树无法匹配时应退回分类名称',
)

console.log('warehouseProducts.categoryPath.test: ok')
