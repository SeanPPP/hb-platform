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
  'Home / 家居 > Bath / 浴室；Promotion / 促销 > Bath / 浴室',
  '只有分类名称时应从分类树反查所有完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryName: 'Bath',
      warehouseCategoryGUID: 'cat-bath',
    } as WarehouseProductListItem,
    lookup,
  ),
  'Home / 家居 > Bath / 浴室',
  '商品带分类 GUID 时应优先使用 GUID 对应完整路径',
)

assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryName: 'Bath',
      warehouseCategoryGUID: 'cat-bath',
      categoryPath: 'Backend / Full / Path',
    } as WarehouseProductListItem,
    lookup,
  ),
  'Backend / Full / Path',
  '后端完整路径字段应优先于前端计算路径',
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
