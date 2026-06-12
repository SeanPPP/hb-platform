import { readFileSync } from 'node:fs'
import type { ContainerDetail } from '../../../types/container'
import type { DetectionResult } from '../../../services/warehouseProductService'
import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import {
  buildWarehouseCategoryLookup,
  getWarehouseProductCategoryTooltip,
} from '../Products/categoryPath'
import {
  CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY,
  CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY,
  applyContainerDetailEnglishNameUpdates,
  applyContainerDetailWarehouseStatusByProductCodes,
  applyContainerDetailCategoryFilter,
  applyContainerDetailLoadedTextFilters,
  applyContainerDetailColumnState,
  buildContainerDetailQuery,
  buildContainerDetailClearEnglishNameUpdates,
  buildContainerDetailDetectionItems,
  buildContainerDetailEnglishNameUpdates,
  buildContainerDetailMatchedDomesticDataUpdates,
  buildContainerDetailMatchStatusUpdates,
  mergeContainerDetailColumnOrder,
  isContainerDetailColumnOrderCustomized,
  buildContainerDetailSaveFailureKeys,
  moveContainerDetailColumnOrder,
  getContainerDetailRemoteQueryResetState,
  findContainerDetailRowsMissingCreateProductRetailPrice,
  findContainerDetailRowsMissingProductName,
  buildContainerDetailTagStats,
  buildContainerDetailFloatRateUpdates,
  buildContainerDetailExportRow,
  buildContainerDetailExportRows,
  buildContainerDetailHqPushSelection,
  buildContainerDetailTranslationUpdates,
  calculateContainerDetailImportPrice,
  calculateContainerSetCodePurchasePrice,
  calculateContainerDetailTransportCost,
  countContainerDetailInvalidTranslationResults,
  extractPushToHqErrorResult,
  mergeContainerDetailLoadedItems,
  getContainerDetailBatchCategoryProductCodes,
  getContainerDetailCategoryGuid,
  getContainerDetailCategoryName,
  getContainerDetailCategoryPath,
  getContainerDetailCategoryTooltipRecord,
  getContainerDetailEnglishName,
  getContainerDetailMatchType,
  getContainerDetailProductCode,
  getContainerDetailCreateProductRowLabel,
  getContainerDetailProductType,
  getContainerDetailProductTypeFilterKey,
  getContainerDetailOemPriceSource,
  getContainerDetailTranslationSource,
  getContainerDetailWarehouseActionFailureMessage,
  getContainerDetailExportColumns,
  getNextContainerDetailEditableCell,
  isContainerDetailSortField,
  matchesContainerDetailSelectedTags,
  matchesContainerDetailTagFilter,
  normalizeContainerDetailPushToHqPayload,
  resolveContainerDetailOemPrice,
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
  type ContainerDetailTableColumnKey,
  type ContainerDetailColumnFilters,
  type ContainerDetailExportColumnKey,
  type ContainerDetailSortField,
  type ContainerDetailSortState,
} from './containerDetailLogic'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const rows: ContainerDetail[] = [
  {
    id: 1,
    hguid: 'detail-1',
    商品名称: '明细大草莓',
    英文名称: 'Detail Strawberry',
    商品信息: {
      商品名称: '商品信息大草莓',
      英文名称: 'Product Strawberry',
    },
  },
  {
    id: 2,
    hguid: 'detail-2',
    商品信息: {
      商品名称: 'TPR鲨鱼',
      英文名称: 'TPR Shark',
    },
  },
]

assertDeepEqual(
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
  [
    'index',
    'itemNumber',
    'productName',
    'englishName',
    'containerPieces',
    'containerQuantity',
    'unitVolume',
    'totalVolume',
    'middlePackQuantity',
    'domesticPrice',
    'oemPrice',
  ],
  '货柜明细默认导出列应为用户指定的 Excel 核对模板',
)

const customExportColumnKeys: ContainerDetailExportColumnKey[] = ['oemPrice', 'itemNumber', 'containerQuantity']
assertDeepEqual(
  getContainerDetailExportColumns(customExportColumnKeys).map((column) => column.key),
  ['oemPrice', 'itemNumber', 'containerQuantity'],
  '货柜明细自定义导出列应按用户选择顺序输出',
)

const exportRow = buildContainerDetailExportRow({
  id: 101,
  hguid: 'export-101',
  商品名称: '明细名称',
  英文名称: 'Detail Name',
  商品类型: '普通商品',
  是否新商品: true,
  matchType: 'productCode',
  装柜件数: 3,
  装柜数量: 720,
  国内价格: 2.3,
  调整浮率: 1.2,
  运输成本: 0.08,
  warehouseImportPrice: 0.52,
  进口价格: 0.67,
  贴牌价格: 3.5,
  warehouseOEMPrice: 4.8,
  单件体积: 0.1188,
  合计装柜体积: 0.3564,
  warehouseIsActive: true,
  备注: '优先上架',
  商品信息: {
    货号: 'HB291-005',
    条形码: '9525812910005',
    商品名称: '商品信息名称',
    英文名称: 'Product Info Name',
    商品类型: '套装商品',
    零售价格: 9.99,
  },
})
assertDeepEqual(
  {
    itemNumber: exportRow.itemNumber,
    productName: exportRow.productName,
    englishName: exportRow.englishName,
    containerPieces: exportRow.containerPieces,
    containerQuantity: exportRow.containerQuantity,
    unitVolume: exportRow.unitVolume,
    totalVolume: exportRow.totalVolume,
    oemPrice: exportRow.oemPrice,
  },
  {
    itemNumber: 'HB291-005',
    productName: '明细名称',
    englishName: 'Detail Name',
    containerPieces: 3,
    containerQuantity: 720,
    unitVolume: 0.1188,
    totalVolume: 0.3564,
    oemPrice: 4.8,
  },
  '货柜明细导出行应按 Excel 模板读取页面展示字段和体积字段，且贴牌价格应优先映射仓库商品贴牌价',
)

assertEqual(
  resolveContainerDetailOemPrice({ id: 103, hguid: 'oem-warehouse', 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }),
  6.6,
  '有效贴牌价应优先使用小写 warehouseOEMPrice',
)
assertEqual(
  resolveContainerDetailOemPrice({ id: 104, hguid: 'oem-warehouse-big', 贴牌价格: 2.2, WarehouseOEMPrice: 7.7 }),
  7.7,
  '有效贴牌价应兼容后端大写 WarehouseOEMPrice',
)
assertEqual(
  resolveContainerDetailOemPrice({ id: 105, hguid: 'oem-fallback', 贴牌价格: 2.2, warehouseOEMPrice: 0 }),
  2.2,
  '仓库商品贴牌价为空或不大于 0 时应兜底明细贴牌价',
)
assertEqual(
  getContainerDetailOemPriceSource({ id: 106, hguid: 'oem-source-warehouse', 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }),
  'warehouse',
  '贴牌价来源应识别有效仓库商品贴牌价',
)
assertEqual(
  getContainerDetailOemPriceSource({ id: 107, hguid: 'oem-source-detail', 贴牌价格: 2.2, warehouseOEMPrice: 0 }),
  'detail',
  '贴牌价来源应识别明细兜底贴牌价',
)
assertEqual(
  getContainerDetailOemPriceSource({ id: 108, hguid: 'oem-source-none' }),
  'none',
  '贴牌价来源应识别无价格状态',
)

assertDeepEqual(
  buildContainerDetailExportRows([
    {
      id: 102,
      hguid: 'export-empty',
      warehouseIsActive: undefined,
    },
  ]),
  [
    {
      index: 1,
      itemNumber: '',
      productName: '',
      englishName: '',
      containerPieces: 0,
      containerQuantity: 0,
      unitVolume: 0,
      totalVolume: 0,
      middlePackQuantity: 0,
      domesticPrice: 0,
      oemPrice: 0,
    },
  ],
  '货柜明细导出行缺失字段时应使用稳定空值或 0，避免 Excel 导出报错',
)

assertDeepEqual(
  buildContainerDetailExportRows([
    {
      id: 105,
      hguid: 'export-volume-detail-first',
      装柜件数: 2,
      单件体积: 0.25,
      商品信息: { 单件体积: 0.5 },
    },
    {
      id: 106,
      hguid: 'export-volume-fallback',
      装柜件数: 3,
      商品信息: { 单件体积: 0.4 },
    },
  ]).map((row) => ({
    index: row.index,
    unitVolume: row.unitVolume,
    totalVolume: row.totalVolume,
  })),
  [
    { index: 1, unitVolume: 0.25, totalVolume: 0.5 },
    { index: 2, unitVolume: 0.4, totalVolume: 1.2000000000000002 },
  ],
  '货柜明细导出体积应优先读取明细单件体积，并在合计体积缺失时用件数乘单件体积兜底',
)

assertEqual(
  getContainerDetailEnglishName(rows[0]),
  'Detail Strawberry',
  '英文名称展示应优先读取货柜明细字段',
)
assertEqual(
  getContainerDetailProductType({
    id: 103,
    hguid: 'type-domestic-first',
    商品类型: '普通商品',
    商品信息: { 商品类型: '套装商品' },
  }),
  '套装商品',
  '商品类型应优先读取国内商品表关联信息',
)
assertEqual(
  getContainerDetailProductType({
    id: 104,
    hguid: 'type-detail-fallback',
    商品类型: '套装子商品',
  }),
  '套装子商品',
  '国内商品表类型缺失时应回退货柜明细商品类型',
)

const categoryRows: ContainerDetail[] = [
  {
    id: 151,
    hguid: 'category-row-direct',
    商品编码: ' P001 ',
    categoryName: 'Bath',
    categoryPath: 'Home / 家居 > Bath / 浴室',
    warehouseCategoryGUID: 'cat-bath',
  },
  {
    id: 152,
    hguid: 'category-row-info',
    商品信息: {
      商品编码: 'P002',
      ProductCategoryName: 'Kitchen',
      CategoryFullPath: 'Home / 家居 > Kitchen / 厨房',
      ProductCategoryGUID: 'cat-kitchen',
    },
  },
  {
    id: 153,
    hguid: 'category-row-empty',
    商品编码: 'P003',
  },
  {
    id: 154,
    hguid: 'category-row-duplicate-code',
    商品编码: 'P001',
    WarehouseCategoryGUID: 'cat-bath',
    CategoryName: 'Bath',
  },
  {
    id: 157,
    hguid: 'category-row-grandchild',
    商品编码: 'P004',
    商品名称: '浴巾套装',
    warehouseCategoryGUID: 'cat-towels',
    categoryName: 'Towels',
    商品信息: { 货号: 'HB-P004' },
  },
  {
    id: 158,
    hguid: 'category-row-great-grandchild',
    商品编码: 'P005',
    warehouseCategoryGUID: 'cat-small-towels',
    categoryName: 'Small Towels',
  },
  {
    id: 159,
    hguid: 'category-row-sibling',
    商品编码: 'P006',
    warehouseCategoryGUID: 'cat-kitchen',
    categoryName: 'Kitchen',
  },
  {
    id: 160,
    hguid: 'category-row-name-only-grandchild',
    商品编码: 'P007',
    categoryName: 'Small Towels',
  },
]
const categoryTree: WarehouseCategoryNode[] = [
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
        children: [
          {
            categoryGUID: 'cat-towels',
            categoryName: 'Towels',
            chineseName: '毛巾',
            isActive: true,
            children: [
              {
                categoryGUID: 'cat-small-towels',
                categoryName: 'Small Towels',
                chineseName: '小毛巾',
                isActive: true,
                children: [],
              },
            ],
          },
        ],
      },
      {
        categoryGUID: 'cat-kitchen',
        categoryName: 'Kitchen',
        chineseName: '厨房',
        isActive: true,
        children: [],
      },
    ],
  },
]
const categoryLookup = buildWarehouseCategoryLookup(categoryTree)
assertEqual(getContainerDetailCategoryName(categoryRows[0]), 'Bath', '分类名称应优先读取明细行字段')
assertEqual(getContainerDetailCategoryName(categoryRows[1]), 'Kitchen', '分类名称应兼容商品信息 PascalCase 字段')
assertEqual(getContainerDetailCategoryPath(categoryRows[1]), 'Home / 家居 > Kitchen / 厨房', '完整分类路径应兼容商品信息 CategoryFullPath 字段')
assertEqual(getContainerDetailCategoryGuid(categoryRows[1]), 'cat-kitchen', '分类 GUID 应兼容商品信息 ProductCategoryGUID 字段')
assertEqual(
  getWarehouseProductCategoryTooltip(getContainerDetailCategoryTooltipRecord({ id: 155, hguid: 'category-name-only', categoryName: 'Bath' }), buildWarehouseCategoryLookup(categoryTree), 'zh'),
  '家居 > 浴室',
  '只有分类名称时应通过分类树反查当前语言 Tooltip 完整路径',
)
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY).map((row) => row.hguid),
  [
    'category-row-direct',
    'category-row-info',
    'category-row-empty',
    'category-row-duplicate-code',
    'category-row-grandchild',
    'category-row-great-grandchild',
    'category-row-sibling',
    'category-row-name-only-grandchild',
  ],
  '全部分类过滤应保留当前已加载行',
)
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY).map((row) => row.hguid),
  ['category-row-empty'],
  '未分类过滤应只保留缺少分类名称和分类 GUID 的当前已加载行',
)
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, 'cat-home', categoryLookup).map((row) => row.hguid),
  [
    'category-row-direct',
    'category-row-info',
    'category-row-duplicate-code',
    'category-row-grandchild',
    'category-row-great-grandchild',
    'category-row-sibling',
    'category-row-name-only-grandchild',
  ],
  '选择根分类时应命中自身和所有层级的子孙分类商品',
)
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, 'cat-bath', categoryLookup).map((row) => row.hguid),
  ['category-row-direct', 'category-row-duplicate-code', 'category-row-grandchild', 'category-row-great-grandchild', 'category-row-name-only-grandchild'],
  '选择父分类时应命中自身、子类、孙子类和只有分类名的后代商品',
)
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, 'cat-towels', categoryLookup).map((row) => row.hguid),
  ['category-row-grandchild', 'category-row-great-grandchild', 'category-row-name-only-grandchild'],
  '选择子分类时应命中自身和所有更深层后代商品',
)
assertDeepEqual(
  applyContainerDetailLoadedTextFilters(categoryRows, ' p004 ', {}).map((row) => row.hguid),
  ['category-row-grandchild'],
  '顶部货号关键字应只按当前已加载行的货号做前端包含过滤',
)
assertDeepEqual(
  applyContainerDetailLoadedTextFilters(categoryRows, '', { itemNumber: 'p00', productName: '浴巾' }).map((row) => row.hguid),
  ['category-row-grandchild'],
  '列头文字搜索应在前端同时匹配货号和商品名称等文本列',
)
assertDeepEqual(
  getContainerDetailBatchCategoryProductCodes(categoryRows),
  { productCodes: ['P001', 'P002', 'P003', 'P004', 'P005', 'P006', 'P007'], skippedMissingCodeCount: 0 },
  '批量分类应提取 trim 后商品编码并去重',
)
assertDeepEqual(
  getContainerDetailBatchCategoryProductCodes([{ id: 156, hguid: 'missing-code' }, ...categoryRows.slice(0, 1)]),
  { productCodes: ['P001'], skippedMissingCodeCount: 1 },
  '批量分类应跳过缺商品编码行并统计数量',
)
assertDeepEqual(
  buildContainerDetailSaveFailureKeys('row-1', { 商品名称: '皮带' }),
  ['row-1:商品名称'],
  '明细保存失败 key 应区分商品名称字段，避免被同一行其它保存清除',
)
assertDeepEqual(
  buildContainerDetailSaveFailureKeys('row-1', { 备注: '已确认' }),
  ['row-1:备注'],
  '同一行备注保存应使用独立失败 key，不能清除商品名称保存失败状态',
)

assertDeepEqual(
  buildContainerDetailTranslationUpdates(rows, {
    明细大草莓: 'Large Strawberry',
    TPR鲨鱼: 'TPR Shark Toy',
  }),
  [
    { hguid: 'detail-1', 英文名称: 'Large Strawberry' },
    { hguid: 'detail-2', 英文名称: 'TPR Shark Toy' },
  ],
  '批量翻译应用明细中文名优先生成可保存的英文名称更新',
)

const englishNameChineseRows: ContainerDetail[] = [
  {
    id: 3,
    hguid: 'detail-3',
    商品名称: '商品中文名不应优先',
    英文名称: '草莓玩具',
  },
]

assertEqual(
  getContainerDetailTranslationSource(englishNameChineseRows[0]),
  '草莓玩具',
  '英文名称仍含中文时应优先作为翻译源',
)

assertDeepEqual(
  buildContainerDetailTranslationUpdates(englishNameChineseRows, {
    草莓玩具: 'Strawberry Toy',
    商品中文名不应优先: 'Wrong Source',
  }),
  [
    { hguid: 'detail-3', 英文名称: 'Strawberry Toy' },
  ],
  '英文名称为中文时批量翻译应翻译英文名称字段本身',
)

assertDeepEqual(
  buildContainerDetailTranslationUpdates(rows, {
    明细大草莓: 'Large 草莓',
    TPR鲨鱼: 'TPR Shark Toy',
  }),
  [
    { hguid: 'detail-2', 英文名称: 'TPR Shark Toy' },
  ],
  '批量翻译应跳过仍包含中文的英文名称结果',
)

assertEqual(
  countContainerDetailInvalidTranslationResults(rows, {
    明细大草莓: 'Large 草莓',
    TPR鲨鱼: 'TPR Shark Toy',
  }),
  1,
  '批量翻译应统计仍包含中文的跳过结果',
)

const updatedRows = applyContainerDetailEnglishNameUpdates(rows, [
  { hguid: 'detail-1', 英文名称: 'Large Strawberry' },
])

assertEqual(updatedRows[0].英文名称, 'Large Strawberry', '本地行应写入明细级英文名称')
assertEqual(updatedRows[0].商品信息?.英文名称, 'Large Strawberry', '本地行应同步商品信息英文名称用于展示兜底')
assertEqual(updatedRows[1].商品信息?.英文名称, 'TPR Shark', '未命中的行应保持原值')

assertDeepEqual(
  buildContainerDetailEnglishNameUpdates(rows, '  Unified English Name  '),
  [
    { hguid: 'detail-1', 英文名称: 'Unified English Name' },
    { hguid: 'detail-2', 英文名称: 'Unified English Name' },
  ],
  '批量修改英文名称应为所有有效明细生成统一且去空格的英文名称',
)

assertDeepEqual(
  buildContainerDetailEnglishNameUpdates(rows, 'Unified 草莓 Name'),
  [],
  '手动批量修改英文名称应拒绝仍包含中文的输入',
)

assertDeepEqual(
  buildContainerDetailClearEnglishNameUpdates(rows),
  [
    { hguid: 'detail-1', ClearEnglishName: true },
    { hguid: 'detail-2', ClearEnglishName: true },
  ],
  '清除英文名称应为所有有效明细生成明确清空标记',
)

const clearedRows = applyContainerDetailEnglishNameUpdates(rows, [
  { hguid: 'detail-1', 英文名称: undefined },
])

assertEqual(clearedRows[0].英文名称, undefined, '清除后本地行明细级英文名称应为空')
assertEqual(clearedRows[0].商品信息?.英文名称, undefined, '清除后本地行商品信息英文名称应为空')

const editableRowKeys = ['row-1', 'row-2', 'row-3']
const editableColumnKeys = ['englishName', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark']

assertDeepEqual(
  getNextContainerDetailEditableCell('row-2', 'floatRate', editableRowKeys, editableColumnKeys, 'up'),
  { rowKey: 'row-1', columnKey: 'floatRate' },
  '方向键上应移动到上一行同一编辑列',
)
assertDeepEqual(
  getNextContainerDetailEditableCell('row-2', 'floatRate', editableRowKeys, editableColumnKeys, 'down'),
  { rowKey: 'row-3', columnKey: 'floatRate' },
  '方向键下应移动到下一行同一编辑列',
)
assertDeepEqual(
  getNextContainerDetailEditableCell('row-2', 'floatRate', editableRowKeys, editableColumnKeys, 'left'),
  { rowKey: 'row-2', columnKey: 'middlePackQuantity' },
  '方向键左应移动到同一行前一个编辑列',
)
assertDeepEqual(
  getNextContainerDetailEditableCell('row-2', 'floatRate', editableRowKeys, editableColumnKeys, 'right'),
  { rowKey: 'row-2', columnKey: 'importPrice' },
  '方向键右应移动到同一行后一个编辑列',
)
assertEqual(
  getNextContainerDetailEditableCell('row-1', 'englishName', editableRowKeys, editableColumnKeys, 'up'),
  null,
  '第一行按上不应移动',
)
assertEqual(
  getNextContainerDetailEditableCell('row-3', 'remark', editableRowKeys, editableColumnKeys, 'down'),
  null,
  '最后一行按下不应移动',
)
assertEqual(
  getNextContainerDetailEditableCell('row-2', 'englishName', editableRowKeys, editableColumnKeys, 'left'),
  null,
  '首个编辑列按左不应移动',
)
assertEqual(
  getNextContainerDetailEditableCell('row-2', 'remark', editableRowKeys, editableColumnKeys, 'right'),
  null,
  '最后一个编辑列按右不应移动',
)
assertEqual(
  getNextContainerDetailEditableCell('missing-row', 'floatRate', editableRowKeys, editableColumnKeys, 'down'),
  null,
  '当前行不存在时不应移动',
)
assertEqual(
  getNextContainerDetailEditableCell('row-2', 'missing-column', editableRowKeys, editableColumnKeys, 'right'),
  null,
  '当前列不存在时不应移动',
)

const tagRows: ContainerDetail[] = [
  { id: 31, hguid: 'tag-31', 是否新商品: true, 贴牌价格: 0, 进口价格: 1, warehouseIsActive: true },
  { id: 32, hguid: 'tag-32', 是否新商品: true, 贴牌价格: 0, warehouseOEMPrice: 2, 进口价格: 0, warehouseIsActive: false, 商品信息: { 商品类型: '套装商品' } },
  { id: 33, hguid: 'tag-33', 是否新商品: false, 贴牌价格: 3, 进口价格: 4, warehouseIsActive: true, 商品信息: { 商品类型: '多码商品' } },
  { id: 34, hguid: 'tag-34', 是否新商品: false, 贴牌价格: 0, 进口价格: undefined, warehouseIsActive: undefined, 商品类型: '套装子商品' },
]

assertDeepEqual(
  buildContainerDetailTagStats(tagRows),
  {
    all: 4,
    new: 2,
    existing: 2,
    noOemPrice: 1,
    abnormalImport: 2,
    active: 2,
    inactive: 2,
    normal: 1,
    set: 1,
    multi: 1,
    setChild: 1,
  },
  '统计栏应按当前基础结果统计全部、新商品、已有商品、缺贴牌价、进口价异常、上下架和商品类型数量',
)
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'new'), true, '新商品 tag 应匹配是否新商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'new'), false, '新商品 tag 不应匹配已有商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'noOemPrice'), true, '缺贴牌价只统计新商品且有效贴牌价为空或不大于 0 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[1], 'noOemPrice'), false, '仓库商品贴牌价有效时不应进入缺贴牌价 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'noOemPrice'), false, '已有商品缺贴牌价不进入缺贴牌价 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[1], 'abnormalImport'), true, '进口价为 0 应进入进口价异常 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'all'), true, '全部 tag 应匹配所有行')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'active'), true, '上架 tag 应匹配 warehouseIsActive 为 true 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'inactive'), true, '下架 tag 应匹配 warehouseIsActive 非 true 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[1], 'set'), true, '套装商品统计 tag 应匹配国内商品表类型')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'multi'), true, '多码商品统计 tag 应匹配多码类型')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'setChild'), true, '套装子商品统计 tag 应匹配明细兜底类型')
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], []), true, '未选择 tag 时应显示全部行')
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ['new', 'inactive']), true, '新商品与下架属于不同分组，应同时满足')
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], ['new', 'inactive']), false, '新商品但已上架时不应命中新商品加下架组合')
assertEqual(matchesContainerDetailSelectedTags(tagRows[2], ['new', 'existing']), true, '新商品和已有商品同组多选应按 OR 匹配')
assertEqual(matchesContainerDetailSelectedTags(tagRows[2], ['set', 'multi']), true, '商品类型统计 tag 同组多选应按 OR 匹配')
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ['multi', 'inactive']), false, '商品类型未命中时即使上下架命中也应被过滤')
assertEqual(matchesContainerDetailSelectedTags(tagRows[3], ['noOemPrice', 'abnormalImport']), true, '异常类 tag 同组多选应按 OR 匹配')
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ['noOemPrice', 'abnormalImport', 'inactive']), true, '异常类 OR 后应继续与上下架分组 AND')
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], ['noOemPrice', 'abnormalImport', 'inactive']), false, '命中异常类但未命中下架时应被过滤')

const columnStateRows: ContainerDetail[] = [
  {
    id: 201,
    hguid: 'column-201',
    商品名称: '明细塑料杯钩',
    英文名称: 'Plastic Cup Hook',
    商品类型: '普通商品',
    是否新商品: false,
    matchType: 'productCode',
    装柜件数: 8,
    中包数: 12,
    装柜数量: 1152,
    国内价格: 12,
    调整浮率: 1.2,
    运输成本: 0.35,
    进口价格: 3.22,
    贴牌价格: 3.88,
    warehouseOEMPrice: 4.5,
    备注: '第一行备注',
    warehouseIsActive: true,
    商品信息: { 货号: '8101733', 条形码: '8052533117337', 商品名称: '商品塑料杯钩' },
  },
  {
    id: 202,
    hguid: 'column-202',
    商品名称: '魔方珠子',
    商品类型: '套装商品',
    是否新商品: true,
    matchType: 'unmatched',
    装柜件数: 30,
    中包数: 0,
    装柜数量: 4320,
    国内价格: 0,
    调整浮率: 1.1,
    运输成本: undefined,
    进口价格: 0,
    贴牌价格: 0,
    备注: '需要补价格',
    warehouseIsActive: false,
    商品信息: { 货号: 'HB386-013', 条形码: '9527938600047', 英文名称: 'Cube Beads', 商品类型: '普通商品' },
  },
  {
    id: 203,
    hguid: 'column-203',
    商品类型: '套装子商品',
    是否新商品: false,
    matchType: 'supplierItem',
    装柜件数: 2,
    中包数: undefined,
    装柜数量: 480,
    国内价格: 5.5,
    调整浮率: undefined,
    运输成本: 0.12,
    进口价格: 1.45,
    贴牌价格: 2.01,
    warehouseIsActive: undefined,
    商品信息: { 货号: '8104032', 条形码: '8052533140328', 商品名称: '三角支架', 英文名称: 'Triangle Bracket' },
  },
]

function columnState(filters: ContainerDetailColumnFilters, sortState?: ContainerDetailSortState) {
  return applyContainerDetailColumnState(columnStateRows, filters, sortState).map((row) => row.hguid)
}

assertDeepEqual(columnState({ itemNumber: ' hb386 ' }), ['column-202'], '货号列头文本过滤应忽略大小写并包含匹配')
assertDeepEqual(columnState({ barcode: '3140328' }), ['column-203'], '条码列头文本过滤应支持局部匹配')
assertDeepEqual(columnState({ productName: '塑料杯' }), ['column-201'], '商品名称列头过滤应优先读取明细名称并支持中文包含匹配')
assertDeepEqual(columnState({ englishName: 'triangle' }), ['column-203'], '英文名称列头过滤应读取商品信息兜底并忽略大小写')
assertDeepEqual(columnState({ remark: '补价格' }), ['column-202'], '备注列头过滤应支持文本包含匹配')
assertDeepEqual(columnState({ productTypes: ['set', 'setChild'] }), ['column-203'], '商品类型列头过滤应优先读取国内商品表类型并支持多选枚举')
assertDeepEqual(columnState({ productTypes: ['normal'] }), ['column-201', 'column-202'], '国内商品表类型覆盖明细旧类型时应按国内商品表类型过滤')
assertEqual(getContainerDetailProductTypeFilterKey({ id: 204, hguid: 'column-204', 商品信息: { 商品类型: '多码商品' } }), 'multi', '商品类型过滤键应支持多码商品')
assertDeepEqual(columnState({ newProductStates: ['new'] }), ['column-202'], '新商品列头过滤应支持筛出新商品')
assertDeepEqual(columnState({ matchTypes: ['supplierItem'] }), ['column-203'], '匹配方式列头过滤应支持供应商编码加货号匹配')
assertDeepEqual(columnState({ warehouseStatus: ['inactive'] }), ['column-202', 'column-203'], '仓库状态列头过滤应把非 true 视为下架')
assertDeepEqual(columnState({ middlePackQuantity: { min: 1, max: 20 } }), ['column-201'], '中包数列头范围过滤应读取仓库商品最小订货量')
assertDeepEqual(columnState({ containerQuantity: { min: 500, max: 2000 } }), ['column-201'], '装柜数量列头范围过滤应同时支持最小值和最大值')
assertDeepEqual(columnState({ domesticPrice: { min: 0, max: 0 } }), ['column-202'], '数字列头范围过滤应正确匹配 0 值')
assertDeepEqual(columnState({ transportCost: { min: 0 } }), ['column-201', 'column-203'], '数字列头范围过滤应排除空值')
assertDeepEqual(columnState({ oemPrice: { min: 4, max: 5 } }), ['column-201'], '贴牌价列头范围过滤应优先读取仓库商品贴牌价')
assertDeepEqual(columnState({ oemPrice: { min: 2 } }, { field: 'containerPieces', order: 'ascend' }), ['column-203', 'column-201'], '列头过滤后排序应只作用于过滤后的可见行')
assertDeepEqual(columnState({}, { field: 'itemNumber', order: 'ascend' }), ['column-201', 'column-203', 'column-202'], '货号排序应按文本升序且保持稳定输出')
assertDeepEqual(
  applyContainerDetailColumnState([
    { id: 211, hguid: 'sort-211', 商品信息: { 货号: 'HB2' } },
    { id: 212, hguid: 'sort-212', 商品信息: { 货号: '' } },
    { id: 213, hguid: 'sort-213', 商品信息: { 货号: 'HB10' } },
    { id: 214, hguid: 'sort-214', 商品信息: { 货号: 'HB2' } },
  ], {}, { field: 'itemNumber', order: 'ascend' }).map((row) => row.hguid),
  ['sort-211', 'sort-214', 'sort-213', 'sort-212'],
  '货号升序应按自然排序、空货号排最后，并保持相同货号原始顺序',
)
assertDeepEqual(columnState({}, { field: 'transportCost', order: 'ascend' }), ['column-203', 'column-201', 'column-202'], '数字排序应把空值排在最后')
assertDeepEqual(columnState({}, { field: 'warehouseStatus', order: 'descend' }), ['column-201', 'column-202', 'column-203'], '仓库状态排序应支持上架优先且同值保持原始顺序')
assertDeepEqual(columnState({}, { field: 'matchType', order: 'ascend' }), ['column-201', 'column-203', 'column-202'], '匹配方式排序应按商品编码、供应商货号、未匹配稳定排序')

assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: 'CONTAINER-QUERY',
    filters: {
      itemNumber: ' HB308 ',
      barcode: '',
      productName: ' 梳子 ',
      englishName: ' Grooming ',
      remark: ' 需确认 ',
      productTypes: ['normal', 'set'],
      newProductStates: ['new'],
      matchTypes: ['productCode', 'supplierItem'],
      warehouseStatus: ['active'],
      containerPieces: { min: 1, max: 8 },
      middlePackQuantity: { min: 1, max: 24 },
      containerQuantity: { min: 0, max: 1200 },
      domesticPrice: { min: 2.5 },
      floatRate: { max: 1.3 },
      transportCost: { min: 0 },
      warehouseImportPrice: { max: 9.99 },
      importPrice: { min: 1.11, max: 3.33 },
      oemPrice: { min: 4.44, max: 5.55 },
    },
    selectedTags: ['all', 'new', 'inactive'],
    sortState: { field: 'itemNumber', order: 'ascend' },
    pageNumber: 3,
    pageSize: 80,
  }),
  {
    containerGuid: 'CONTAINER-QUERY',
    pageNumber: 3,
    pageSize: 80,
    itemNumber: 'HB308',
    productName: '梳子',
    englishName: 'Grooming',
    remark: '需确认',
    productTypes: ['normal', 'set'],
    newProductStates: ['new'],
    matchTypes: ['productCode', 'supplierItem'],
    warehouseStatus: ['active'],
    containerPiecesMin: 1,
    containerPiecesMax: 8,
    middlePackQuantityMin: 1,
    middlePackQuantityMax: 24,
    containerQuantityMin: 0,
    containerQuantityMax: 1200,
    domesticPriceMin: 2.5,
    floatRateMax: 1.3,
    transportCostMin: 0,
    warehouseImportPriceMax: 9.99,
    importPriceMin: 1.11,
    importPriceMax: 3.33,
    oemPriceMin: 4.44,
    oemPriceMax: 5.55,
    selectedTags: ['new', 'inactive'],
    sortBy: 'itemNumber',
    sortOrder: 'ascend',
  },
  '远程查询参数应由列筛选、tag、排序和分页状态生成，并裁剪空文本与 all tag',
)

assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: 'CONTAINER-NO-SORT',
    filters: { barcode: ' 9300 ' },
    selectedTags: [],
    pageNumber: 1,
    pageSize: 50,
  }),
  {
    containerGuid: 'CONTAINER-NO-SORT',
    pageNumber: 1,
    pageSize: 50,
    barcode: '9300',
  },
  '远程查询参数没有排序和 tag 时不应提交空字段',
)

assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: 'CONTAINER-TYPE-TAGS',
    filters: { productTypes: ['normal'] },
    selectedTags: ['multi', 'setChild', 'inactive'],
    pageNumber: 1,
    pageSize: 50,
  }),
  {
    containerGuid: 'CONTAINER-TYPE-TAGS',
    pageNumber: 1,
    pageSize: 50,
    productTypes: ['normal', 'multi', 'setChild'],
    selectedTags: ['inactive'],
  },
  '商品类型统计 tag 应转换为 productTypes 参数，且不混入 selectedTags',
)

assertDeepEqual(
  mergeContainerDetailLoadedItems(
    [
      { id: 401, hguid: 'merge-401', 商品名称: '旧 401' },
      { id: 402, hguid: 'merge-402', 商品名称: '旧 402' },
    ],
    [
      { id: 402, hguid: 'merge-402', 商品名称: '新 402' },
      { id: 403, hguid: 'merge-403', 商品名称: '新 403' },
    ],
  ).map((row) => ({ hguid: row.hguid, name: row.商品名称 })),
  [
    { hguid: 'merge-401', name: '旧 401' },
    { hguid: 'merge-402', name: '新 402' },
    { hguid: 'merge-403', name: '新 403' },
  ],
  '懒加载追加明细时应按 hguid 去重并用新页数据覆盖重复行',
)

assertDeepEqual(
  mergeContainerDetailLoadedItems(
    [{ id: 501, hguid: '', 商品名称: '无 GUID 旧行' }],
    [{ id: 501, hguid: '', 商品名称: '无 GUID 新行' }],
  ).map((row) => row.商品名称),
  ['无 GUID 旧行', '无 GUID 新行'],
  '缺少 hguid 的明细不能被误判为同一行',
)

assertDeepEqual(
  getContainerDetailRemoteQueryResetState({ selectedRowKeys: ['a', 'b'] }),
  {
    selectedRowKeys: [],
    loadedItems: [],
    pageNumber: 1,
  },
  '远程 query 变化时应重置选择、已加载明细和页码',
)
const sortableFields: ContainerDetailSortField[] = [
  'itemNumber',
  'barcode',
  'productName',
  'englishName',
  'productType',
  'newProduct',
  'matchType',
  'containerPieces',
  'middlePackQuantity',
  'containerQuantity',
  'domesticPrice',
  'floatRate',
  'transportCost',
  'warehouseImportPrice',
  'importPrice',
  'oemPrice',
  'warehouseStatus',
  'remark',
]
assertDeepEqual(
  sortableFields.filter((field) => !isContainerDetailSortField(field)),
  [],
  '货柜明细所有可排序字段都应通过运行时白名单校验',
)
assertEqual(isContainerDetailSortField('装柜数量'), false, '中文 dataIndex 不应被当作货柜明细排序字段')
assertEqual(isContainerDetailSortField('unknownField'), false, '未知字段不应被当作货柜明细排序字段')
assertEqual(isContainerDetailSortField(undefined), false, '空字段不应被当作货柜明细排序字段')
assertEqual(
  getContainerDetailProductCode({ id: 301, hguid: 'code-301', 商品编码: '   ', 商品信息: { 商品编码: ' HB301 ' } }),
  'HB301',
  '商品编码解析应 trim 明细编码并在空白时回退商品信息编码',
)
assertEqual(
  getContainerDetailProductCode({ id: 302, hguid: 'code-302', 商品编码: '   ', 商品信息: { 商品编码: '   ' } }),
  undefined,
  '商品编码解析应把空白编码视为缺失',
)
assertEqual(
  getContainerDetailCreateProductRowLabel({
    id: 311,
    hguid: 'label-311',
    商品编码: 'P-LABEL',
    商品信息: { 货号: 'HB308-031' },
  }),
  'HB308-031',
  '创建新商品中文名提示应优先使用货号定位行',
)
assertEqual(
  getContainerDetailCreateProductRowLabel({
    id: 312,
    hguid: 'label-312',
    商品编码: 'P-LABEL',
  }),
  'P-LABEL',
  '创建新商品中文名提示在缺少货号时应使用商品编码定位行',
)
assertEqual(
  getContainerDetailCreateProductRowLabel({
    id: 313,
    hguid: 'label-313',
  }),
  'label-313',
  '创建新商品中文名提示在缺少货号和商品编码时应使用明细 GUID 定位行',
)
assertDeepEqual(
  findContainerDetailRowsMissingProductName([
    { id: 314, hguid: 'name-314', 是否新商品: true, 商品名称: '皮带', 商品信息: { 货号: 'HB308-030' } },
    { id: 315, hguid: 'name-315', 是否新商品: true, 商品名称: 'belt', 商品信息: { 货号: 'HB308-031' } },
    { id: 318, hguid: 'name-318', 是否新商品: true, 商品名称: '22-36,3PCS', 商品信息: { 货号: 'HB137-480' } },
    { id: 316, hguid: 'name-316', 是否新商品: true, 商品名称: '   ', 商品信息: { 货号: 'HB308-032' } },
    { id: 317, hguid: 'name-317', 是否新商品: false, 商品名称: 'belt', 商品信息: { 货号: 'HB308-033' } },
  ]),
  [
    { hguid: 'name-316', label: 'HB308-032', productName: '' },
  ],
  '创建新商品前应只拦截新商品中商品名称为空的明细，非中文名称也应通过',
)
assertDeepEqual(
  findContainerDetailRowsMissingCreateProductRetailPrice([
    { id: 319, hguid: 'price-319', 是否新商品: true, 贴牌价格: 25, 商品信息: { 货号: 'HB137-480' } },
    { id: 320, hguid: 'price-320', 是否新商品: true, 贴牌价格: 0, 商品信息: { 货号: 'HB137-481' } },
    { id: 321, hguid: 'price-321', 是否新商品: true, 商品信息: { 货号: 'HB137-482' } },
    { id: 322, hguid: 'price-322', 是否新商品: true, 贴牌价格: 0, warehouseOEMPrice: 26, 商品信息: { 货号: 'HB137-483' } },
    { id: 323, hguid: 'price-323', 是否新商品: false, 贴牌价格: 0, 商品信息: { 货号: 'HB137-484' } },
  ]),
  [
    { hguid: 'price-320', label: 'HB137-481', retailPrice: 0 },
    { hguid: 'price-321', label: 'HB137-482', retailPrice: undefined },
  ],
  '创建新商品前应只拦截新商品中零售价不大于 0 的明细，页面有效零售价和仓库零售价都应通过',
)
assertEqual(getContainerDetailMatchType({ id: 306, hguid: 'match-306', matchType: 'productCode' }), 'productCode', '匹配方式应优先读取前端归一化字段')
assertEqual(getContainerDetailMatchType({ id: 307, hguid: 'match-307', MatchType: 'SupplierItem' }), 'supplierItem', '匹配方式应兼容后端 PascalCase 字段')
assertEqual(getContainerDetailMatchType({ id: 308, hguid: 'match-308', 是否新商品: true }), 'unmatched', '缺少匹配方式的新商品应显示未匹配')
assertEqual(getContainerDetailMatchType({ id: 309, hguid: 'match-309', 是否新商品: false }), 'unmatched', '缺少匹配方式的已有商品不能默认显示商品编码匹配')
assertEqual(
  getContainerDetailMatchType({
    id: 310,
    hguid: 'match-310',
    MatchType: 'ProductCode',
    商品信息: { 商品编码: 'HB013-108', 货号: 'HB013-108' },
  }),
  'productCode',
  '后端明确返回 ProductCode 时应展示商品编码匹配',
)
assertDeepEqual(
  applyContainerDetailWarehouseStatusByProductCodes([
    { id: 303, hguid: 'code-303', 商品编码: ' HB303 ', warehouseIsActive: false },
    { id: 304, hguid: 'code-304', 商品信息: { 商品编码: 'HB303' }, warehouseIsActive: false },
    { id: 305, hguid: 'code-305', 商品编码: 'HB305', warehouseIsActive: false },
  ], ['HB303'], true).map((row) => ({ hguid: row.hguid, active: row.warehouseIsActive })),
  [
    { hguid: 'code-303', active: true },
    { hguid: 'code-304', active: true },
    { hguid: 'code-305', active: false },
  ],
  '仓库状态本地更新应按 trim 后商品编码同步同商品行',
)
const defaultColumnOrder: ContainerDetailTableColumnKey[] = ['index', 'image', 'itemNumber', 'categoryName', 'barcode', 'productName', 'englishName']
assertDeepEqual(
  mergeContainerDetailColumnOrder(['barcode', 'unknown', 'barcode', 'image'], defaultColumnOrder),
  ['barcode', 'image', 'index', 'itemNumber', 'categoryName', 'productName', 'englishName'],
  '货柜明细列顺序应过滤未知列、去重并补齐新增列',
)
assertDeepEqual(
  moveContainerDetailColumnOrder(defaultColumnOrder, 'barcode', 'image'),
  ['index', 'barcode', 'image', 'itemNumber', 'categoryName', 'productName', 'englishName'],
  '货柜明细列拖拽应把 active 列移动到 over 列位置',
)
assertDeepEqual(
  moveContainerDetailColumnOrder(defaultColumnOrder, 'missing', 'image'),
  defaultColumnOrder,
  '货柜明细列拖拽遇到未知列时应保持原顺序',
)
assertDeepEqual(
  moveContainerDetailColumnOrder(defaultColumnOrder, 'image', 'image'),
  defaultColumnOrder,
  '货柜明细列拖拽 active 与 over 相同时应保持原顺序',
)
assertEqual(
  isContainerDetailColumnOrderCustomized(defaultColumnOrder, defaultColumnOrder),
  false,
  '货柜明细列顺序与默认顺序一致时不应显示重置入口',
)
assertEqual(
  isContainerDetailColumnOrderCustomized(moveContainerDetailColumnOrder(defaultColumnOrder, 'barcode', 'image'), defaultColumnOrder),
  true,
  '货柜明细列顺序被拖拽修改后应显示重置入口',
)
assertEqual(
  isContainerDetailColumnOrderCustomized([], defaultColumnOrder),
  false,
  '货柜明细列顺序初始化为空时不应误判为已自定义',
)
assertEqual(
  getContainerDetailWarehouseActionFailureMessage({ success: true, failedCount: 1, errors: ['HB303 更新失败'] }, '批量上下架失败'),
  'HB303 更新失败',
  '仓库上下架结果有失败明细时应视为失败',
)
assertEqual(
  getContainerDetailWarehouseActionFailureMessage({ success: false, message: '后端失败' }, '批量上下架失败'),
  '后端失败',
  '仓库上下架结果根 success false 时应返回失败信息',
)
assertEqual(
  getContainerDetailWarehouseActionFailureMessage({ success: true, failedCount: 0 }, '批量上下架失败'),
  undefined,
  '仓库上下架结果全成功时不应返回失败信息',
)

const pageSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.tsx', 'utf8')
const pageStyleSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.css', 'utf8')
const mobileLayoutSource = readFileSync('src/layout/MobileLayout.tsx', 'utf8')
const containerDetailLogicSource = readFileSync('src/pages/Warehouse/ContainerDetail/containerDetailLogic.ts', 'utf8')
const warehouseProductServiceSource = readFileSync('src/services/warehouseProductService.ts', 'utf8')

assertEqual(
  pageSource.includes("const DEFAULT_CONTAINER_DETAIL_SORT: ContainerDetailSortState = { field: 'itemNumber', order: 'ascend' }"),
  true,
  '货柜明细页应声明货号升序默认排序',
)
assertEqual(
  pageSource.includes('useState<ContainerDetailSortState>(DEFAULT_CONTAINER_DETAIL_SORT)'),
  true,
  '货柜明细页初始排序应默认使用货号升序',
)
assertEqual(
  pageSource.includes('setSortState(DEFAULT_CONTAINER_DETAIL_SORT)'),
  true,
  '清空表格排序或列状态时应恢复货号升序默认排序',
)
assertEqual(
  pageSource.includes('const [showReadonlyOemPrice, setShowReadonlyOemPrice] = useState(false)'),
  true,
  '只读贴牌价格快览列应默认关闭',
)
assertEqual(
  pageSource.includes('showReadonlyOemPrice ? [readonlyOemPriceColumn] : []'),
  true,
  '只读贴牌价格快览列应只在开关打开时插入表格列',
)

const matchedPriceContainer = { 汇率: 4.5, 运费: 100, 总体积: 10 }
const matchedPriceRows: ContainerDetail[] = [
  {
    id: 701,
    hguid: 'match-price-701',
    商品编码: 'P-MATCH-1',
    国内价格: undefined,
    贴牌价格: 0,
    调整浮率: 1.2,
    装柜件数: 2,
    装柜数量: 20,
    单件体积: 0.5,
    运输成本: undefined,
    进口价格: 0,
    商品名称: '旧商品名',
    英文名称: 'Old English',
  },
  {
    id: 702,
    hguid: 'match-price-702',
    商品编码: 'P-MATCH-2',
    国内价格: 8.8,
    贴牌价格: 3.3,
    调整浮率: 1.1,
    装柜件数: 2,
    装柜数量: 24,
    单件装箱数: 12,
    单件体积: 0.2,
    合计装柜体积: 0.4,
    合计装柜金额: 232.32,
    商品名称: '保留价格但更新规格',
  },
  {
    id: 703,
    hguid: 'match-price-703',
    商品信息: { 货号: 'ITEM-703', 条形码: 'BAR-703' },
    国内价格: 0,
    贴牌价格: undefined,
    装柜件数: 3,
  },
  {
    id: 704,
    hguid: 'match-price-704',
    国内价格: undefined,
    贴牌价格: undefined,
  },
  {
    id: 705,
    hguid: 'match-price-705',
    商品编码: 'P-CODE-FIRST',
    localSupplierCode: '200',
    商品信息: { 货号: 'ITEM-FALLBACK', 条形码: 'BAR-FALLBACK' },
    贴牌价格: 0,
    装柜件数: 4,
  },
  {
    id: 706,
    hguid: 'match-price-706',
    商品信息: { 货号: 'HB138-066', 条形码: '9527913800028' },
    贴牌价格: 0,
    装柜件数: 12,
  },
  {
    id: 707,
    hguid: 'match-price-707',
    商品编码: 'P-STALE-TOTALS',
    国内价格: 5,
    调整浮率: undefined,
    装柜件数: 2,
    装柜数量: 10,
    单件装箱数: 5,
    单件体积: 0.5,
    合计装柜体积: 0,
    合计装柜金额: 0,
    运输成本: 0,
    进口价格: 0,
    商品名称: '旧合计商品',
  },
  {
    id: 708,
    hguid: 'match-price-708',
    商品信息: { 条形码: 'BARCODE-ONLY' },
    国内价格: undefined,
    贴牌价格: undefined,
  },
]

const matchedPriceUpdates = buildContainerDetailMatchedDomesticDataUpdates(
  matchedPriceRows,
  [
    { ProductCode: 'P-MATCH-1', ProductName: '新商品名', EnglishName: 'New English', WarehouseDomesticPrice: 11.6, WarehouseOEMPrice: 6.99, PackingQuantity: 48, WarehouseVolume: 0.118 },
    { ProductCode: 'P-MATCH-2', ProductName: '覆盖名称', EnglishName: 'Override English', WarehouseDomesticPrice: 22.2, WarehouseOEMPrice: 9.9, PackingQuantity: 24, WarehouseVolume: 0.33 },
    { ItemNumber: 'ITEM-703', ProductName: '货号匹配商品', EnglishName: 'Item Matched', WarehouseDomesticPrice: 5.5, WarehouseOEMPrice: 2.2, PackingQuantity: 10, WarehouseVolume: 0.25 },
    { ProductCode: 'P-CODE-FIRST', ItemNumber: 'OTHER-ITEM', ProductName: '商品编码优先商品', WarehouseOEMPrice: 7.7, PackingQuantity: 8 },
    { ItemNumber: 'ITEM-FALLBACK', ProductName: '不应使用的货号匹配', WarehouseOEMPrice: 1.1, PackingQuantity: 99 },
    { ItemNumber: 'HB138-066', ProductName: '金/黑框混30X40', DomesticOEMPrice: 15.5, PackingQuantity: 24 },
    { ProductCode: 'P-STALE-TOTALS', ProductName: '旧合计商品', WarehouseDomesticPrice: 5, PackingQuantity: 5, WarehouseVolume: 0.5 },
    { Barcode: 'BARCODE-ONLY', ProductName: '条码不应兜底', WarehouseDomesticPrice: 9.9, WarehouseOEMPrice: 8.8 },
  ] satisfies DetectionResult[],
  matchedPriceContainer,
)

assertDeepEqual(
  buildContainerDetailDetectionItems([
    { id: 901, hguid: 'detect-901', 商品信息: { 商品编码: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B', 货号: 'HB013-108', 条形码: '9528501322108' } },
    { id: 902, hguid: 'detect-902', 商品编码: 'P-CODE', localSupplierCode: '200', 商品信息: { 货号: 'ITEM-902' } },
    { id: 903, hguid: 'detect-903', 商品信息: { 条形码: 'BARCODE-ONLY' } },
    { id: 904, hguid: 'detect-904', 商品信息: { 商品编码: 'HB013-108', 货号: 'HB013-108', 条形码: '9528501322108' } },
  ]),
  [
    { ProductCode: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B', ItemNumber: 'HB013-108', SupplierCode: '200' },
    { ProductCode: 'P-CODE', ItemNumber: 'ITEM-902', SupplierCode: '200' },
    { ProductCode: 'HB013-108', ItemNumber: 'HB013-108', SupplierCode: '200' },
  ],
  '匹配检测项应同时提交商品编码和供应商 200 + 货号，且不提交条码兜底',
)

assertDeepEqual(
  buildContainerDetailMatchedDomesticDataUpdates(
    [
      {
        id: 904,
        hguid: 'match-price-904',
        商品编码: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B',
        商品信息: { 货号: 'HB013-108', 条形码: '9528501322108' },
        是否新商品: true,
      },
    ],
    [
      { ProductCode: 'G091539', ItemNumber: 'HB013-108', SupplierCode: '200', ProductName: 'FOLDABLE BROOM SET' },
      { Barcode: '9528501322108', ProductName: '条码不应兜底' },
    ] satisfies DetectionResult[],
    matchedPriceContainer,
  ),
  [
    {
      hguid: 'match-price-904',
      matchType: 'supplierItem',
      是否新商品: false,
      商品名称: 'FOLDABLE BROOM SET',
    },
  ],
  '商品编码不一致但供应商 200 + HB 货号命中时，应按供应商货号匹配而不是商品编码匹配',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 905,
        hguid: 'match-status-905',
        商品编码: 'P-SAME',
        localSupplierCode: '200',
        商品信息: { 货号: 'HB013-108', 条形码: '9528501322108' },
        是否新商品: true,
      },
    ],
    [
      { ProductCode: 'P-SAME', ProductName: 'FOLDABLE BROOM SET', WarehouseDomesticPrice: 13, WarehouseOEMPrice: 11.99 },
      { Barcode: '9528501322108', ProductName: '条码不应兜底', WarehouseDomesticPrice: 99 },
    ] satisfies DetectionResult[],
  ),
  [
    {
      hguid: 'match-status-905',
      matchType: 'productCode',
      是否新商品: false,
    },
  ],
  '真实商品编码一致时，加载态只读匹配校正应标记商品编码匹配且不生成价格写库字段',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9051,
        hguid: 'match-status-9051',
        商品编码: 'P-SAME',
        localSupplierCode: '200',
        商品信息: { 货号: 'HB013-108', 条形码: '9528501322108' },
        MatchType: 'ProductCode',
        是否新商品: false,
      },
    ],
    [
      {
        ProductCode: 'P-SAME',
        ItemNumber: 'HB013-108',
        matchType: 'both',
        ProductName: '扫把',
      },
    ] satisfies DetectionResult[],
  ),
  [
    {
      hguid: 'match-status-9051',
      matchType: 'productCode',
      是否新商品: false,
    },
  ],
  '后端返回 both 且商品编码也命中时，应优先展示商品编码匹配',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9052,
        hguid: 'match-status-9052',
        商品编码: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B',
        商品信息: { 货号: 'HB013-108', 条形码: '9528501322108' },
        是否新商品: true,
      },
    ],
    [
      {
        productCode: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B',
        itemNumber: 'HB013-108',
        exists: true,
        matchType: 'item_number',
        productName: '套扫',
      },
    ] satisfies DetectionResult[],
  ),
  [
    {
      hguid: 'match-status-9052',
      matchType: 'supplierItem',
      是否新商品: false,
    },
  ],
  '后端返回 item_number 时，即使结果包含 productCode，也应展示货号匹配',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 906,
        hguid: 'match-status-906',
        商品编码: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B',
        商品信息: { 货号: 'HB013-108', 条形码: '9528501322108' },
        是否新商品: true,
      },
    ],
    [
      {
        ProductCode: 'G091539',
        ItemNumber: 'HB013-108',
        SupplierCode: '200',
        MatchType: 'ProductCode',
        ProductName: 'FOLDABLE BROOM SET',
      },
      { Barcode: '9528501322108', ProductName: '条码不应兜底', WarehouseDomesticPrice: 99 },
    ] satisfies DetectionResult[],
  ),
  [
    {
      hguid: 'match-status-906',
      matchType: 'supplierItem',
      是否新商品: false,
    },
  ],
  '即使后端回传 ProductCode 提示，只要真实商品编码不同且 200 + 货号命中，展示匹配方式也应以供应商货号为准',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 907,
        hguid: 'match-status-907',
        商品信息: { 商品编码: 'HB013-108', 货号: 'HB013-108', 条形码: '9528501322108' },
        MatchType: 'ProductCode',
        是否新商品: false,
      },
    ],
    [
      {
        ProductCode: 'HB013-108',
        ItemNumber: 'HB013-108',
        SupplierCode: '200',
        MatchType: 'ProductCode',
        ProductName: 'FOLDABLE BROOM SET',
      },
    ] satisfies DetectionResult[],
  ),
  [
    {
      hguid: 'match-status-907',
      matchType: 'productCode',
      是否新商品: false,
    },
  ],
  '商品编码命中时，即使同时带有供应商 200 + 货号，也应展示商品编码匹配',
)

assertDeepEqual(
  matchedPriceUpdates,
  [
    {
      hguid: 'match-price-701',
      matchType: 'productCode',
      是否新商品: false,
      国内价格: 11.6,
      贴牌价格: 6.99,
      商品名称: '新商品名',
      英文名称: 'New English',
      单件装箱数: 48,
      装柜数量: 96,
      单件体积: 0.118,
      合计装柜体积: 0.236,
      合计装柜金额: 1336.32,
      运输成本: 0.02,
      进口价格: 2.83,
    },
    {
      hguid: 'match-price-702',
      matchType: 'productCode',
      是否新商品: false,
      商品名称: '覆盖名称',
      英文名称: 'Override English',
      单件装箱数: 24,
      装柜数量: 48,
      单件体积: 0.33,
      合计装柜体积: 0.66,
      合计装柜金额: 464.64,
      运输成本: 0.14,
      进口价格: 2.1,
    },
    {
      hguid: 'match-price-703',
      matchType: 'supplierItem',
      是否新商品: false,
      国内价格: 5.5,
      贴牌价格: 2.2,
      商品名称: '货号匹配商品',
      英文名称: 'Item Matched',
      单件装箱数: 10,
      装柜数量: 30,
      单件体积: 0.25,
      合计装柜体积: 0.75,
      合计装柜金额: 165,
      运输成本: 0.25,
      进口价格: 1.34,
    },
    {
      hguid: 'match-price-705',
      matchType: 'productCode',
      是否新商品: false,
      贴牌价格: 7.7,
      商品名称: '商品编码优先商品',
      单件装箱数: 8,
      装柜数量: 32,
    },
    {
      hguid: 'match-price-706',
      matchType: 'supplierItem',
      是否新商品: false,
      贴牌价格: 15.5,
      商品名称: '金/黑框混30X40',
      单件装箱数: 24,
      装柜数量: 288,
    },
    {
      hguid: 'match-price-707',
      matchType: 'productCode',
      是否新商品: false,
      合计装柜体积: 1,
      合计装柜金额: 50,
      运输成本: 1,
      进口价格: 1.92,
    },
  ],
  '匹配国内数据应只补缺失价格、覆盖名称规格，并同步重算装柜数量、体积、运输成本和进口价格',
)
assertEqual(pageSource.includes('匹配国内数据'), true, '页面按钮文案应改为匹配国内数据')
assertEqual(
  pageSource.includes("renderColumnTitle('warehouseImportPrice'") &&
    pageSource.includes("t('containers.fields.warehouseImportPrice'"),
  true,
  '货柜明细应独立显示仓库当前进货价格列',
)
assertEqual(
  pageSource.includes('buildContainerDetailMatchedDomesticDataUpdates(scopedRows, detected, container)') &&
  pageSource.includes('const scopedRows = selectedRowKeys.length ? targetRows : await fetchAllRowsForCurrentQuery()'),
  true,
  '页面应调用匹配国内数据 helper，未勾选时按当前筛选结果全量处理',
)
assertEqual(
  pageSource.includes('findContainerDetailRowsMissingProductName(targetRows)') &&
    pageSource.includes("'containers.messages.createProductsMissingProductName'") &&
    pageSource.includes('missingProductNameRows.map((row) => row.label).join'),
  true,
  '创建新商品前应拦截商品名称为空的新商品，并在提示中带出可定位的货号或编码',
)
assertEqual(
  pageSource.includes('editingProductNameRowKey') &&
    pageSource.includes('startEditingProductName(row)') &&
    pageSource.includes('commitProductNameEdit(row)') &&
    pageSource.includes("saveRowPatch(row, { 商品名称: productName })"),
  true,
  '商品名称列应支持双击进入编辑，并复用明细保存接口写回中文商品名',
)
assertEqual(
  pageStyleSource.includes('.container-detail-product-name-editable') &&
    pageStyleSource.includes('.container-detail-product-name-input'),
  true,
  '商品名称双击编辑应有稳定样式类，避免输入态改变表格布局',
)
assertEqual(
  pageSource.includes('const detectionItems = buildContainerDetailDetectionItems(scopedRows)'),
  true,
  '页面匹配国内数据检测请求应复用统一检测项 helper',
)
assertEqual(
  containerDetailLogicSource.includes("SupplierCode: '200'") && !containerDetailLogicSource.includes('Barcode: getContainerDetailBarcode(row)'),
  true,
  '匹配国内数据检测请求应固定供应商编码 200 且不再提交条码兜底',
)
assertEqual(
  pageSource.includes('void reconcileLoadedMatchStatus(result.items, currentRequestId)') &&
    pageSource.includes('products.filter((row) => getContainerDetailProductCode(row) || getContainerDetailItemNumber(row))') &&
    pageSource.includes('buildContainerDetailMatchStatusUpdates(rowsNeedingMatchStatus, detected)') &&
    pageSource.includes('加载态只校正表格展示状态，不写库'),
  true,
  '页面加载后应对当前懒加载块只读校正匹配状态，避免旧错误 MatchType 留在表格中且避免写库',
)
assertEqual(
  pageSource.includes('SkipRelatedProductSync: true'),
  true,
  '匹配国内数据应只更新货柜明细，跳过关联商品同步',
)
assertEqual(
  pageSource.includes('value={getContainerDetailEnglishName(row) ?? \'\'}'),
  true,
  '英文名称输入框应受控绑定行状态，批量翻译后才能立即刷新显示',
)
assertEqual(
  pageSource.includes('defaultValue={getContainerDetailEnglishName(row)}'),
  false,
  '英文名称输入框不能使用 defaultValue，否则批量翻译后的状态变化不会刷新已挂载输入框',
)
assertEqual(
  pageSource.includes('<Input.TextArea'),
  true,
  '英文名称输入框应使用 TextArea 支持长英文自动换行',
)
assertEqual(
  pageSource.includes('autoSize={{ minRows: 1, maxRows: 2 }}'),
  true,
  '英文名称输入框自动换行最多显示 2 行',
)
assertEqual(
  pageSource.includes('setSelectedRowKeys([])'),
  true,
  '筛选条件变化时应清空已选明细，避免隐藏选中行后批量操作退回作用于当前全部可见行',
)
assertEqual(
  pageSource.includes('[active, detailQueryKey]') &&
    pageSource.includes('detailQueryKey 已包含货柜、筛选、排序和 tag'),
  true,
  '清空已选明细的 effect 应监听 active 和远程查询 key，覆盖顶部筛选、列头过滤和列头排序',
)
assertEqual(
  pageSource.includes("{ value: 'all', label: t('containers.filters.allTags'), color: 'blue' }"),
  true,
  '全部标签统计项应保持蓝色，作为总览入口',
)
assertEqual(
  pageSource.includes("{ value: 'new', label: t('containers.tags.newProduct'), color: 'cyan' }"),
  true,
  '新商品统计项应使用不同于全部标签的颜色',
)
assertEqual(
  pageSource.includes("{ value: 'multi', label: t('containers.productTypes.multiCode'), color: 'purple' }"),
  true,
  '统计 tag 应包含多码商品类型入口',
)
assertEqual(
  pageSource.includes('productTypeFilter'),
  false,
  '顶部独立商品类型下拉已取消，商品类型过滤应通过统计 tag 和列头筛选完成',
)
assertEqual(
  pageSource.includes('color={option.color}'),
  true,
  '统计标签应始终按各自语义色显示，不只在选中时显示蓝色',
)
assertEqual(
  pageSource.includes('const targetRows = selectedRowKeys.length ? selectedRows : displayRows'),
  true,
  '批量目标行应按是否存在选择意图判断，隐藏选中行时不能退回当前全部可见行且未选中时使用最终可见行',
)
assertEqual(
  pageSource.includes('const ensureTargetRowsVisible = () => {'),
  true,
  '批量操作应在已选行被当前筛选隐藏时统一拦截',
)
assertEqual(
  pageSource.includes('已选明细不在当前筛选结果中，请重新选择后再操作'),
  true,
  '隐藏选中行触发批量操作时应提示用户重新选择',
)
assertEqual(pageSource.includes('role="button"'), true, '统计 tag 应提供按钮语义')
assertEqual(pageSource.includes('tabIndex={0}'), true, '统计 tag 应可通过键盘聚焦')
assertEqual(pageSource.includes('aria-pressed={active}'), true, '统计 tag 应暴露当前选中状态')
assertEqual(
  pageSource.includes("event.key === 'Enter' || event.key === ' '"),
  true,
  '统计 tag 应支持 Enter 和空格键触发过滤',
)

const hqSelectionRows: ContainerDetail[] = [
  { id: 10, hguid: 'detail-10', 商品编码: 'HB001', 是否新商品: false },
  { id: 11, hguid: 'detail-11', 商品编码: 'HB002', 是否新商品: true },
  { id: 12, hguid: 'detail-12', 商品信息: { 商品编码: 'HB003' }, 是否新商品: false },
  { id: 13, hguid: 'detail-13', 商品编码: 'HB001', 是否新商品: false },
  { id: 14, hguid: 'detail-14', 是否新商品: false },
]

assertDeepEqual(
  buildContainerDetailHqPushSelection(hqSelectionRows),
  {
    productCodes: ['HB001', 'HB003'],
    items: [
      {
        productCode: 'HB001',
        localSupplierCode: undefined,
        itemNumber: undefined,
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: undefined,
        importPrice: undefined,
        oemPrice: undefined,
        isNewProduct: false,
      },
      {
        productCode: 'HB003',
        localSupplierCode: undefined,
        itemNumber: undefined,
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: undefined,
        importPrice: undefined,
        oemPrice: undefined,
        isNewProduct: false,
      },
    ],
    skippedNewProductCount: 1,
    missingProductCodeCount: 1,
  },
  '发送到 HQ 应只收集本地已有且有商品编码的选中明细，并对商品编码去重',
)

assertDeepEqual(
  buildContainerDetailHqPushSelection([
    {
      id: 15,
      hguid: 'detail-15',
      商品编码: ' HB015 ',
      是否新商品: 1 as unknown as boolean,
      国内价格: 4.2,
      进口价格: 1.55,
      贴牌价格: 1.72,
      localSupplierCode: 'DATS',
      商品信息: { 货号: '72653' },
    },
    {
      id: 16,
      hguid: 'detail-16',
      商品编码: '   ',
      商品名称: '货柜商品名',
      英文名称: 'Container Product',
      商品信息: { 商品编码: ' HB016 ', 货号: '72654', localSupplierCode: 'COS', 条形码: '9527000016', 商品图片: 'local-product-image.jpg' },
      是否新商品: false,
      国内价格: 5.1,
      进口价格: 1.88,
      贴牌价格: 2.01,
      warehouseOEMPrice: 3.21,
      warehouseIsActive: false,
    },
    { id: 17, hguid: 'detail-17', 商品编码: 'HB017', 是否新商品: true, warehouseIsActive: true },
  ]),
  {
    productCodes: ['HB016'],
    items: [
      {
        productCode: 'HB016',
        localSupplierCode: 'COS',
        itemNumber: '72654',
        productName: '货柜商品名',
        englishName: 'Container Product',
        barcode: '9527000016',
        imageUrl: 'local-product-image.jpg',
        domesticPrice: 5.1,
        importPrice: 1.88,
        oemPrice: 3.21,
        isNewProduct: false,
      },
    ],
    skippedNewProductCount: 2,
    missingProductCodeCount: 0,
  },
  '发送到 HQ 应把 1 也视为新商品跳过，并在明细编码为空白时回退商品信息编码',
)

assertDeepEqual(
  buildContainerDetailHqPushSelection([
    { id: 20, hguid: 'detail-20', 商品编码: 'HB020', 是否新商品: true },
    {
      id: 21,
      hguid: 'detail-21',
      是否新商品: false,
      localSupplierCode: 'DATS',
      商品信息: { 货号: '72655' },
      国内价格: 6.2,
      进口价格: 2.11,
      贴牌价格: 2.34,
      WarehouseOEMPrice: 4.56,
      warehouseIsActive: true,
    },
  ]),
  {
    productCodes: [],
    items: [
      {
        productCode: undefined,
        localSupplierCode: 'DATS',
        itemNumber: '72655',
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: 6.2,
        importPrice: 2.11,
        oemPrice: 4.56,
        isNewProduct: false,
      },
    ],
    skippedNewProductCount: 1,
    missingProductCodeCount: 0,
  },
  '缺商品编码但有供应商和货号时，发送到 HQ 仍应携带候选项',
)

assertDeepEqual(
  buildContainerDetailHqPushSelection([
    {
      id: 22,
      hguid: 'detail-22',
      商品编码: 'HB022',
      是否新商品: false,
      warehouseIsActive: true,
      国内价格: 3.2,
      进口价格: 1.08,
      贴牌价格: 1.2,
      warehouseOEMPrice: 2.4,
      商品图片: 'container-hb022.jpg',
    },
    {
      id: 23,
      hguid: 'detail-23',
      商品编码: 'HB023',
      商品信息: { 商品图片: 'product-hb023.jpg' },
      是否新商品: false,
      warehouseIsActive: false,
      国内价格: 3.5,
      进口价格: 1.18,
      贴牌价格: 1.3,
    },
  ]).items,
  [
    {
      productCode: 'HB022',
      localSupplierCode: undefined,
      itemNumber: undefined,
      productName: undefined,
      englishName: undefined,
      barcode: undefined,
      imageUrl: 'container-hb022.jpg',
      domesticPrice: 3.2,
      importPrice: 1.08,
      oemPrice: 2.4,
      isNewProduct: false,
    },
    {
      productCode: 'HB023',
      localSupplierCode: undefined,
      itemNumber: undefined,
      productName: undefined,
      englishName: undefined,
      barcode: undefined,
      imageUrl: 'product-hb023.jpg',
      domesticPrice: 3.5,
      importPrice: 1.18,
      oemPrice: 1.3,
      isNewProduct: false,
    },
  ],
  '发送到 HQ 的候选项应保留图片和价格，但不得携带仓库上下架状态',
)

const normalizedPushFailure = normalizeContainerDetailPushToHqPayload({
  failedCount: 2,
  errors: ['商品不存在', '价格异常'],
})
assertEqual(normalizedPushFailure?.successCount, 0, '发送到 HQ 失败 payload 应归一成功数')
assertEqual(normalizedPushFailure?.failedCount, 2, '发送到 HQ 失败 payload 应归一失败数')
assertEqual(normalizedPushFailure?.totalCount, 2, '发送到 HQ 失败 payload 缺少 totalCount 时应使用成功数加失败数兜底')
assertEqual(normalizedPushFailure?.affectedRowCount, 0, '发送到 HQ 失败 payload 应归一 HQ 影响记录数')
assertDeepEqual(normalizedPushFailure?.errors, ['商品不存在', '价格异常'], '发送到 HQ 失败 payload 应保留错误明细')

const rootPayloadPushFailure = extractPushToHqErrorResult({
  payload: {
    success: false,
    message: '后端明确返回失败',
  },
})
assertEqual(rootPayloadPushFailure?.message, '后端明确返回失败', '发送到 HQ 失败解析应支持 payload 根对象 message')

assertEqual(pageSource.includes('createPushProductsToHqJob({'), true, '页面应先创建发送到 HQ 后台 job')
assertEqual(pageSource.includes('getPushProductsToHqJob'), true, '页面应通过查询接口轮询发送到 HQ job')
assertEqual(pageSource.includes('createHqSyncJobPoller({'), true, '页面应复用后台 job 轮询器等待发送到 HQ 终态')
assertEqual(pageSource.includes('buildContainerDetailHqPushSelection(selectedRows)'), true, '页面应只基于手动选中的明细构建发送范围')
assertEqual(pageSource.includes('items: selection.items'), true, '页面发送到 HQ 时应把候选 items 一并发送给后端')
assertEqual(pageSource.includes('const pushToHqLoadingRef = useRef(false)'), true, '页面应使用 ref 锁防止连续点击重复发送')
assertEqual(pageSource.includes('releasePushToHqLoading()'), true, '发送到 HQ job 提交成功后应立即解除按钮 loading')
assertEqual(pageSource.includes("notification.info({") && pageSource.includes("key: pushToHqNotificationKey"), true, '发送到 HQ job 提交后应展示后台执行通知')
assertEqual(pageSource.includes("notification.success({") && pageSource.includes("notification.warning({") && pageSource.includes("notification.error({"), true, '发送到 HQ job 终态应按成功、部分成功和失败展示通知')
const pushToHqPollingSource = pageSource.slice(
  pageSource.indexOf('const pollPushToHqJob = ('),
  pageSource.indexOf('const handlePushSelectedProductsToHq = async () => {'),
)
assertEqual(pushToHqPollingSource.includes('loadData()'), false, '发送到 HQ job 终态不应重新加载货柜明细表格')
assertEqual(pushToHqPollingSource.includes('showPushToHqResult'), false, '发送到 HQ job 终态只使用右上角通知，不应再弹结果 Modal')
assertEqual(pageSource.includes("message.warning(t('containers.messages.pushToHqSkippedNewProducts'"), true, '选中明细包含新商品时应给出友好 warning')
assertEqual(pageSource.includes('发送 HQ 的结果统一收敛到右上角通知'), true, '发送到 HQ 提交失败也应使用右上角通知承载结果')
assertEqual(pageSource.includes("message: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败')"), true, '后端明确失败时应展示失败通知而不是部分成功')
assertEqual(pageSource.includes('result.warehouseInventoriesCreated'), true, '结果弹窗应展示仓库库存新增统计')
assertEqual(pageSource.includes('result.warehouseInventoriesUpdated'), true, '结果弹窗应展示仓库库存更新统计')
assertEqual(pageSource.includes('result.storeRetailPricesCreated'), true, '结果弹窗应展示分店价格新增统计')
assertEqual(pageSource.includes('result.productSetCodesCreated'), true, '结果弹窗应展示套装多码新增统计')
assertEqual(pageSource.includes('result.storeMultiCodesCreated'), true, '结果弹窗应展示分店多码新增统计')
assertEqual(pageSource.includes('disabled={!selectedRowKeys.length || pushToHqLoading}'), true, '发送到 HQ 按钮必须要求手动选中明细')
assertEqual(
  pageSource.includes('createContainerProductCreationJob({'),
  true,
  '创建新商品应提交后端后台 job，而不是由页面串行写多个服务',
)
assertEqual(
  pageSource.includes('buildContainerCreateProductsOperationId(containerGuid, detailHguids)'),
  true,
  '创建新商品应使用货柜和明细生成稳定 operationId',
)
assertEqual(
  pageSource.includes('waitForContainerProductCreationJob(job.jobId)'),
  true,
  '创建新商品应轮询后台 job 直到终态',
)
const createProductsJobSource = pageSource.slice(
  pageSource.indexOf('const showCreateProductsJobResult = (job: ContainerProductCreationJob) => {'),
  pageSource.indexOf('const updateExistingPurchase = async () => {'),
)
assertEqual(createProductsJobSource.includes('loadData()'), false, '批量创建新商品后台任务终态不应自动刷新货柜明细表格')
assertEqual(createProductsJobSource.includes('Modal.'), false, '批量创建新商品后台任务终态只使用右上角通知，不应再弹结果 Modal')
assertEqual(
  pageSource.includes("createPushProductsToHqJob") && pageSource.includes("getPushProductsToHqJob"),
  true,
  '发送到 HQ 应导入后台 job 创建和查询接口',
)
assertEqual(
  pageSource.includes('updateProduct(code, { purchasePrice: row.进口价格 ?? 0 })'),
  false,
  '更新已有商品价格不应调用普通 POS 商品整对象更新接口，避免清空名称、条码和上下架状态',
)
assertEqual(
  pageSource.indexOf('await batchUpdateWarehouseProducts(updates.map') < pageSource.indexOf('await upsertRetailForActiveStores(updates.map'),
  true,
  '更新已有商品价格应先确认仓库商品批量更新成功，再继续分店价格 upsert',
)
assertEqual(
  pageSource.includes('OEMPrice: oemPrice') &&
    pageSource.includes('StoreRetailPriceValue: oemPrice') &&
    pageSource.includes('MultiCodeRetailPrice: oemPrice'),
  true,
  '更新已有商品价格应同时提交有效贴牌价格，补齐商品主表和分店零售价',
)
assertEqual(
  pageSource.includes('const oemPrice = resolveContainerDetailOemPrice(row)') &&
    pageSource.includes('((row.进口价格 ?? 0) > 0 || (oemPrice ?? 0) > 0)') &&
    !pageSource.includes('hasImportDiff || hasOemDiff'),
  true,
  '更新已有商品价格应以有效贴牌价为准提交价格，不应因检测到的仓库价格相同而跳过',
)
assertEqual(
  pageSource.includes("message.error(error instanceof Error ? error.message : t('containers.messages.purchasePricesUpdateFailed', '更新已有商品价格失败'))"),
  true,
  '更新已有商品价格失败时应给用户可见错误提示',
)
assertEqual(
  pageSource.indexOf('await batchUpdateWarehouseProducts(updates.map') < pageSource.indexOf('await upsertMultiCodeForActiveStores(updates.map'),
  true,
  '更新已有商品价格应先确认仓库商品批量更新成功，再继续多码价格 upsert',
)
assertEqual(
  warehouseProductServiceSource.includes("ensureApiSuccess(raw?.success ?? raw?.isSuccess, raw?.message, '仓库批量更新失败')"),
  true,
  '仓库商品批量更新 service 应校验根响应失败，失败时阻断后续写表',
)
assertEqual(
  warehouseProductServiceSource.includes("throw new Error(result.message || errors.join('；') || '仓库批量更新部分失败')"),
  true,
  '仓库商品批量更新 service 应在 failedCount/errors 表示部分失败时抛错',
)
assertEqual(
  pageSource.includes('!access.canEditContainer || !access.canManagePosProducts'),
  true,
  '创建新商品函数内应同时校验货柜编辑和 POS 商品管理权限',
)
assertEqual(
  pageSource.includes('access.canEditContainer && access.canManagePosProducts'),
  true,
  '创建新商品入口应同时要求货柜编辑和 POS 商品管理权限',
)
assertEqual(
  pageSource.includes('createProductsLoadingRef.current'),
  true,
  '创建新商品应使用 ref 锁防止连续点击重复提交',
)
assertEqual(
  pageSource.includes('pendingDetailSavePromisesRef') &&
    pageSource.includes('failedDetailSaveKeysRef') &&
    pageSource.includes('buildContainerDetailSaveFailureKeys(saveKey, patch)') &&
    pageSource.includes('blurActiveContainerDetailEditableCell()') &&
    pageSource.includes('flushPendingDetailSaves') &&
    pageSource.includes('failedDetailSaveKeysRef.current.size > 0') &&
    pageSource.indexOf('blurActiveContainerDetailEditableCell()') < pageSource.indexOf('await flushPendingDetailSaves()') &&
    pageSource.indexOf('await flushPendingDetailSaves()') < pageSource.indexOf('const missingProductNameRows = findContainerDetailRowsMissingProductName(targetRows)') &&
    pageSource.indexOf('await flushPendingDetailSaves()') < pageSource.indexOf('const missingRetailPriceRows = findContainerDetailRowsMissingCreateProductRetailPrice(targetRows)') &&
    pageSource.indexOf('await flushPendingDetailSaves()') < pageSource.indexOf('const job = await createContainerProductCreationJob({'),
  true,
  '创建新商品前必须先触发编辑单元格 blur 并等待货柜明细保存完成，避免后台 job 读取旧值',
)
assertEqual(
  pageSource.includes('pendingDetailSaveCount > 0') &&
    pageSource.includes('disabled: createProductsLoading || pendingDetailSaveCount > 0'),
  true,
  '创建新商品入口应在明细保存中禁用，避免保存竞态',
)
assertEqual(
  pageSource.includes('handleDetailSaveError') &&
    pageSource.includes('.catch(handleDetailSaveError)'),
  true,
  '行内保存的 fire-and-forget 调用应捕获失败，避免未处理 Promise 拒绝',
)
assertEqual(
  pageSource.includes('await createProduct({'),
  false,
  '创建新商品页面不应再逐行调用 POS createProduct',
)
assertEqual(
  pageSource.includes('batchCreateProducts(created.map'),
  false,
  '创建新商品页面不应再直接批量写仓库商品',
)
assertEqual(
  pageSource.includes('catch(() => null)'),
  false,
  '更新已有商品价格不应吞掉 POS 商品更新失败',
)
assertEqual(
  pageSource.includes('posUpdateFailures'),
  false,
  '更新已有商品价格不应再保留 POS 商品整对象更新失败分支',
)

const priceContainer = {
  id: 1,
  hguid: 'container-1',
  汇率: 4.5,
  运费: 12000,
  总体积: 67.44,
}
const priceRows: ContainerDetail[] = [
  {
    id: 101,
    hguid: 'price-101',
    国内价格: 3.9,
    装柜数量: 1200,
    合计装柜体积: 6.744,
    运输成本: 0,
    进口价格: 1,
    调整浮率: 1,
  },
  {
    id: 102,
    hguid: 'price-102',
    国内价格: 5.2,
    装柜数量: 600,
    合计装柜体积: 3.372,
    运输成本: 0,
    进口价格: 2,
    调整浮率: 1.1,
  },
]

assertEqual(
  calculateContainerDetailTransportCost(priceRows[0], priceContainer),
  1,
  '运输成本应按运费、明细体积、装柜数量、总体积分摊为单位成本并保留 2 位',
)
assertEqual(
  calculateContainerDetailImportPrice(priceRows[0], priceContainer, 1.2, 1),
  2.04,
  '进口价格应沿用当前公式并保留 2 位',
)
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 105, hguid: 'price-105', 装柜件数: 2, 装柜数量: 5, 商品信息: { 单件体积: 0.5 } },
    { ...priceContainer, 运费: 100, 总体积: 10 },
  ),
  2,
  '明细级合计体积缺失时应使用商品信息单件体积计算单位运输成本',
)
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 107, hguid: 'price-107', 装柜件数: 2, 装柜数量: 5, 单件体积: 0.25, 商品信息: { 单件体积: 0.5 } },
    { ...priceContainer, 运费: 100, 总体积: 10 },
  ),
  1,
  '明细单件体积应优先于商品信息单件体积计算单位运输成本',
)
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 108, hguid: 'price-108', 合计装柜体积: 2, 装柜件数: 2, 装柜数量: 5, 单件体积: 0.25, 商品信息: { 单件体积: 0.5 } },
    { ...priceContainer, 运费: 100, 总体积: 10 },
  ),
  4,
  '合计装柜体积应优先于装柜件数乘单件体积',
)
assertEqual(
  calculateContainerDetailTransportCost(priceRows[0], { ...priceContainer, 运费: 0 }),
  0,
  '运费为 0 是合法公式输入，应重算为 0 而不是保留旧运输成本',
)
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], 合计装柜体积: 0, 运输成本: 9 }, priceContainer),
  0,
  '明细体积为 0 是合法公式输入，应重算为 0 而不是保留旧运输成本',
)
assertEqual(
  calculateContainerDetailImportPrice({ ...priceRows[0], 国内价格: 0, 进口价格: 9 }, priceContainer, 1.2, 10),
  10.91,
  '国内价格为 0 是合法公式输入，应继续叠加运输成本计算进口价格',
)
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(priceRows, priceContainer, 1.2),
  [
    { hguid: 'price-101', 调整浮率: 1.2, 运输成本: 1, 进口价格: 2.04 },
    { hguid: 'price-102', 调整浮率: 1.2, 运输成本: 1, 进口价格: 2.35 },
  ],
  '批量应用浮率应同时生成调整浮率、运输成本和进口价格更新',
)
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(priceRows, { ...priceContainer, 汇率: 5, 运费: 9000 }, undefined),
  [
    { hguid: 'price-101', 调整浮率: 1, 运输成本: 0.75, 进口价格: 1.39 },
    { hguid: 'price-102', 调整浮率: 1.1, 运输成本: 0.75, 进口价格: 1.79 },
  ],
  '汇率或运费变化后应按每行现有调整浮率重算价格',
)
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(
    [
      { ...priceRows[0], 运输成本: 1, 进口价格: 1.7 },
      { ...priceRows[1], 运输成本: 1, 进口价格: 2.16 },
    ],
    priceContainer,
    undefined,
  ),
  [],
  '成本和进口价没有变化时不应生成无差异写库更新',
)
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(
    [{ id: 106, hguid: 'price-106', 调整浮率: 1, 进口价格: 8 }],
    { ...priceContainer, 汇率: 0 },
    1.2,
  ),
  [{ hguid: 'price-106', 调整浮率: 1.2, 运输成本: undefined, 进口价格: 8 }],
  '单行修改浮率时即使价格无法重算，也应生成浮率写库更新',
)
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], 装柜数量: 0, 运输成本: 9 }, priceContainer),
  9,
  '装柜数量为 0 时应保留原运输成本',
)
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], 装柜数量: -1, 运输成本: 9 }, priceContainer),
  9,
  '装柜数量为负数时应保留原运输成本',
)
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], 装柜数量: undefined, 运输成本: 9 }, priceContainer),
  9,
  '缺少装柜数量时应保留原运输成本',
)
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 103, hguid: 'price-103', 合计装柜体积: 1, 运输成本: 7 },
    { ...priceContainer, 总体积: 0 },
  ),
  7,
  '缺少可用总体积时应保留原运输成本',
)
assertEqual(
  calculateContainerDetailImportPrice(
    { id: 104, hguid: 'price-104', 国内价格: undefined, 进口价格: 8 },
    priceContainer,
    1.2,
    10,
  ),
  8,
  '缺少国内价格时应保留原进口价格',
)
assertDeepEqual(
  [4.99, 6.99, 8.99].map((itemRetailPrice) => calculateContainerSetCodePurchasePrice(6.1, itemRetailPrice, 20.97)),
  [1.45, 2.03, 2.62],
  '套装子项进货价应按子项价格占比从主商品进口价格分摊',
)
assertEqual(calculateContainerSetCodePurchasePrice(undefined, 4.99, 20.97), undefined, '缺少主商品进口价格时不自动分摊套装子项进货价')
assertEqual(calculateContainerSetCodePurchasePrice(0, 4.99, 20.97), undefined, '主商品进口价格为 0 时不自动分摊套装子项进货价')
assertEqual(calculateContainerSetCodePurchasePrice(6.1, 4.99, 0), undefined, '子项价格合计为 0 时不自动分摊套装子项进货价')
assertEqual(pageSource.includes('value={row.调整浮率}'), true, '调整浮率输入框应受控，批量应用后立即刷新显示')
assertEqual(pageSource.includes('defaultValue={row.调整浮率}'), false, '调整浮率输入框不能使用 defaultValue')
assertEqual(
  pageSource.includes('value={row.调整浮率}\n            keyboard={false}\n            precision={2}'),
  true,
  '调整浮率行内输入应保持 2 位小数',
)
assertEqual(pageSource.includes('renderNumericCell(formatNumber(row.调整浮率, 2))'), true, '调整浮率只读显示应保持 2 位小数')
assertEqual(
  pageSource.includes("<InputNumber value={batchFloatRate} placeholder={t('containers.fields.floatRate')} precision={2}"),
  true,
  '批量调整浮率输入应保持 2 位小数',
)
assertEqual(pageSource.includes('value={row.进口价格}'), true, '进口价格输入框应受控，批量应用后立即刷新显示')
assertEqual(pageSource.includes('defaultValue={row.进口价格}'), false, '进口价格输入框不能使用 defaultValue')
assertEqual(pageSource.includes('const updatePayload: UpdateContainerRequest'), true, '保存货柜头部应使用窄更新 payload')
assertEqual(pageSource.includes('await updateContainer(containerGuid, nextContainer)'), false, '保存货柜头部不能把完整货柜对象发送到后端')
assertEqual(pageSource.includes('buildContainerDetailFloatRateUpdates(rows,'), false, '保存货柜头部不能隐式批量重算全部明细成本')
assertEqual(pageSource.includes('shouldRecalculatePrices'), false, '成本重算必须通过手动入口触发，不能挂在保存头部流程')
assertEqual(pageSource.includes("t('containers.formulas.transportCost'"), true, '表格页脚运输成本公式应使用 i18n key')
assertEqual(pageSource.includes("t('containers.formulas.importPrice'"), true, '表格页脚进口价格公式应使用 i18n key')
assertEqual(pageSource.includes('运输成本 = 运费 × 明细体积 ÷ 装柜数量 ÷ 总体积'), true, '表格页脚应展示运输成本公式')
assertEqual(pageSource.includes('进口价格 = ((国内价格 ÷ 汇率 + 运输成本) × 调整浮率 × 10) ÷ 11'), true, '表格页脚应展示进口价格公式')
assertEqual(
  pageSource.includes('const [recalculateCostsLoading, setRecalculateCostsLoading] = useState(false)'),
  true,
  '页面应维护重算成本按钮的 loading 状态',
)
assertEqual(
  pageSource.includes('const handleRecalculateCosts = async () => {'),
  true,
  '页面应提供重算成本的手动处理入口',
)
assertEqual(
  pageSource.includes('recalculateContainerCostsByScope(containerGuid, buildDetailBatchScope())'),
  true,
  '重算成本应按当前筛选 scope 交给后端计算更新',
)
assertEqual(
  pageSource.includes('dataSource={displayRows}'),
  true,
  '货柜明细表格应使用列头过滤和排序后的 displayRows',
)
assertEqual(
  pageSource.includes('getCategoryTree') &&
    pageSource.includes('batchAssignProducts') &&
    pageSource.includes('buildWarehouseCategoryLookup') &&
    pageSource.includes('getWarehouseProductCategoryTooltip') &&
    pageSource.includes('formatWarehouseCategoryNodeName'),
  true,
  '货柜明细应加载分类树、复用分类路径 Tooltip helper 和国际化名称 helper，并调用批量分类服务',
)
assertEqual(
  pageSource.includes("const [categoryFilterValue, setCategoryFilterValue] = useState(CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY)") &&
    pageSource.includes('applyContainerDetailLoadedTextFilters(baseFilteredRows, itemNumberFilter, columnFilters)') &&
    pageSource.includes('applyContainerDetailCategoryFilter(textFilteredRows, categoryFilterValue, categoryLookup)') &&
    !pageSource.includes('categoryFilterValue, sortState') &&
    !pageSource.includes('categoryFilterValue, selectedTagFilters'),
  true,
  '顶部货号、列头文字和分类过滤应在前端过滤已加载行，不应进入远程 detailQuery 依赖',
)
assertEqual(
  pageSource.includes("placeholder={t('containers.filters.allCategories'") &&
    pageSource.includes('options={categoryFilterOptions}') &&
    pageSource.includes('setCategoryFilterValue(value || CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY)') &&
    pageSource.includes('buildContainerDetailCategoryOptions(categories, t, i18n.language)'),
  true,
  '货柜明细顶部应提供当前语言分类 Select，并支持清空回到全部分类',
)
assertEqual(
  pageSource.includes('filteredRows.length !== rows.length') &&
    pageSource.includes("t('containers.text.visibleRows'"),
  true,
  '分类过滤后应显示当前可见行数量，避免误解为后端总数变化',
)
assertEqual(
  pageSource.includes('buildContainerDetailQuery({') &&
    pageSource.includes('filters: remoteColumnFilters') &&
    pageSource.includes('sortState'),
  true,
  '非文本列头过滤、标签筛选和排序应继续转换为服务端查询条件',
)
assertEqual(
  !pageSource.includes('itemNumber: itemNumberFilter.trim() || columnFilters.itemNumber') &&
    pageSource.includes('const remoteColumnFilters = useMemo<ContainerDetailColumnFilters>(() => omitContainerDetailTextFilters(columnFilters), [columnFilters])'),
  true,
  '顶部货号和列头文字筛选不应合并进远程查询条件',
)
assertEqual(
  pageSource.includes('columnFilters') && pageSource.includes('sortState'),
  true,
  '页面应维护受控列头过滤和排序状态',
)
assertEqual(
  pageSource.includes('makeTextFilterDropdown') && pageSource.includes('makeNumberRangeFilterDropdown') && pageSource.includes('makeEnumFilterDropdown'),
  true,
  '列头应提供文本、数字范围和枚举三类过滤面板',
)
assertEqual(
  pageSource.includes("renderColumnTitle('itemNumber'") &&
    pageSource.includes("renderColumnTitle('barcode'") &&
    pageSource.includes("renderColumnTitle('productName'") &&
    pageSource.includes("renderColumnTitle('middlePackQuantity'") &&
    pageSource.includes("renderColumnTitle('containerQuantity'") &&
    pageSource.includes("renderColumnTitle('importPrice'") &&
    pageSource.includes("renderColumnTitle('warehouseStatus'"),
  true,
  '关键业务列应挂载列头排序或过滤配置',
)
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS = ['englishName', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark'] as const") &&
    pageSource.includes("patchRow(rowKey(row), { 中包数: value == null ? undefined : Number(value) })") &&
    pageSource.includes("saveRowPatch(row, { 中包数: event.target.value ? Number(event.target.value) : undefined })"),
  true,
  '中包数列应作为可编辑数字列保存到货柜明细更新接口',
)
assertEqual(
  pageSource.includes('function renderOemPriceCell(row: ContainerDetail)') &&
    pageSource.includes("formatCurrency(resolveContainerDetailOemPrice(row), '$')") &&
    pageSource.includes('function renderImportPriceCell(row: ContainerDetail, input?: ReactNode)') &&
    pageSource.includes('renderImportPriceCell(row, (') &&
    pageSource.includes(': renderImportPriceCell(row)') &&
    pageSource.includes("formatCurrency(v, '¥')") &&
    pageSource.includes("prefix=\"$\""),
  true,
  '价格列应按国内价格人民币、其它价格美元显示货币符号',
)
assertEqual(
  pageSource.includes("warehouseImportPrice > importPrice ? 'up' : 'down'") &&
    pageSource.includes("const Icon = trend === 'up' ? ArrowUpOutlined : ArrowDownOutlined") &&
    pageSource.includes("return <Icon className={className} />") &&
    pageSource.includes("saveRowPatch(row, { 进口价格: event.target.value ? Number(event.target.value) : undefined })") &&
    pageStyleSource.includes('.container-detail-import-price-trend-up') &&
    pageStyleSource.includes('color: #52c41a') &&
    pageStyleSource.includes('.container-detail-import-price-trend-down') &&
    pageStyleSource.includes('color: #ff4d4f'),
  true,
  '进口价格应对比仓库当前进货价格显示绿色向上或红色向下箭头，且保存字段仍为进口价格',
)
assertEqual(
  pageSource.includes("source === 'warehouse' ? 'container-detail-oem-price-cell-warehouse' : ''") &&
    pageSource.includes("source === 'detail' ? 'container-detail-oem-price-cell-fallback' : ''") &&
    pageSource.includes('className={getOemPriceSourceClassName(row)}') &&
    pageStyleSource.includes('.container-detail-oem-price-cell-warehouse') &&
    pageStyleSource.includes('background: #f6ffed') &&
    pageStyleSource.includes('.container-detail-oem-price-cell-fallback') &&
    pageStyleSource.includes('background: #fffbe6') &&
    pageStyleSource.includes('.container-detail-table .ant-input-number.container-detail-oem-price-cell-warehouse') &&
    pageStyleSource.includes('.container-detail-table .ant-input-number.container-detail-oem-price-cell-fallback'),
  true,
  '贴牌价格应按来源显示绿色仓库价和黄色明细兜底价，编辑输入框也应沿用同一来源底色',
)
assertEqual(
  pageSource.includes('handleWarehouseStatusChange'),
  true,
  '仓库状态列应支持行内编辑',
)
assertEqual(
  pageSource.includes('getContainerDetailProductCode(row)'),
  true,
  '页面应通过统一 helper 解析商品编码，避免空白编码绕过兜底',
)
assertEqual(
  pageSource.includes('getContainerDetailWarehouseActionFailureMessage(result'),
  true,
  '仓库状态更新应统一检查根失败和部分失败结果',
)
assertEqual(
  pageSource.includes('pendingWarehouseStatusCodes') &&
    pageSource.includes('loading={isWarehouseStatusPending}') &&
    pageSource.includes('disabled={!productCode || isWarehouseStatusPending}'),
  true,
  '行内仓库状态更新应显示提交中状态并阻止重复点击',
)
assertEqual(
  pageSource.includes('const previousStatuses = rows') &&
    pageSource.includes('setRows((items) => applyContainerDetailWarehouseStatusByProductCodes(items, [productCode], isActive))') &&
    pageSource.includes('rollbackContainerDetailWarehouseStatuses'),
  true,
  '行内仓库状态应先乐观更新，失败时回滚同商品编码行',
)
assertEqual(
  pageSource.includes('.filter((value) => !pendingWarehouseStatusCodes.has(value))') &&
    pageSource.includes("t('containers.messages.warehouseStatusUpdating'"),
  true,
  '批量上下架应跳过正在行内提交的商品编码，避免失败回滚覆盖批量成功状态',
)
assertEqual(
  pageSource.includes('applyContainerDetailWarehouseStatusByProductCodes(items, productCodes, isActive)') &&
    pageSource.includes('applyContainerDetailWarehouseStatusByProductCodes(items, [productCode], isActive)'),
  true,
  '批量和行内仓库状态更新都应复用同商品编码本地更新 helper',
)
assertEqual(
  pageSource.includes('applyContainerPricesByScope(containerGuid, buildDetailBatchScope()') &&
    pageSource.includes('const scopedRows = selectedRowKeys.length ? targetRows : await fetchAllRowsForCurrentQuery()') &&
    pageSource.includes('const productCodes = scopedRows'),
  true,
  '应用价格应使用服务端 scope，批量上下架未选择时应作用于当前筛选结果全量',
)
assertEqual(
  pageSource.includes('data-column-key="image"') || pageSource.includes('data-column-key="index"'),
  false,
  '图片和编号列不应添加无意义列头过滤配置',
)
assertEqual(
  pageSource.includes('buildContainerDetailFloatRateUpdates([row], container, value)'),
  true,
  '单行保存浮率应使用原始行计算变化，不能提前覆盖浮率导致不写库',
)
assertEqual(
  pageSource.includes('recalculateContainerCostsByScope(containerGuid, buildDetailBatchScope())'),
  true,
  '重算成本应通过服务端 scope 接口写回后端',
)
assertEqual(
  pageSource.includes("await loadDetailChunk(1, 'reset')"),
  true,
  '重算成本写回成功后应重载当前查询首块',
)
assertEqual(pageSource.includes('缺少运费，无法重算成本'), true, '缺少运费时应提示 warning 且不写库')
assertEqual(pageSource.includes('缺少总体积，无法重算成本'), true, '缺少总体积时应提示 warning 且不写库')
assertEqual(
  pageSource.includes("message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))"),
  true,
  '重算成本成功后应提示更新条数',
)
assertEqual(pageSource.includes('loading={recalculateCostsLoading}'), true, '重算成本按钮应绑定独立 loading 状态')
assertEqual(pageSource.includes('onClick={() => void handleRecalculateCosts()}'), true, '重算成本按钮应调用手动重算入口')
assertEqual(pageSource.includes('>重算成本</Button>'), true, '货柜明细应显示重算成本按钮文案')
assertEqual(
  pageSource.includes("renderColumnTitle('itemNumber', t('containers.fields.itemNumber'))") &&
    pageSource.includes("fixed: 'left'"),
  true,
  '货号列应固定在左侧，横向滚动时保持可见',
)
const defaultColumnOrderMarkers = [
  "key: 'index'",
  "key: 'image'",
  "renderColumnTitle('itemNumber'",
  "renderColumnTitle('englishName'",
  "key: 'categoryName'",
  "renderColumnTitle('containerPieces'",
  "renderColumnTitle('containerQuantity'",
  "renderColumnTitle('domesticPrice'",
  "renderColumnTitle('transportCost'",
  "renderColumnTitle('floatRate'",
  "renderColumnTitle('middlePackQuantity'",
  "renderColumnTitle('warehouseImportPrice'",
  "renderColumnTitle('importPrice'",
  "renderColumnTitle('oemPrice'",
  "renderColumnTitle('newProduct'",
  "renderColumnTitle('productType'",
  "renderColumnTitle('matchType'",
  "renderColumnTitle('barcode'",
  "renderColumnTitle('productName'",
  "renderColumnTitle('warehouseStatus'",
  "renderColumnTitle('remark'",
]
const defaultColumnOrderIndexes = defaultColumnOrderMarkers.map((marker) => pageSource.indexOf(marker))
assertEqual(
  defaultColumnOrderIndexes.every((index) => index >= 0) &&
    defaultColumnOrderIndexes.every((index, markerIndex) => markerIndex === 0 || index > defaultColumnOrderIndexes[markerIndex - 1]),
  true,
  '货柜明细默认列顺序应按截图排列',
)
const categoryColumnSource = pageSource.slice(
  pageSource.indexOf("key: 'categoryName'"),
  pageSource.indexOf("title: renderColumnTitle('containerPieces'"),
)
assertEqual(
  categoryColumnSource.includes("title: renderCompactHeader(t('containers.fields.category'") &&
    categoryColumnSource.includes('renderContainerDetailCategoryCell(row, categoryLookup, i18n.language)') &&
    pageSource.includes("const displayName = getContainerDetailCategoryName(record) || '--'"),
  true,
  '分类列应显示分类名称，Tooltip 使用完整路径 helper，缺失时显示 --',
)
const barcodeColumnSource = pageSource.slice(
  pageSource.indexOf("renderColumnTitle('barcode', t('containers.fields.barcode'))"),
  pageSource.indexOf("title: renderColumnTitle('productName'"),
)
assertEqual(
  barcodeColumnSource.includes("fixed: 'left'"),
  false,
  '条码列应按截图移动到后段，不再固定在左侧',
)
assertEqual(
  barcodeColumnSource.includes('showReadonlyOemPrice ? [readonlyOemPriceColumn] : []') &&
    pageSource.includes("const readonlyOemPriceColumn: ColumnsType<ContainerDetail>[number]") &&
    pageSource.includes('render: (_, row) => renderOemPriceCell(row)') &&
    !pageSource.slice(pageSource.indexOf('const readonlyOemPriceColumn'), pageSource.indexOf('const baseColumns')).includes("fixed: 'left'") &&
    !pageSource.slice(pageSource.indexOf('const readonlyOemPriceColumn'), pageSource.indexOf('const baseColumns')).includes('<InputNumber'),
  true,
  '条码列后应按开关插入只读贴牌价格列，便于横向滚动前快速核价',
)
assertEqual(
  pageSource.includes('rowSelection={{') &&
    pageSource.includes('selectedRowKeys,') &&
    pageSource.includes('onChange: setSelectedRowKeys,') &&
    pageSource.includes('fixed: !viewport.isSmallPortrait,') &&
    pageSource.includes("orderedBaseColumns.map((column) => ({ ...column, fixed: undefined }))"),
  true,
  '选择框列默认随左侧列固定，小屏竖屏时应随表格列一起取消固定',
)
assertEqual(
  pageSource.includes('key: field') &&
    pageSource.includes('sorter: true') &&
    pageSource.includes('sortOrder: sortState?.field === field ? sortState.order : null'),
  true,
  '可排序列应使用 AntD 稳定 key 作为排序字段标识，并保留 sorter 与 sortOrder',
)
assertEqual(
  pageSource.includes('isContainerDetailSortField(nextSortField)'),
  true,
  '表格排序回调应通过运行时白名单过滤 sorter 字段',
)
assertEqual(
  pageSource.includes('DndContext') &&
    pageSource.includes('SortableContext') &&
    pageSource.includes('useSortable') &&
    pageSource.includes('horizontalListSortingStrategy'),
  true,
  '货柜明细表头列拖拽应复用 @dnd-kit 横向排序能力',
)
assertEqual(
  pageSource.includes('components={{ header: { cell: DraggableHeaderCell } }}') &&
    pageSource.includes('<SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>') &&
    pageSource.includes('<DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>'),
  true,
  '货柜明细表格应接入可拖拽表头 cell 与横向 SortableContext',
)
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.containerDetail.columnOrder.v2'") &&
    pageSource.includes('localStorage.setItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY') &&
    pageSource.includes('mergeContainerDetailColumnOrder('),
  true,
  '货柜明细列顺序应保存到 v2 localStorage key，覆盖旧默认顺序并兼容列增删',
)
assertEqual(
  pageSource.includes("containers.actions.resetColumns") &&
    pageSource.includes('isContainerDetailColumnOrderCustomized(columnOrder, draggableColumnKeys)') &&
    pageSource.includes('setColumnOrder(draggableColumnKeys)') &&
    pageSource.includes('localStorage.removeItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY)'),
  true,
  '货柜明细手动拖拽列后应提供重置列按钮并清除本地列顺序',
)
assertEqual(
  pageSource.includes('const draggableColumnKeys = baseColumns.map((column) => String(column.key) as ContainerDetailTableColumnKey)') &&
    pageSource.includes('rowSelection={{') &&
    !pageSource.includes("columnOrder.includes('selection')"),
  true,
  '货柜明细选择列仍应由 rowSelection 管理，不能进入业务列拖拽顺序',
)
assertEqual(
  pageSource.includes("key: 'batchCategory'") &&
    pageSource.includes('const canBatchSetCategory = access.canEditContainer && access.canManagePosProducts') &&
    pageSource.includes('if (!canBatchSetCategory)') &&
    pageSource.includes('openBatchCategory()') &&
    pageSource.includes('handleBatchCategorySave') &&
    pageSource.includes('getContainerDetailBatchCategoryProductCodes(batchCategoryTargetRows)') &&
    pageSource.includes('batchAssignProducts(targetCategoryGuid, productCodes)'),
  true,
  '批量操作菜单应包含批量分类，并提交当前目标行的去重商品编码',
)
const batchCategorySaveSource = pageSource.slice(
  pageSource.indexOf('const handleBatchCategorySave = async () => {'),
  pageSource.indexOf('const submitBatchEditEnglishName = async () => {'),
)
assertEqual(
  batchCategorySaveSource.includes('await batchAssignProducts(targetCategoryGuid, productCodes)') &&
    !batchCategorySaveSource.includes("await loadDetailChunk(1, 'reset')") &&
    batchCategorySaveSource.includes('setRows((items) =>') &&
    batchCategorySaveSource.includes('const productCode = getContainerDetailProductCode(item)') &&
    batchCategorySaveSource.includes('productCodeSet.has(productCode)') &&
    batchCategorySaveSource.includes('WarehouseCategoryGUID: targetCategoryGuid') &&
    batchCategorySaveSource.includes('ProductCategoryGUID: targetCategoryGuid'),
  true,
  '货柜明细批量分类保存成功后应本地更新当前行分类，不应重新查询明细表格',
)
assertEqual(
    pageSource.includes("title={t('containers.modals.batchCategoryTitle'") &&
    pageSource.includes("t('warehouse.categories.targetCategory'") &&
    pageSource.includes('selectedTargetCategoryPath || formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)') &&
    pageSource.includes('import CategoryTreePicker') &&
    pageSource.includes('setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1))') &&
    pageSource.includes('<CategoryTreePicker') &&
    pageSource.includes('selectedKey={targetCategoryGuid}') &&
    pageSource.includes('maxHeight={360}'),
  true,
  '批量分类弹窗应使用带查询的当前语言分类树，并在每次打开时默认展开到一级分类',
)
assertEqual(pageSource.includes('className="container-detail-table"'), true, '货柜明细表格应挂载专属 class 以隔离垂直对齐样式')
assertEqual(
  pageSource.includes("rowClassName={(_, index) => index % 2 === 1 ? 'container-detail-row-striped' : ''}"),
  true,
  '货柜明细表格应按当前显示顺序给偶数视觉行添加隔行色 class',
)
assertEqual(
  pageSource.includes('const CONTAINER_DETAIL_PAGE_SIZE = 50') &&
    pageSource.includes('pagination={false}') &&
    pageSource.includes('virtual') &&
    pageSource.includes('onScroll={handleDetailTableScroll}'),
  true,
  '货柜明细应关闭可见分页器，使用 50 条内部懒加载块和虚拟滚动',
)
assertEqual(
  pageSource.includes('const [detailTableRenderKey, setDetailTableRenderKey] = useState(0)') &&
    pageSource.includes('const lastDetailTableScrollTopRef = useRef(0)') &&
    pageSource.includes('const wasContainerDetailTabActiveRef = useRef(active)') &&
    pageSource.includes('if (!active || wasActive || rows.length === 0)') &&
    pageSource.includes('window.requestAnimationFrame(() => {') &&
    pageSource.includes('setDetailTableRenderKey((value) => value + 1)') &&
    pageSource.includes('detailTableRef.current?.scrollTo?.({ top: scrollTop })') &&
    pageSource.includes('key={`${containerGuid}-${detailTableRenderKey}`}'),
  true,
  '货柜明细 Tab 切回时应重挂载 AntD 虚拟表格并恢复滚动位置，避免 KeepAlive 隐藏后 body 空白',
)
assertEqual(
  pageSource.includes('lastDetailTableScrollTopRef.current = target.scrollTop') &&
    pageSource.includes('target.scrollTop + target.clientHeight >= target.scrollHeight - 96') &&
    pageSource.includes('void loadNextDetailChunk()'),
  true,
  '货柜明细表格滚动处理应同时保存滚动位置并保留触底加载下一块',
)
assertEqual(
  pageStyleSource.includes('.container-detail-table .ant-table-thead > tr > th'),
  true,
  '货柜明细表头应通过专属样式保持垂直居中',
)
assertEqual(
  pageStyleSource.includes('.container-detail-table .ant-table-tbody > tr > td'),
  true,
  '货柜明细正文单元格应通过专属样式保持垂直居中',
)
assertEqual(
  pageStyleSource.includes('.container-detail-table .container-detail-row-striped:not(.ant-table-row-selected) > td'),
  true,
  '货柜明细隔行色应限定在专属表格内且不覆盖选中行',
)
assertEqual(
  pageStyleSource.includes('.container-detail-table .container-detail-row-striped:not(.ant-table-row-selected):hover > td'),
  true,
  '货柜明细隔行色应保留 hover 反馈',
)
assertEqual(
  pageSource.includes('className="container-detail-nowrap container-detail-copyable"'),
  true,
  '货号复制区域应使用专属 nowrap 样式保持单行紧凑显示',
)
assertEqual(
  pageSource.includes('className="container-detail-copy-button"'),
  true,
  '货号复制按钮应使用图标按钮样式，不显示复制文字',
)
assertEqual(
  pageSource.includes('className="container-detail-barcode-cell"'),
  true,
  '条码列应使用专属紧凑容器避免条码和复制按钮换行',
)
assertEqual(
  pageSource.includes('<BarcodePreview value={barcode} showText showCopy={false} options={{ height: 24 }} />'),
  true,
  '条码列应显示条码文本，不能隐藏条码文本',
)
assertEqual(
  pageSource.includes('Image,') &&
    pageSource.includes('<Image') &&
    pageSource.includes('className="container-detail-product-image"') &&
    pageSource.includes('preview={{ mask: t(\'containers.actions.previewImage\', \'查看大图\') }}'),
  true,
  '货柜明细商品图片应可点击放大预览',
)
assertEqual(
  pageStyleSource.includes('.container-detail-barcode-cell .ant-typography'),
  true,
  '条码文本应使用专属样式保持完整显示',
)
assertEqual(
  pageStyleSource.includes('overflow: visible'),
  true,
  '条码文本不应被省略或折叠隐藏',
)
assertEqual(
  pageSource.includes('className="container-detail-two-line-text"'),
  true,
  '商品名称应使用两行截断样式避免长名称撑高整行',
)
assertEqual(
  pageSource.includes('className="container-detail-english-name-input"'),
  true,
  '英文名称输入框应使用专属两行紧凑样式',
)
assertEqual(
  pageSource.includes('renderNumericCell('),
  true,
  '数字列应通过统一 helper 保持单行和等宽数字显示',
)
assertEqual(
  pageStyleSource.includes('-webkit-line-clamp: 2'),
  true,
  '名称和表头应通过 CSS 限制最多两行显示',
)
assertEqual(
  pageStyleSource.includes('font-variant-numeric: tabular-nums'),
  true,
  '数字列应使用等宽数字视觉以便扫读',
)
assertEqual(
  pageStyleSource.includes('.container-detail-table .ant-table-cell'),
  true,
  '货柜明细表格应压缩单元格 padding 提升密度',
)
assertEqual(
  pageStyleSource.includes('.container-detail-stat-tag-muted'),
  true,
  '未选中的统计标签应有弱化样式，保留颜色同时避免和选中态混淆',
)
assertEqual(
  pageSource.includes('const headerLoadRequestIdRef = useRef(0)') &&
    pageSource.includes('const containerDetailLoadRequestIdRef = useRef(0)') &&
    pageSource.includes('detailAbortControllerRef.current?.abort()'),
  true,
  '货柜详情头部和明细加载应分别使用 request id 与 AbortController 防止旧请求覆盖新页面',
)
assertEqual(
  pageSource.includes('if (headerLoadRequestIdRef.current !== currentRequestId)') &&
    pageSource.includes('if (controller.signal.aborted || containerDetailLoadRequestIdRef.current !== currentRequestId)') &&
    pageSource.includes('return'),
  true,
  '货柜详情过期请求完成或失败后应直接忽略，不能写入 state 或弹失败提示',
)
assertEqual(
  pageSource.includes("const errorMessage = error instanceof Error ? error.message : t('containers.messages.loadDetailFailed')") &&
    pageSource.includes('if (showLoading) {') &&
    pageSource.includes('message.error(errorMessage)') &&
    pageSource.includes('} else {') &&
    pageSource.includes("console.error('货柜详情静默刷新失败', error)"),
  true,
  '货柜详情静默刷新失败应保留当前内容，不弹明显失败提示；首次加载失败才展示错误',
)
assertEqual(
  pageSource.includes("const [containerGuid] = useState(() => route?.params.containerGuid || '')"),
  false,
  '货柜 GUID 不能用 useState 固定首次路由参数，移动端切换详情时必须跟随当前 URL',
)
assertEqual(
  pageSource.includes('移动端布局可能复用 route element，货柜 GUID 必须每次跟随当前 URL'),
  true,
  '货柜详情应有中文注释说明移动端 route element 复用时 GUID 必须跟随当前 URL',
)
assertEqual(
  pageSource.includes('const lastLoadedContainerDetailSuccessRef = useRef<{ containerGuid: string; queryKey: string } | null>(null)') &&
    pageSource.includes('lastLoadedContainerDetailSuccessRef.current = { containerGuid, queryKey: detailQueryKey }') &&
    pageSource.includes('loadedDetailQueryKey: lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid') &&
    pageSource.includes('lastLoadedContainerDetailSuccessRef.current?.queryKey') &&
    !pageSource.includes('loadedDetailQueryKey: lastLoadedContainerDetailQueryKeyRef.current'),
  true,
  '明细自动跳过判断只能使用明细成功加载记录，不能沿用头部加载状态或旧查询 key',
)
assertEqual(
  mobileLayoutSource.includes('<div className="mobile-content" key={location.pathname}>'),
  true,
  '移动端布局应按 pathname 重建当前页面，避免不同货柜详情复用同一个组件实例',
)
assertEqual(
  pageSource.includes('renderProductTypeTag(row)') &&
    pageSource.includes('container-detail-product-type-tag-clickable') &&
    pageSource.includes("productType === '套装商品'") &&
    pageSource.includes('openSetCodeModal(row)'),
  true,
  '货柜明细商品类型列应使用彩色 Tag，套装商品 Tag 应可点击打开套装多码弹窗',
)
assertEqual(
  pageSource.includes('getContainerDomesticSetCodes(productCode, abortController.signal)') &&
    pageSource.includes('setCodeAbortControllerRef.current?.abort()') &&
    pageSource.includes('updateContainerDomesticSetCodePrices(productCode, changedSetCodePriceItems)'),
  true,
  '套装多码弹窗应支持中止旧请求，并通过国内套装价格接口保存变更',
)
assertEqual(
  pageSource.includes('const mainPurchasePrice = setCodeModalRow?.进口价格') &&
    pageSource.includes('calculateContainerSetCodePurchasePrice(mainPurchasePrice, nextRetailPrice, totalRetailPrice)') &&
    !pageSource.includes('setCodeModalRow?.warehouseImportPrice'),
  true,
  '套装子项进货价应按货柜明细当前行进口价格分摊，不能使用仓库当前进货价',
)
assertEqual(
  pageStyleSource.includes('.container-detail-product-type-tag-clickable'),
  true,
  '可点击商品类型 Tag 应有专属样式提示可操作',
)

console.log('containerDetailLogic.test: ok')
