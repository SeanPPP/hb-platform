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
  CONTAINER_DETAIL_EXPORT_COLUMNS,
  CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY,
  applyContainerDetailEnglishNameUpdates,
  applyContainerDetailWarehouseStatusByProductCodes,
  applyContainerDetailCategoryFilter,
  applyContainerDetailLoadedTextFilters,
  applyContainerDetailColumnState,
  buildContainerDetailQuery,
  calculateContainerDetailTableScrollY,
  buildContainerDetailClearEnglishNameUpdates,
  buildContainerDetailDetectionItems,
  buildContainerDetailEnglishNameUpdates,
  buildContainerDetailMatchedDomesticDataUpdates,
  buildContainerDetailMatchStatusUpdates,
  canUseContainerDetailLocalTagFilters,
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
  calculateContainerDetailUnitTransportCost,
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
  getContainerDetailReadonlyOemPrice,
  getContainerDetailImageUrl,
  getContainerDetailImportPriceTrend,
  getContainerDetailRealtimeImportPrice,
  getContainerDetailRealtimeRetailPrice,
  getContainerDetailVisibleOemPrice,
  getContainerDetailOemPriceSource,
  getContainerDetailTranslationSource,
  getContainerDetailWarehouseActionFailureMessage,
  getContainerDetailEditableColumnKeysInOrder,
  getContainerDetailExportColumns,
  getNextContainerDetailEditableCell,
  getNextUpdateFieldSelection,
  isContainerDetailSortField,
  matchesContainerDetailSelectedTags,
  matchesContainerDetailTagFilter,
  normalizeContainerDetailPushToHqPayload,
  resolveContainerDetailOemPrice,
  DEFAULT_CONTAINER_DETAIL_FLOAT_RATE,
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
  DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS,
  getContainerDetailCostMissingFields,
  getUpdateFieldSelectionState,
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
assertEqual(
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS.some((key) => key === 'barcodeImage' || key === 'productImage'),
  false,
  '货柜明细默认导出列不应包含图片列，避免默认导出变慢',
)

const updateFieldOptions = ['importPrice', 'oemPrice', 'storeRetailPrice'] as const
assertDeepEqual(
  getUpdateFieldSelectionState(['importPrice'], updateFieldOptions),
  { isAllSelected: false, isPartiallySelected: true },
  '字段选择器部分勾选时应显示半选态',
)
assertDeepEqual(
  getUpdateFieldSelectionState([...updateFieldOptions], updateFieldOptions),
  { isAllSelected: true, isPartiallySelected: false },
  '字段选择器全部勾选时应显示全选态',
)
assertDeepEqual(
  getUpdateFieldSelectionState([], updateFieldOptions),
  { isAllSelected: false, isPartiallySelected: false },
  '字段选择器未勾选时不应显示全选或半选态',
)
assertDeepEqual(
  getNextUpdateFieldSelection(true, updateFieldOptions),
  [...updateFieldOptions],
  '字段选择器点击全选时应选中全部字段',
)
assertDeepEqual(
  getNextUpdateFieldSelection(false, updateFieldOptions),
  [],
  '字段选择器取消全选时应清空字段',
)
assertDeepEqual(
  DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS,
  ['index', 'productImage', 'itemNumber', 'barcodeImage', 'englishName', 'oemPrice'],
  '货柜明细 PDF 默认导出列应为序号、商品图片、货号、条码图片、英文和零售价',
)

const customExportColumnKeys: ContainerDetailExportColumnKey[] = ['oemPrice', 'itemNumber', 'containerQuantity']
assertDeepEqual(
  getContainerDetailExportColumns(customExportColumnKeys).map((column) => column.key),
  ['oemPrice', 'itemNumber', 'containerQuantity'],
  '货柜明细自定义导出列应按用户选择顺序输出',
)
assertDeepEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS
    .filter((column) => column.key === 'lastImportPrice' || column.key === 'lastOEMPrice')
    .map((column) => column.key),
  ['lastImportPrice', 'lastOEMPrice'],
  '货柜明细可选导出列应包含实时进货价和实时零售价',
)
assertDeepEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS
    .filter((column) => column.key === 'barcode' || column.key === 'barcodeImage' || column.key === 'productImage')
    .map((column) => column.key),
  ['barcode', 'barcodeImage', 'productImage'],
  '货柜明细可选导出列应包含条码、条码图片和商品图片',
)
assertEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS.find((column) => column.key === 'lastImportPrice')?.labelKey,
  'containers.fields.warehouseImportPrice',
  '实时进货价导出列应复用表格里的实时进货价翻译 key',
)
assertEqual(
  getContainerDetailImportPriceTrend({ id: 120, hguid: 'import-trend-up', warehouseImportPrice: 0.29, 进口价格: 0.38 }),
  'up',
  '本次进口价格高于实时进货价时应显示绿色上涨',
)
assertEqual(
  getContainerDetailImportPriceTrend({ id: 121, hguid: 'import-trend-down', warehouseImportPrice: 0.38, 进口价格: 0.29 }),
  'down',
  '本次进口价格低于实时进货价时应显示红色下降',
)
assertEqual(
  getContainerDetailImportPriceTrend({ id: 122, hguid: 'import-trend-same', warehouseImportPrice: 0.29, 进口价格: 0.29 }),
  undefined,
  '本次进口价格等于实时进货价时不应显示趋势箭头',
)
assertEqual(
  getContainerDetailImportPriceTrend({ id: 123, hguid: 'import-trend-missing-realtime', 进口价格: 0.38 }),
  undefined,
  '缺少实时进货价时不应显示趋势箭头',
)
assertEqual(
  getContainerDetailImportPriceTrend({ id: 124, hguid: 'import-trend-missing-current', warehouseImportPrice: 0.38 }),
  undefined,
  '缺少本次进口价格时不应显示趋势箭头',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 768,
    toolbarHeight: 128,
    tableChromeHeight: 88,
    isSmallLandscape: false,
    isSmallPortrait: false,
    maxScrollY: 620,
  }),
  366,
  '桌面表格高度应扣除工具栏和表格头尾，并在顶部低于工作区时使用下限',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 1200,
    toolbarHeight: 120,
    tableChromeHeight: 92,
    isSmallLandscape: false,
    isSmallPortrait: false,
    maxScrollY: 620,
  }),
  620,
  '桌面空间充足时表格高度应保持最大上限',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 994,
    toolbarHeight: 165,
    tableChromeHeight: 130,
    isSmallLandscape: false,
    isSmallPortrait: false,
    maxScrollY: 620,
  }),
  513,
  '桌面滚动后仍应使用稳定工作区顶部计算，避免滚动中改变表格高度',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 994,
    toolbarHeight: 226,
    tableChromeHeight: 130,
    isSmallLandscape: false,
    isSmallPortrait: true,
    maxScrollY: 620,
  }),
  468,
  '窄屏滚动后仍应使用稳定工作区顶部计算，避免滚动中改变表格高度',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 994,
    toolbarHeight: 274,
    tableChromeHeight: 120,
    isSmallLandscape: false,
    isSmallPortrait: true,
    maxScrollY: 620,
  }),
  430,
  '678 宽窄屏 web 视口应按稳定工作区 top 计算表格 body，避免滚动中重算高度',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 820,
    toolbarHeight: 220,
    tableChromeHeight: 124,
    isSmallLandscape: false,
    isSmallPortrait: true,
    maxScrollY: 620,
  }),
  306,
  '窄屏空数据场景应按稳定工作区 top 计算表格 body，保留顺滑滚动',
)
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 430,
    toolbarHeight: 168,
    tableChromeHeight: 84,
    isSmallLandscape: true,
    isSmallPortrait: false,
    maxScrollY: 620,
  }),
  96,
  '小屏横屏表格高度不足时应只保留可操作硬下限',
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
  lastImportPrice: 8.88,
  lastOEMPrice: 9.99,
  单件体积: 0.1188,
  合计装柜体积: 0.3564,
  warehouseIsActive: true,
  备注: '优先上架',
  商品信息: {
    货号: 'HB291-005',
    条形码: '9525812910005',
    商品名称: '商品信息名称',
    英文名称: 'Product Info Name',
    商品图片: 'https://cdn.example.com/info-image.jpg',
    商品类型: '套装商品',
    零售价格: 9.99,
  },
})
assertDeepEqual(
  {
    itemNumber: exportRow.itemNumber,
    barcode: exportRow.barcode,
    barcodeImage: exportRow.barcodeImage,
    productImage: exportRow.productImage,
    productName: exportRow.productName,
    englishName: exportRow.englishName,
    containerPieces: exportRow.containerPieces,
    containerQuantity: exportRow.containerQuantity,
    unitVolume: exportRow.unitVolume,
    totalVolume: exportRow.totalVolume,
    lastImportPrice: exportRow.lastImportPrice,
    lastOEMPrice: exportRow.lastOEMPrice,
    oemPrice: exportRow.oemPrice,
  },
  {
    itemNumber: 'HB291-005',
    barcode: '9525812910005',
    barcodeImage: '9525812910005',
    productImage: 'https://cdn.example.com/info-image.jpg',
    productName: '明细名称',
    englishName: 'Detail Name',
    containerPieces: 3,
    containerQuantity: 720,
    unitVolume: 0.1188,
    totalVolume: 0.3564,
    lastImportPrice: 0.52,
    lastOEMPrice: 4.8,
    oemPrice: 3.5,
  },
  '货柜明细导出行应按 Excel 模板读取页面展示字段、体积字段和实时仓库价，且新商品零售价应使用明细业务价',
)

assertEqual(
  resolveContainerDetailOemPrice({ id: 103, hguid: 'oem-warehouse', 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }),
  2.2,
  '纯明细零售价 helper 应只读取货柜明细业务价',
)
assertEqual(
  getContainerDetailVisibleOemPrice({ id: 104, hguid: 'visible-oem-existing', 是否新商品: false, 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }),
  6.6,
  '已有商品表格零售价应读取仓库实时零售价',
)
assertEqual(
  getContainerDetailVisibleOemPrice({ id: 105, hguid: 'visible-oem-new', 是否新商品: true, 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }),
  2.2,
  '新商品表格零售价应读取货柜明细零售价',
)
assertEqual(
  buildContainerDetailExportRow({ id: 106, hguid: 'export-existing-oem', 是否新商品: false, 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }).oemPrice,
  6.6,
  '已有商品导出零售价应使用仓库实时零售价',
)
assertEqual(
  getContainerDetailRealtimeRetailPrice({ id: 107, hguid: 'retail-warehouse-camel', 贴牌价格: 2.2, warehouseOEMPrice: 6.6, LastOEMPrice: 7.7 }),
  6.6,
  '实时零售价应读取仓库商品 camelCase 字段',
)
assertEqual(
  getContainerDetailRealtimeRetailPrice({ id: 108, hguid: 'retail-warehouse-pascal', 贴牌价格: 2.2, WarehouseOEMPrice: 7.7, LastOEMPrice: 8.8 }),
  7.7,
  '实时零售价应兼容后端 PascalCase 字段',
)
assertEqual(
  getContainerDetailRealtimeRetailPrice({ id: 109, hguid: 'retail-warehouse-none', 贴牌价格: 2.2, LastOEMPrice: 8.8 }),
  undefined,
  '实时零售价缺字段时不应回退历史快照或明细价',
)
assertEqual(
  getContainerDetailRealtimeImportPrice({ id: 110, hguid: 'import-warehouse-camel', warehouseImportPrice: 5.5, LastImportPrice: 8.8 }),
  5.5,
  '实时进货价应读取仓库商品 camelCase 字段',
)
assertEqual(
  getContainerDetailRealtimeImportPrice({ id: 111, hguid: 'import-warehouse-pascal', WarehouseImportPrice: 6.6, LastImportPrice: 8.8 }),
  6.6,
  '实时进货价应兼容后端 PascalCase 字段',
)
assertEqual(
  getContainerDetailRealtimeImportPrice({ id: 112, hguid: 'import-warehouse-none', LastImportPrice: 8.8 }),
  undefined,
  '实时进货价缺字段时不应回退历史快照',
)
assertEqual(
  getContainerDetailOemPriceSource({ id: 106, hguid: 'oem-source-warehouse', 贴牌价格: 2.2, warehouseOEMPrice: 6.6 }),
  'detail',
  '零售价来源应识别明细零售价',
)
assertEqual(
  getContainerDetailOemPriceSource({ id: 107, hguid: 'oem-source-detail', 贴牌价格: 2.2, warehouseOEMPrice: 0 }),
  'detail',
  '零售价来源应识别明细业务价',
)
assertEqual(
  getContainerDetailOemPriceSource({ id: 108, hguid: 'oem-source-none' }),
  'none',
  '零售价来源应识别无价格状态',
)
assertEqual(
  getContainerDetailReadonlyOemPrice({ id: 109, hguid: 'readonly-oem-camel', readonlyOemPrice: 6.6, 贴牌价格: 2.2 }),
  6.6,
  '只读零售价应读取后端 camelCase 字段',
)
assertEqual(
  getContainerDetailReadonlyOemPrice({ id: 110, hguid: 'readonly-oem-pascal', ReadonlyOemPrice: 7.7, 贴牌价格: 2.2 }),
  7.7,
  '只读零售价应兼容后端 PascalCase 字段',
)
assertEqual(
  getContainerDetailReadonlyOemPrice({ id: 111, hguid: 'readonly-oem-none', 贴牌价格: 2.2 }),
  undefined,
  '只读零售价缺字段时不应回退货柜明细业务价',
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
      barcode: '',
      barcodeImage: '',
      productImage: '',
      productName: '',
      englishName: '',
      containerPieces: 0,
      containerQuantity: 0,
      unitVolume: 0,
      totalVolume: 0,
      middlePackQuantity: 0,
      domesticPrice: 0,
      lastImportPrice: 0,
      lastOEMPrice: 0,
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
const defaultPageColumnOrder: ContainerDetailTableColumnKey[] = [
  'index',
  'image',
  'itemNumber',
  'englishName',
  'categoryName',
  'containerPieces',
  'packingQuantity',
  'containerQuantity',
  'unitVolume',
  'domesticPrice',
  'transportCost',
  'unitTransportCost',
  'floatRate',
  'middlePackQuantity',
  'warehouseImportPrice',
  'importPrice',
  'oemPrice',
  'lastOEMPrice',
  'newProduct',
  'productType',
  'matchType',
  'barcode',
  'productName',
  'warehouseStatus',
  'remark',
]
const editableColumnWhitelist = ['englishName', 'packingQuantity', 'unitVolume', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark'] as const
const editableColumnKeys = getContainerDetailEditableColumnKeysInOrder(defaultPageColumnOrder, editableColumnWhitelist)

assertDeepEqual(
  editableColumnKeys,
  ['englishName', 'packingQuantity', 'unitVolume', 'floatRate', 'middlePackQuantity', 'importPrice', 'oemPrice', 'remark'],
  '默认页面列顺序应决定方向键可编辑列顺序',
)
assertDeepEqual(
  getContainerDetailEditableColumnKeysInOrder(
    ['remark', 'warehouseStatus', 'oemPrice', 'floatRate', 'englishName'],
    editableColumnWhitelist,
  ),
  ['remark', 'oemPrice', 'floatRate', 'englishName'],
  '自定义列顺序应决定方向键可编辑列顺序',
)

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
  { rowKey: 'row-2', columnKey: 'unitVolume' },
  '方向键左应移动到同一行页面顺序的前一个编辑列',
)
assertDeepEqual(
  getNextContainerDetailEditableCell('row-2', 'floatRate', editableRowKeys, editableColumnKeys, 'right'),
  { rowKey: 'row-2', columnKey: 'middlePackQuantity' },
  '方向键右应移动到同一行页面顺序的后一个编辑列',
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
    noOemPrice: 2,
    abnormalImport: 2,
    active: 2,
    inactive: 2,
    normal: 1,
    set: 1,
    multi: 1,
    setChild: 1,
  },
  '统计栏应按当前基础结果统计全部、新商品、已有商品、缺零售价、进口价异常、上下架和商品类型数量',
)
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'new'), true, '新商品 tag 应匹配是否新商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'new'), false, '新商品 tag 不应匹配已有商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'noOemPrice'), true, '缺零售价只统计新商品且有效零售价为空或不大于 0 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[1], 'noOemPrice'), true, '明细零售价为空时应进入缺零售价 tag，仓库快照不再兜底')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'noOemPrice'), false, '已有商品缺零售价不进入缺零售价 tag')
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
    warehouseOEMPrice: 2.01,
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
assertDeepEqual(columnState({ oemPrice: { min: 4, max: 5 } }), ['column-201'], '零售价列头范围过滤应读取表格可见零售价')
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
    sortBy: 'itemNumber',
    sortOrder: 'ascend',
  },
  '远程查询参数应由列筛选、排序和分页状态生成，并且不再提交标签字段',
)

assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: 'CONTAINER-NO-SORT',
    filters: { barcode: ' 9300 ' },
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
    containerGuid: 'CONTAINER-APPEND',
    filters: {},
    pageNumber: 2,
    pageSize: 50,
    includeTotal: false,
    includeStats: false,
  }),
  {
    containerGuid: 'CONTAINER-APPEND',
    pageNumber: 2,
    pageSize: 50,
    includeTotal: false,
    includeStats: false,
  },
  '追加页查询应允许显式跳过 total 和标签统计',
)

assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: 'CONTAINER-TYPE-TAGS',
    filters: { productTypes: ['normal'] },
    pageNumber: 1,
    pageSize: 50,
  }),
  {
    containerGuid: 'CONTAINER-TYPE-TAGS',
    pageNumber: 1,
    pageSize: 50,
    productTypes: ['normal'],
  },
  '商品类型统计 tag 不再合并到远程 productTypes，只有列头商品类型筛选进入后端',
)

assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: 'base-query',
    baseQueryKey: 'base-query',
    loadedRowsLength: 83,
    itemsTotal: 83,
    hasMore: false,
    loading: false,
    loadingMore: false,
  }),
  true,
  '当前非标签查询已全量加载时应允许前端标签过滤',
)
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: 'base-query',
    baseQueryKey: 'base-query',
    loadedRowsLength: 50,
    itemsTotal: 83,
    hasMore: true,
    loading: false,
    loadingMore: false,
  }),
  false,
  '仍有下一页时不能前端标签过滤，必须由后端兜底',
)
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: 'scoped-query',
    baseQueryKey: 'base-query',
    loadedRowsLength: 12,
    itemsTotal: 12,
    hasMore: false,
    loading: false,
    loadingMore: false,
  }),
  false,
  '最近加载的是带标签查询时不能当作 base 全量结果做本地标签切换',
)
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: 'base-query',
    baseQueryKey: 'base-query',
    loadedRowsLength: 82,
    itemsTotal: 83,
    hasMore: false,
    loading: false,
    loadingMore: false,
  }),
  false,
  '已加载数量小于总数时不能前端标签过滤',
)
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: 'base-query',
    baseQueryKey: 'base-query',
    loadedRowsLength: 83,
    itemsTotal: 83,
    hasMore: false,
    loading: true,
    loadingMore: false,
  }),
  false,
  '明细加载中不能前端标签过滤，避免使用半截数据',
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
    { hguid: 'price-322', label: 'HB137-483', retailPrice: 0 },
  ],
  '创建新商品前应只拦截新商品中明细零售价不大于 0 的明细，上次零售价不再兜底',
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
const tagFiltersSource = readFileSync('src/pages/Warehouse/ContainerDetail/ContainerTagFilters.tsx', 'utf8')
const columnsSource = readFileSync('src/pages/Warehouse/ContainerDetail/ContainerDetailColumns.tsx', 'utf8')
const setCodeHookSource = readFileSync('src/pages/Warehouse/ContainerDetail/useContainerSetCode.tsx', 'utf8')
const pageStyleSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.css', 'utf8')
const mobileLayoutSource = readFileSync('src/layout/MobileLayout.tsx', 'utf8')
const containerDetailLogicSource = readFileSync('src/pages/Warehouse/ContainerDetail/containerDetailLogic.ts', 'utf8')
const warehouseProductServiceSource = readFileSync('src/services/warehouseProductService.ts', 'utf8')
const zhLocale = JSON.parse(readFileSync('src/i18n/locales/zh.json', 'utf8'))
const enLocale = JSON.parse(readFileSync('src/i18n/locales/en.json', 'utf8'))
assertEqual(
  pageSource.includes('PDF 对外分享时不展示汇率和运费金额；Excel 仍保留完整核对信息。') &&
    pageSource.includes("const summaryRows: NonNullable<ContainerExportOptions['summary']>['rows'] = format === 'pdf'") &&
    pageSource.includes(': baseSummaryRows'),
  true,
  '货柜明细 PDF 基础信息不应展示汇率和运费金额，Excel 应保留完整摘要',
)
assertEqual(
  pageSource.includes("const containerDetailTabKey = containerGuid ? `/warehouse/container/detail/${containerGuid}` : undefined") &&
    pageSource.includes('if (!active || !containerDetailTabKey || !containerDetailTabTitle)') &&
    pageSource.includes('updateTabTitle(containerDetailTabKey, containerDetailTabTitle)') &&
    !pageSource.includes('useDynamicTabTitle(container?.货柜编号'),
  true,
  '货柜明细 Tab 标题只应由当前 active 的 KeepAlive 实例更新，避免旧实例跟随全局 URL 改错标签',
)

function getLocaleValue(source: Record<string, unknown>, key: string) {
  return key.split('.').reduce<unknown>((current, part) => (
    current && typeof current === 'object' ? (current as Record<string, unknown>)[part] : undefined
  ), source)
}

const containerDetailExportLabelKeys = CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => column.labelKey)

const requiredContainerI18nKeys = [
  'containers.actions.batchUpdateFloatRate',
  'containers.actions.batchUpdatePrices',
  'containers.actions.showReadonlyOemPrice',
  'containers.actions.pushToHq',
  'containers.actions.saveDetails',
  'containers.actions.matchDomesticData',
  'containers.actions.alignDomesticProductCode',
  'containers.actions.previewImage',
  'containers.text.loadedRows',
  'containers.text.warehouseInventoriesCreated',
  'containers.text.warehouseInventoriesUpdated',
  'containers.text.skippedNewProducts',
  'containers.text.missingProductCodeRows',
  'containers.text.pushToHqSelectedLocalProducts',
  'containers.text.moreCreateProductResultItems',
  'containers.text.createProductsJobSummary',
  'containers.text.skippedRows',
  'containers.text.failedRows',
  'containers.modals.savePendingPriceDetailsTitle',
  'containers.modals.savePendingPriceDetailsUpdateTitle',
  'containers.modals.savePendingPriceDetailsSummary',
  'containers.modals.savePendingPriceDetailsExistingRetailHint',
  'containers.modals.savePendingPriceDetailsNewRetailHint',
  'containers.modals.savePendingPriceDetailsRetryHint',
  'containers.messages.selectedRowsHidden',
  'containers.messages.savePendingPriceDetailsFirst',
  'containers.messages.detailSaveFailed',
  'containers.messages.noPendingPriceDetails',
  'containers.messages.detailPricesSaved',
  'containers.messages.selectBatchProducts',
  'containers.messages.noMatchableDetails',
  'containers.messages.missingMatchableProductIdentity',
  'containers.messages.noDomesticDataToUpdate',
  'containers.messages.domesticDataMatched',
  'containers.messages.columnOrderReset',
  'containers.messages.matchDomesticDataFailed',
  'containers.messages.missingAlignProductCode',
  'containers.messages.domesticProductCodeAligned',
  'containers.messages.alignDomesticProductCodeFailed',
  'containers.messages.alignSetChildNotSupported',
  'containers.messages.rowCategoryUpdated',
  'containers.messages.noExistingLocalProductsToPushHq',
  'containers.messages.createProductsJobSubmitted',
  'containers.messages.createProductsJobFailed',
  'containers.messages.createProductsJobPartialSucceeded',
  'containers.messages.createProductsJobSucceeded',
  'containers.messages.createProductFailed',
  'containers.messages.purchasePricesUpdateFailed',
  'containers.messages.newProductCannotToggleWarehouseStatus',
  'containers.messages.newProductsSkippedForWarehouseStatus',
  'containers.modals.batchUpdateFloatRateTitle',
  'containers.modals.batchUpdatePricesTitle',
  'containers.modals.batchActionContent',
  'containers.modals.batchActionAllHint',
  'containers.modals.alignDomesticProductCodeTitle',
  'containers.modals.alignDomesticProductCodeContent',
  'containers.modals.alignDomesticProductCodeConflictHint',
  'containers.modals.rowCategoryTitle',
  'containers.export.summaryTitle',
  ...containerDetailExportLabelKeys,
  'containers.setCode.pricesTitle',
  'containers.setCode.missingProductCode',
  'containers.setCode.loadFailed',
  'containers.setCode.saveSuccess',
  'containers.setCode.saveFailed',
  'containers.setCode.itemNumber',
  'containers.setCode.barcode',
  'containers.setCode.retailPrice',
  'containers.setCode.purchasePrice',
  'warehouse.categories.selectTargetCategory',
  'warehouse.categories.batchAssignFailed',
  'posAdmin.products.noManagePermission',
  'posAdmin.products.pushToHqAffectedRows',
  'posAdmin.products.productsAdded',
  'posAdmin.products.productsUpdated',
  'posAdmin.products.storeRetailPricesUpdated',
  'posAdmin.products.storeMultiCodesUpdated',
  'posAdmin.products.pushToHqResult',
  'posAdmin.products.pushToHqFailed',
  'posAdmin.products.pushToHqPartialSucceeded',
  'posAdmin.products.pushToHqSucceeded',
]

assertDeepEqual(
  requiredContainerI18nKeys.filter((key) => !getLocaleValue(zhLocale, key) || !getLocaleValue(enLocale, key)),
  [],
  '货柜明细新增可见文案应同时补齐中英文 locale，避免英文模式回退中文兜底',
)
assertEqual(
  pageSource.includes('const newProductCount = scopedRows.filter((row) => row.是否新商品).length') &&
    pageSource.includes('const eligibleRows = scopedRows.filter((row) => !row.是否新商品)') &&
    pageSource.includes("message.warning(t('containers.messages.newProductsSkippedForWarehouseStatus'") &&
    pageSource.includes('const productCodes = eligibleRows\n      .map(getContainerDetailProductCode)') &&
    pageSource.includes('eligibleRows.some((row) => {'),
  true,
  '批量上下架应跳过新商品，只把已有商品编码提交到仓库商品上下架接口',
)
assertEqual(
  pageSource.includes('if (!eligibleRows.length)') &&
    pageSource.includes("message.warning(t('containers.messages.newProductCannotToggleWarehouseStatus', '新商品请先创建后再上下架'))"),
  true,
  '批量上下架目标全是新商品时应只提示，不请求后端接口',
)
assertEqual(
  pageSource.includes('const handleWarehouseStatusChange = async (row: ContainerDetail, isActive: boolean) => {') &&
    pageSource.includes("if (row.是否新商品) {\n      message.warning(t('containers.messages.newProductCannotToggleWarehouseStatus', '新商品请先创建后再上下架'))\n      return\n    }\n    const productCode = getContainerDetailProductCode(row)"),
  true,
  '单行仓库状态切换应先拦截新商品，避免用不存在的仓库商品编码调用上下架接口',
)
assertEqual(
  pageSource.includes('const warehouseStatusDisabledMessage = isWarehouseStatusPending') &&
    pageSource.includes("? t('containers.messages.newProductCannotToggleWarehouseStatus', '新商品请先创建后再上下架')") &&
    pageSource.includes('<Tooltip title={warehouseStatusDisabledMessage}>') &&
    pageSource.includes('disabled={row.是否新商品 || !productCode || isWarehouseStatusPending}'),
  true,
  '新商品行的仓库状态开关应禁用，并显示先创建再上下架提示',
)
assertDeepEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => getLocaleValue(enLocale, column.labelKey)),
  [
    'No.',
    'Item No.',
    'Barcode',
    'Barcode Image',
    'Product Image',
    'Chinese Name',
    'English Name',
    'Pieces',
    'Total Qty',
    'Unit Volume',
    'Total Volume',
    'INNER',
    'RMB Cost',
    'Current Purchase Price',
    'Current Retail Price',
    'RRP',
  ],
  '英文模式导出列选择弹窗和 Excel 表头应全部使用英文 locale，不能回退到中文 fallback',
)

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
  '只读零售价快览列应默认关闭',
)
assertEqual(
  pageSource.includes('showReadonlyOemPrice ? [readonlyOemPriceColumn] : []'),
  true,
  '只读零售价快览列应只在开关打开时插入表格列',
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
    { id: 902, hguid: 'detect-902', 商品编码: 'P-CODE', localSupplierCode: 'SUP01', 商品信息: { 货号: 'ITEM-902' } },
    { id: 903, hguid: 'detect-903', 商品信息: { 条形码: 'BARCODE-ONLY' } },
    { id: 904, hguid: 'detect-904', 商品信息: { 商品编码: 'HB013-108', 货号: 'HB013-108', 条形码: '9528501322108' } },
  ]),
  [
    { ProductCode: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B', ItemNumber: 'HB013-108', SupplierCode: '200' },
    { ProductCode: 'P-CODE', ItemNumber: 'ITEM-902', SupplierCode: 'SUP01' },
    { ProductCode: 'HB013-108', ItemNumber: 'HB013-108', SupplierCode: '200' },
  ],
  '匹配检测项应同时提交商品编码和行供应商+货号，且不提交条码兜底',
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
  [],
  '商品编码不一致但供应商 200 + HB 货号命中时，只能作为候选，不能自动写入明细数据',
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
      matchType: 'productCode',
      是否新商品: false,
    },
  ],
  '商品编码真实一致时，即使后端返回 item_number，也应按商品编码匹配展示',
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
      hasProductCodeConflict: true,
      localProductCode: 'G091539',
      domesticProductCode: '59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B',
    },
  ],
  '商品编码不同但 200 + 货号命中时，应只标记候选并带出本地主档编码',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9061,
        hguid: 'match-status-9061',
        商品编码: 'DOM-SUP01',
        localSupplierCode: 'SUP01',
        商品信息: { 货号: 'ITEM-SUP01' },
        是否新商品: true,
      },
    ],
    [
      {
        ProductCode: 'LOCAL-SUP01',
        ItemNumber: 'ITEM-SUP01',
        SupplierCode: 'SUP01',
        ProductName: 'Supplier scoped candidate',
      },
    ] satisfies DetectionResult[],
  ),
  [
    {
      hguid: 'match-status-9061',
      matchType: 'supplierItem',
      hasProductCodeConflict: true,
      localProductCode: 'LOCAL-SUP01',
      domesticProductCode: 'DOM-SUP01',
    },
  ],
  '商品编码不同但行供应商+货号命中时，应按真实供应商标记候选',
)

assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9062,
        hguid: 'match-status-9062',
        商品编码: 'DOM-SUP02',
        localSupplierCode: 'SUP02',
        商品信息: { 货号: 'ITEM-SUP02' },
        是否新商品: true,
      },
    ],
    [
      {
        ProductCode: 'LOCAL-WRONG-SUPPLIER',
        ItemNumber: 'ITEM-SUP02',
        SupplierCode: 'SUP01',
        ProductName: 'Wrong supplier candidate',
      },
    ] satisfies DetectionResult[],
  ),
  [],
  '货号相同但供应商不同，不应展示候选',
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
      hguid: 'match-price-705',
      matchType: 'productCode',
      是否新商品: false,
      贴牌价格: 7.7,
      商品名称: '商品编码优先商品',
      单件装箱数: 8,
      装柜数量: 32,
    },
    {
      hguid: 'match-price-707',
      matchType: 'productCode',
      是否新商品: false,
      合计装柜体积: 1,
      合计装柜金额: 65,
      运输成本: 1,
      进口价格: 2.49,
    },
  ],
  '匹配国内数据只允许商品编码精确命中写入；货号命中仅作为候选，不自动补价格或名称',
)
assertEqual(pageSource.includes("t('containers.actions.matchDomesticData')"), true, '页面按钮文案应使用匹配国内数据 i18n key')
assertEqual(
  pageSource.includes('alignDomesticProductCode({') &&
    pageSource.includes('expectedDomesticProductCode: domesticProductCode') &&
    pageSource.includes('targetProductCode: localProductCode') &&
    pageSource.includes('supplierCode,'),
  true,
  '候选商品编码对齐必须走人工确认接口，不能通过匹配国内数据或保存明细自动改码',
)
assertEqual(
  pageSource.includes('const canAlignDomesticProductCode = access.canEditContainer && (access.isAdmin || access.hasPermission(P.Products.Edit))') &&
    pageSource.includes("getContainerDetailProductType(row) !== '套装子商品'") &&
    pageSource.includes('const canAlignCandidate ='),
  true,
  '对齐国内编码入口应要求商品编辑权限，且套装子商品不能显示单独对齐按钮',
)
assertEqual(
  pageSource.includes("renderColumnTitle('warehouseImportPrice'") &&
    pageSource.includes("t('containers.fields.warehouseImportPrice'"),
  true,
  '货柜明细应独立显示实时进货价列',
)
assertEqual(
  pageSource.includes('buildContainerDetailMatchedDomesticDataUpdates(scopedRows, detected, container)') &&
    pageSource.includes("const scopedRows = await confirmBatchRows(t('containers.actions.matchDomesticData'))") &&
    pageSource.includes('return await fetchAllRowsForCurrentQuery()'),
  true,
  '页面应调用匹配国内数据 helper，未勾选时按当前筛选结果全量处理',
)
assertEqual(
  pageSource.includes('findContainerDetailRowsMissingProductName(scopedRows)') &&
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
  containerDetailLogicSource.includes('SupplierCode: getContainerDetailSupplierCode(row)') && !containerDetailLogicSource.includes('Barcode: getContainerDetailBarcode(row)'),
  true,
  '匹配国内数据检测请求应使用行供应商编码且不再提交条码兜底',
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
  pageSource.includes('[active, activeLoadQueryKey]') &&
    pageSource.includes('标签不进入 detailQueryKey；只有非标签远程筛选变化才重置懒加载结果。'),
  true,
  '远程重载 effect 应监听 active 和 base 查询 key，标签切换不应触发 reset reload',
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
  tagFiltersSource.includes('color={option.color}'),
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
  pageSource.includes("t('containers.messages.selectedRowsHidden'"),
  true,
  '隐藏选中行触发批量操作时应使用 i18n 提示用户重新选择',
)
assertEqual(tagFiltersSource.includes('role="button"'), true, '统计 tag 应提供按钮语义')
assertEqual(tagFiltersSource.includes('tabIndex={0}'), true, '统计 tag 应可通过键盘聚焦')
assertEqual(tagFiltersSource.includes('aria-pressed={active}'), true, '统计 tag 应暴露当前选中状态')
assertEqual(
  tagFiltersSource.includes("event.key === 'Enter' || event.key === ' '"),
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
    productCodes: ['HB001', 'HB002', 'HB003'],
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
        productCode: 'HB002',
        localSupplierCode: undefined,
        itemNumber: undefined,
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: undefined,
        importPrice: undefined,
        oemPrice: undefined,
        isNewProduct: true,
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
    skippedNewProductCount: 0,
    missingProductCodeCount: 1,
  },
  '发送到 HQ 应收集有有效匹配信息的选中明细，并对商品编码去重',
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
    productCodes: ['HB015', 'HB016', 'HB017'],
    items: [
      {
        productCode: 'HB015',
        localSupplierCode: 'DATS',
        itemNumber: '72653',
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: 4.2,
        importPrice: 1.55,
        oemPrice: 1.72,
        isNewProduct: true,
      },
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
      {
        productCode: 'HB017',
        localSupplierCode: undefined,
        itemNumber: undefined,
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: undefined,
        importPrice: undefined,
        oemPrice: undefined,
        isNewProduct: true,
      },
    ],
    skippedNewProductCount: 0,
    missingProductCodeCount: 0,
  },
  '发送到 HQ 不应因新商品页面状态跳过候选，并在明细编码为空白时回退商品信息编码',
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
    productCodes: ['HB020'],
    items: [
      {
        productCode: 'HB020',
        localSupplierCode: undefined,
        itemNumber: undefined,
        productName: undefined,
        englishName: undefined,
        barcode: undefined,
        imageUrl: undefined,
        domesticPrice: undefined,
        importPrice: undefined,
        oemPrice: undefined,
        isNewProduct: true,
      },
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
    skippedNewProductCount: 0,
    missingProductCodeCount: 0,
  },
  '新商品页面状态不拦截发送，缺商品编码但有供应商和货号时也应携带候选项',
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
      oemPrice: undefined,
      isNewProduct: false,
    },
  ],
  '发送到 HQ 的候选项应保留图片和价格，但不得携带仓库上下架状态',
)

assertEqual(
  getContainerDetailImageUrl({
    id: 24,
    hguid: 'detail-24',
    商品图片: '  container-detail-image.jpg  ',
    商品信息: { 商品图片: 'product-info-image.jpg' },
  }),
  'container-detail-image.jpg',
  '货柜明细图片展示应优先使用行级商品图片并清理空白',
)

assertEqual(
  getContainerDetailImageUrl({
    id: 25,
    hguid: 'detail-25',
    商品图片: '   ',
    商品信息: { 商品图片: '  product-info-image.jpg  ' },
  }),
  'product-info-image.jpg',
  '货柜明细图片展示应在行级图片为空时回退商品信息图片',
)

assertEqual(
  getContainerDetailImageUrl({
    id: 26,
    hguid: 'detail-26',
    商品图片: '   ',
    商品信息: { 商品图片: '   ' },
  }),
  undefined,
  '货柜明细图片展示在所有图片字段为空时应返回空值',
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
assertEqual(pageSource.includes("message.warning(t('containers.messages.pushToHqSkippedNewProducts'"), false, '发送到 HQ 不应再因页面新商品状态给出跳过 warning')
assertEqual(pageSource.includes('发送 HQ 的结果统一收敛到右上角通知'), true, '发送到 HQ 提交失败也应使用右上角通知承载结果')
assertEqual(pageSource.includes("message: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败')"), true, '后端明确失败时应展示失败通知而不是部分成功')
assertEqual(pageSource.includes('result.warehouseInventoriesCreated'), true, '结果弹窗应展示仓库库存新增统计')
assertEqual(pageSource.includes('result.warehouseInventoriesUpdated'), true, '结果弹窗应展示仓库库存更新统计')
assertEqual(pageSource.includes('result.storeRetailPricesCreated'), true, '结果弹窗应展示分店价格新增统计')
assertEqual(pageSource.includes('result.productSetCodesCreated'), true, '结果弹窗应展示套装多码新增统计')
assertEqual(pageSource.includes('result.storeMultiCodesCreated'), true, '结果弹窗应展示分店多码新增统计')
assertEqual(pageSource.includes('disabled={!selectedRowKeys.length || pushToHqLoading}'), true, '发送到 HQ 按钮必须要求手动选中明细')
const pushToHqHandlerSource = pageSource.slice(
  pageSource.indexOf('const handlePushSelectedProductsToHq = async () => {'),
  pageSource.indexOf('const renderCreateProductResultItems = (items: ContainerProductCreationResultItem[]) => {'),
)
assertEqual(
  pushToHqHandlerSource.includes('if (!ensureNoPendingPriceDetails()) return') &&
    pushToHqHandlerSource.indexOf('if (!ensureNoPendingPriceDetails()) return') < pushToHqHandlerSource.indexOf('const selection = buildContainerDetailHqPushSelection(selectedRows)'),
  true,
  '发送到 HQ 前应阻止未保存的进口价格和零售价继续流转',
)
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
const createNewProductsHandlerSource = pageSource.slice(
  pageSource.indexOf('const createNewProducts = async () => {'),
  pageSource.indexOf('const updateExistingPurchase = async () => {'),
)
assertEqual(
  createNewProductsHandlerSource.includes('if (!ensureNoPendingPriceDetails()) return') &&
    createNewProductsHandlerSource.indexOf('if (!ensureNoPendingPriceDetails()) return') < createNewProductsHandlerSource.indexOf('const scopedRows = await confirmBatchRows'),
  true,
  '创建新商品前应提示先保存明细价格，避免后台 job 读取旧价格',
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
  pageSource.includes('const updateFields = await confirmPushToHqUpdateFields(selection.items.length)') &&
    pageSource.includes('buildPushProductsToHqOperationId(containerGuid, selection.productCodes, selection.items.length, updateFields)') &&
    pageSource.includes('updateFields,'),
  true,
  '货柜发送到 HQ 应先勾选更新字段，并把字段选择传入 job 与 operationId',
)
assertEqual(
  pageSource.includes('function UpdateFieldSelector') &&
    pageSource.includes('indeterminate={isPartiallySelected}') &&
    pageSource.includes("t('common.selectAll', '全选')") &&
    pageSource.includes('getNextUpdateFieldSelection(event.target.checked, allValues)') &&
    pageSource.includes('getUpdateFieldSelectionState(selectedFields, allValues)') &&
    pageSource.includes('value={selectedFields}'),
  true,
  '字段选择弹窗应提供全选复选框，并用受控勾选状态同步字段列表',
)
assertEqual(
  pageSource.includes('type MissingPushToHqUpdateFieldOption = Exclude<PushProductsToHqUpdateField, PushToHqUpdateFieldOptionValue>') &&
    pageSource.includes('const assertAllPushToHqUpdateFieldsCovered: Record<MissingPushToHqUpdateFieldOption, never> = {}'),
  true,
  '发送 HQ 字段清单应有编译期覆盖检查，避免类型新增字段但弹窗漏列',
)
assertEqual(
  pageSource.includes('updateProduct(code, { purchasePrice: row.进口价格 ?? 0 })'),
  false,
  '更新已有商品价格不应调用普通 POS 商品整对象更新接口，避免清空名称、条码和上下架状态',
)
assertEqual(
  pageSource.indexOf('await batchUpdateWarehouseProducts(warehouseUpdates') < pageSource.indexOf('await upsertRetailForActiveStores(retailUpdates)'),
  true,
  '更新已有商品价格应先确认仓库商品批量更新成功，再继续分店价格 upsert',
)
assertEqual(
  pageSource.includes('item.OEMPrice = oemPrice') &&
    pageSource.includes('item.StoreRetailPriceValue = oemPrice') &&
    pageSource.includes('item.MultiCodeRetailPrice = oemPrice'),
  true,
  '更新已有商品价格应同时提交有效零售价，补齐商品主表和分店零售价',
)
assertEqual(
  pageSource.includes('const oemPrice = getContainerDetailVisibleOemPrice(row)') &&
    pageSource.includes('const hasPositiveOemPrice = (row: ContainerDetail) => (getContainerDetailVisibleOemPrice(row) ?? 0) > 0') &&
    pageSource.includes("shouldUpdate('oemPrice') && hasPositiveOemPrice(row)") &&
    !pageSource.includes('hasImportDiff || hasOemDiff'),
  true,
  '更新已有商品价格应以表格可见零售价为准提交价格，不应因检测到的仓库价格相同而跳过',
)
assertEqual(
  pageSource.includes("message.error(error instanceof Error ? error.message : t('containers.messages.purchasePricesUpdateFailed', '更新已有商品价格失败'))"),
  true,
  '更新已有商品价格失败时应给用户可见错误提示',
)
const updateExistingPurchaseHandlerSource = pageSource.slice(
  pageSource.indexOf('const updateExistingPurchase = async () => {'),
  pageSource.indexOf('const deleteSelected = () => {'),
)
assertEqual(
  updateExistingPurchaseHandlerSource.includes('if (!ensureNoPendingPriceDetails()) return') &&
    updateExistingPurchaseHandlerSource.indexOf('if (!ensureNoPendingPriceDetails()) return') < updateExistingPurchaseHandlerSource.indexOf('const confirmed = await confirmBatchRowsWithUpdateFields'),
  true,
  '更新已有商品价格前应阻止未保存的手动价格直接写入商品和分店价格',
)
assertEqual(
  pageSource.indexOf('await batchUpdateWarehouseProducts(warehouseUpdates') < pageSource.indexOf('await upsertMultiCodeForActiveStores(multiCodeUpdates)'),
  true,
  '更新已有商品价格应先确认仓库商品批量更新成功，再继续多码价格 upsert',
)
assertEqual(
  pageSource.includes('syncStorePurchasePrice: shouldUpdate') &&
    !updateExistingPurchaseHandlerSource.includes('IsActive: true'),
  true,
  '更新已有商品应按字段勾选控制分店进货价同步且不再强制改上下架状态',
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
    pageSource.indexOf('await flushPendingDetailSaves()') < pageSource.indexOf('const missingProductNameRows = findContainerDetailRowsMissingProductName(scopedRows)') &&
    pageSource.indexOf('await flushPendingDetailSaves()') < pageSource.indexOf('const missingRetailPriceRows = findContainerDetailRowsMissingCreateProductRetailPrice(scopedRows)') &&
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
  calculateContainerDetailUnitTransportCost({ id: 109, hguid: 'price-109', 运输成本: 0.05, 单件装箱数: 12 }),
  0.6,
  '单件运输成本应等于单品运输成本乘单件装箱数并保留 2 位',
)
assertEqual(
  calculateContainerDetailImportPrice(priceRows[0], priceContainer, 1.2, 1),
  2.04,
  '进口价格应沿用当前公式并保留 2 位',
)
assertEqual(DEFAULT_CONTAINER_DETAIL_FLOAT_RATE, 1.3, '货柜明细空浮率默认值应为 1.30')
assertDeepEqual(
  getContainerDetailCostMissingFields({ 汇率: undefined, 运费: 0, 总体积: 10 }),
  ['exchangeRate'],
  '缺少汇率时应阻止成本重算，但运费 0 是合法输入',
)
assertDeepEqual(
  getContainerDetailCostMissingFields({ 汇率: 4.5, 运费: undefined, 总体积: 0 }),
  ['freight', 'totalVolume'],
  '缺少运费或总体积时应阻止成本重算',
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
    { hguid: 'price-101', 调整浮率: 1.2, 运输成本: 1, 进口价格: 2.04, SkipRelatedProductSync: true },
    { hguid: 'price-102', 调整浮率: 1.2, 运输成本: 1, 进口价格: 2.35, SkipRelatedProductSync: true },
  ],
  '批量应用浮率应同时生成调整浮率、运输成本和进口价格更新',
)
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(priceRows, { ...priceContainer, 汇率: 5, 运费: 9000 }, undefined),
  [
    { hguid: 'price-101', 调整浮率: 1, 运输成本: 0.75, 进口价格: 1.39, SkipRelatedProductSync: true },
    { hguid: 'price-102', 调整浮率: 1.1, 运输成本: 0.75, 进口价格: 1.79, SkipRelatedProductSync: true },
  ],
  '汇率或运费变化后应按每行现有调整浮率重算价格',
)
assertDeepEqual(
  buildContainerDetailFloatRateUpdates([{ ...priceRows[0], 调整浮率: undefined }], priceContainer, undefined),
  [{ hguid: 'price-101', 调整浮率: 1.3, 运输成本: 1, 进口价格: 2.21, SkipRelatedProductSync: true }],
  '行内浮率为空时应按默认 1.30 重算运输成本和进口价格并写回浮率',
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
  [{ hguid: 'price-106', 调整浮率: 1.2, 运输成本: undefined, 进口价格: 8, SkipRelatedProductSync: true }],
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
  pageSource.includes('value={batchFloatRate}') &&
    pageSource.includes("placeholder={t('containers.fields.floatRate')}") &&
    pageSource.includes('precision={2}\n            controls={false}') &&
    pageSource.includes('onChange={setBatchFloatRate}'),
  true,
  '批量修改浮率弹窗输入应保持 2 位小数',
)
assertDeepEqual(
  [
    'value={row.单件装箱数}\n            keyboard={false}\n            min={0}\n            precision={0}\n            step={1}\n            controls={false}',
    'value={row.单件体积}\n            keyboard={false}\n            min={0}\n            precision={3}\n            controls={false}',
    'value={row.调整浮率}\n            keyboard={false}\n            precision={2}\n            controls={false}',
    'value={row.中包数}\n            keyboard={false}\n            min={0}\n            precision={0}\n            controls={false}',
    'value={row.进口价格}\n              keyboard={false}\n              min={0}\n              prefix="$"\n              precision={2}\n              controls={false}',
    'value={getContainerDetailVisibleOemPrice(row)}\n            keyboard={false}\n            min={0}\n            prefix="$"\n            precision={2}\n            controls={false}',
    'value={batchFloatRate}\n            placeholder={t(\'containers.fields.floatRate\')}\n            precision={2}\n            controls={false}',
    'value={batchImportPrice}\n            placeholder={t(\'containers.fields.importPrice\')}\n            min={0}\n            prefix="$"\n            precision={2}\n            controls={false}',
    'value={batchOemPrice}\n            placeholder={t(\'containers.fields.oemPrice\')}\n            min={0}\n            prefix="$"\n            precision={2}\n            controls={false}',
    '<InputNumber value={headerForm.汇率} precision={4} controls={false}',
    '<InputNumber value={headerForm.运费} precision={2} controls={false}',
  ].filter((snippet) => !pageSource.includes(snippet)),
  [],
  '货柜明细页所有可编辑数字输入都应关闭加减按钮',
)
assertEqual(pageSource.includes('value={row.进口价格}'), true, '进口价格输入框应受控，批量应用后立即刷新显示')
assertEqual(pageSource.includes('defaultValue={row.进口价格}'), false, '进口价格输入框不能使用 defaultValue')
assertEqual(pageSource.includes('const updatePayload: UpdateContainerRequest'), true, '保存货柜头部应使用窄更新 payload')
assertEqual(pageSource.includes('await updateContainer(containerGuid, nextContainer)'), false, '保存货柜头部不能把完整货柜对象发送到后端')
assertEqual(
  pageSource.includes("t('containers.fields.domesticPriceTotal')") &&
    pageSource.includes("formatCurrency(container?.合计金额, '¥')"),
  true,
  '货柜头部基础信息应只读展示国内价格合计并使用人民币格式',
)
{
  const headerFormInitStart = pageSource.indexOf('setHeaderForm({')
  const headerFormInitEnd = pageSource.indexOf('    } catch (error) {', headerFormInitStart)
  const headerUpdatePayloadStart = pageSource.indexOf('const updatePayload: UpdateContainerRequest = {')
  const headerUpdatePayloadEnd = pageSource.indexOf('    setSavingHeader(true)', headerUpdatePayloadStart)
  assertEqual(
    headerFormInitStart >= 0 &&
      headerFormInitEnd > headerFormInitStart &&
      headerUpdatePayloadStart >= 0 &&
      headerUpdatePayloadEnd > headerUpdatePayloadStart,
    true,
    '货柜头部表单和保存 payload 断言应先定位到有效源码片段',
  )
  const headerFormInitSource = pageSource.slice(
    headerFormInitStart,
    headerFormInitEnd,
  )
  const headerUpdatePayloadSource = pageSource.slice(
    headerUpdatePayloadStart,
    headerUpdatePayloadEnd,
  )
  assertEqual(
    !headerFormInitSource.includes('合计金额') &&
      !headerUpdatePayloadSource.includes('合计金额') &&
      !headerUpdatePayloadSource.includes('...headerForm'),
    true,
    '国内价格合计来自主表汇总，只读字段不能进入 headerForm 初始化、保存 payload 或被整包展开',
  )
}
assertEqual(
  pageSource.includes('货柜编号: nextContainerNumber') &&
    pageSource.includes("装柜日期: headerForm.装柜日期 ? headerForm.装柜日期.format('YYYY-MM-DD') : undefined") &&
    pageSource.includes("预计到岸日期: headerForm.预计到岸日期 ? headerForm.预计到岸日期.format('YYYY-MM-DD') : undefined"),
  true,
  '保存货柜头部应携带可编辑的货柜编号、装柜日期和预计到岸日期',
)
assertEqual(
  pageSource.includes('value={headerForm.货柜编号}') &&
    pageSource.includes('value={headerForm.装柜日期}') &&
    pageSource.includes('value={headerForm.预计到岸日期}') &&
    pageSource.includes('<DatePicker allowClear={false} value={headerForm.装柜日期}') &&
    pageSource.includes('<DatePicker allowClear={false} value={headerForm.预计到岸日期}') &&
    pageSource.includes("message.error(t('containers.placeholders.enterContainerNumber'"),
  true,
  '货柜头部基础信息编辑态应覆盖编号和日期字段，禁止清空关键日期，并校验货柜编号不能为空',
)
assertEqual(
  pageSource.includes("getContainerDetailCostMissingFields(nextCostContainer).filter((field) => field !== 'totalVolume')"),
  false,
  '保存非成本基础信息不应被汇率或运费缺失拦截',
)
assertEqual(
  pageSource.includes('const shouldRecalculateCosts =') &&
    pageSource.includes('recalculateContainerCostsByScope(containerGuid, buildWholeContainerDetailBatchScope())'),
  true,
  '保存货柜头部汇率或运费变化后应自动按整柜范围重算成本',
)
assertEqual(
  pageSource.includes('const buildWholeContainerDetailBatchScope = (): ContainerDetailBatchScope => ({') &&
    pageSource.includes('filters: {},') &&
    !pageSource.includes('selectedTags: [],'),
  true,
  '货柜头部自动重算应使用不带筛选条件和标签条件的整柜 scope',
)
assertEqual(
  pageSource.includes('Modal.warning') &&
    pageSource.includes('<Space direction="vertical"') &&
    pageSource.includes('showCostRecalculateWarning') &&
    pageSource.includes('missingExchangeRateForCost') &&
    pageSource.includes('missingFreightForCost') &&
    pageSource.includes('missingTotalVolumeForCost'),
  true,
  '成本重算缺少汇率、运费或总体积时应通过弹窗提示并停止写库',
)
assertEqual(
  pageSource.includes('headerSavedCostsRecalculateFailed') &&
    pageSource.includes("message.warning(t('containers.messages.headerSavedCostsRecalculateFailed'"),
  true,
  '保存货柜头部成功但成本重算失败时应独立提示，不能伪装成保存失败',
)
assertEqual(
  pageSource.includes('setBatchFloatRate(DEFAULT_CONTAINER_DETAIL_FLOAT_RATE)') &&
    pageSource.includes('setBatchModalScopeRows(scopedRows)') &&
    pageSource.includes('applyContainerFloatRateByScope(containerGuid, buildDetailBatchScope(batchModalScopeRows), batchFloatRate)'),
  true,
  '批量修改浮率弹窗打开时应默认填入 1.30，确认后按弹窗解析出的批量 scope 重算成本',
)
assertEqual(pageSource.includes("t('containers.formulas.transportCost'"), true, '表格页脚运输成本公式应使用 i18n key')
assertEqual(pageSource.includes("t('containers.formulas.importPrice'"), true, '表格页脚进口价格公式应使用 i18n key')
assertEqual(pageSource.includes('运输成本 = 运费 × 明细体积 ÷ 装柜数量 ÷ 总体积'), true, '表格页脚应展示运输成本公式')
assertEqual(pageSource.includes('进口价格 = ((国内价格 ÷ 汇率 + 运输成本) × 调整浮率 × 10) ÷ 11'), true, '表格页脚应展示进口价格公式')
assertEqual(
  pageSource.includes('const [recalculateCostsLoading, setRecalculateCostsLoading] = useState(false)'),
  false,
  '货柜明细工具栏不再维护独立重算成本按钮 loading 状态',
)
assertEqual(
  pageSource.includes('const handleRecalculateCosts = async () => {'),
  false,
  '货柜明细工具栏不再提供独立重算成本入口',
)
assertEqual(
  pageSource.includes('recalculateContainerCostsByScope(containerGuid, buildDetailBatchScope())'),
  false,
  '独立重算成本不再按当前筛选 scope 暴露，成本重算由头部保存和批量浮率自动触发',
)
assertEqual(
  pageSource.includes('dataSource={displayRows}'),
  true,
  '货柜明细表格应使用列头过滤和排序后的 displayRows',
)
assertEqual(
  pageSource.includes('applyContainerDetailColumnState(filteredRows, {}, sortState)'),
  true,
  '货柜明细列头排序应在前端对当前已加载可见行排序',
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
  !pageSource.includes('const [itemNumberFilter, setItemNumberFilter]') &&
    !pageSource.includes('const [categoryFilterValue, setCategoryFilterValue]') &&
    pageSource.includes('baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))') &&
    pageSource.includes("applyContainerDetailLoadedTextFilters(tagFilteredRows, '', columnFilters)") &&
    !pageSource.includes('applyContainerDetailCategoryFilter(textFilteredRows, categoryFilterValue, categoryLookup)') &&
    !pageSource.includes('categoryFilterValue, sortState'),
  true,
  '顶部过滤条移除后，标签快览和列头文字过滤仍应在前端过滤已加载行，分类和排序不应进入远程加载查询依赖',
)
assertEqual(
  !pageSource.includes("placeholder={t('containers.filters.allCategories'") &&
    !pageSource.includes('options={categoryFilterOptions}') &&
    !pageSource.includes('setCategoryFilterValue(value || CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY)') &&
    !pageSource.includes('buildContainerDetailCategoryOptions(categories, t, i18n.language)') &&
    pageSource.includes("textFilterProps('itemNumber', t('containers.placeholders.filterItemNumber'))"),
  true,
  '货柜明细顶部分类 Select 已移除，货号过滤应只保留列头入口',
)
assertEqual(
  pageSource.includes('filteredRows.length !== rows.length') &&
    pageSource.includes("t('containers.text.visibleRows'"),
  true,
  '分类过滤后应显示当前可见行数量，避免误解为后端总数变化',
)
assertEqual(
  (() => {
    const baseQueryStart = pageSource.indexOf('const baseDetailQuery = useMemo(() => buildContainerDetailQuery({')
    const queryEnd = pageSource.indexOf('const baseDetailQueryKey', baseQueryStart)
    const baseQuerySource = pageSource.slice(baseQueryStart, queryEnd)
    return baseQueryStart >= 0 &&
      queryEnd > baseQueryStart &&
      !pageSource.includes('const scopedDetailQuery = useMemo(() => buildContainerDetailQuery({') &&
      !pageSource.includes('const scopedFullDetailQuery = useMemo(() => buildContainerDetailQuery({') &&
      !baseQuerySource.includes('selectedTags') &&
      baseQuerySource.includes('filters: remoteColumnFilters') &&
      pageSource.includes('const detailQuery = baseDetailQuery') &&
      pageSource.includes('const detailQueryKey = baseDetailQueryKey') &&
      pageSource.includes('const activeLoadQueryKey = detailQueryKey')
  })(),
  true,
  '明细加载查询应只保留无标签 base 查询，标签切换不应进入远程查询 key',
)
assertEqual(
  pageSource.includes("const shouldComputeDetailMeta = mode === 'reset'") &&
    pageSource.includes('includeTotal: shouldComputeDetailMeta') &&
    pageSource.includes('includeStats: shouldComputeDetailMeta') &&
    pageSource.includes('if (result.totalComputed !== false) {') &&
    pageSource.includes('if (result.statsComputed !== false) {'),
  true,
  '货柜明细首屏才应请求 total/tagStats，追加页不应覆盖首屏统计',
)
assertEqual(
  (() => {
    const currentQueryStart = pageSource.indexOf('const fetchAllRowsForCurrentQuery = async () => {')
    const wholeQueryStart = pageSource.indexOf('const fetchAllRowsForWholeContainer = async () => {')
    const queryBlockEnd = pageSource.indexOf('const confirmSubmitContainer', wholeQueryStart)
    const allRowsSource = pageSource.slice(currentQueryStart, queryBlockEnd)
    return currentQueryStart >= 0 &&
      wholeQueryStart > currentQueryStart &&
      queryBlockEnd > wholeQueryStart &&
      allRowsSource.includes('includeTotal: false') &&
      allRowsSource.includes('includeStats: false')
  })(),
  true,
  '后台批量拉全量明细时应跳过 total/tagStats 并依赖 hasMore',
)
assertEqual(
  !pageSource.includes('itemNumber: itemNumberFilter.trim() || columnFilters.itemNumber') &&
    pageSource.includes('const remoteColumnFilters = useMemo<ContainerDetailColumnFilters>(() => omitContainerDetailTextFilters(columnFilters), [columnFilters])'),
  true,
  '顶部货号和列头文字筛选不应合并进远程查询条件',
)
assertEqual(
  pageSource.includes('baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))') &&
    pageSource.includes("applyContainerDetailLoadedTextFilters(tagFilteredRows, '', columnFilters)") &&
    pageSource.includes('hasLoadedFullBaseDetailQuery ? localBaseTagStats : remoteTagStats') &&
    pageSource.includes('return await fetchAllRowsForCurrentQuery()') &&
    pageSource.includes('setBatchModalScopeRows(scopedRows)') &&
    pageSource.includes('buildDetailBatchScope(batchModalScopeRows)') &&
    pageSource.includes('selectedHguids: getRowsHguids(scopeRows)') &&
    pageSource.includes('...baseDetailQuery,'),
  true,
  '标签应始终进入前端过滤链路，批量作用域和全量拉取应使用 base 查询后在前端收敛 HGUID',
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
  pageSource.includes("const CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS = ['englishName', 'packingQuantity', 'unitVolume', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark'] as const") &&
    pageSource.includes("patchRow(rowKey(row), { 中包数: value == null ? undefined : Number(value) })") &&
    pageSource.includes("saveRowPatch(row, { 中包数: event.target.value ? Number(event.target.value) : undefined })"),
  true,
  '中包数列应作为可编辑数字列保存到货柜明细更新接口',
)
assertEqual(
  pageSource.includes('const orderedEditableColumnKeys = useMemo(') &&
    pageSource.includes('getContainerDetailEditableColumnKeysInOrder(') &&
    pageSource.includes('orderedBaseColumns.map((column) => String(column.key))') &&
    pageSource.includes('orderedEditableColumnKeys,\n      direction,'),
  true,
  '方向键导航应按当前页面列顺序过滤可编辑列',
)
assertEqual(
  pageSource.includes('value={row.单件装箱数}\n            keyboard={false}\n            min={0}\n            precision={0}\n            step={1}') &&
    pageSource.includes('renderNumericCell(formatNumber(row.单件装箱数, 0))'),
  true,
  '单件装箱数行内输入和只读显示应按整数处理',
)
assertEqual(
  pageSource.includes('value={row.单件体积}\n            keyboard={false}\n            min={0}\n            precision={3}') &&
    pageSource.includes('renderNumericCell(formatNumber(row.单件体积, 3))'),
  true,
  '单件体积行内输入和只读显示应保留 3 位小数',
)
assertEqual(
  pageSource.includes("patchRow(rowKey(row), { 单件装箱数: row.单件装箱数 })") &&
    pageSource.includes("patchRow(rowKey(row), { 单件体积: row.单件体积 })") &&
    pageSource.includes('const savePackageMetricPatch = async (row: ContainerDetail, patch: Partial<ContainerDetail>) => {') &&
    pageSource.includes('showCostRecalculateWarning(getContainerDetailCostMissingFields(container))') &&
    pageSource.includes('update.SkipRelatedProductSync = true') &&
    pageSource.includes("savePackageMetricPatch(row, { 单件装箱数: Number(event.target.value) })") &&
    pageSource.includes("savePackageMetricPatch(row, { 单件体积: Number(event.target.value) })"),
  true,
  '单件装箱数和单件体积清空时应回滚当前值，系统重算进货价不能同步仓库表',
)
assertEqual(
  pageSource.includes('type PendingContainerDetailPricePatch =') &&
    pageSource.includes('const [pendingPricePatches, setPendingPricePatches] = useState<PendingContainerDetailPricePatchMap>({})') &&
    pageSource.includes('const [priceDetailsSaving, setPriceDetailsSaving] = useState(false)') &&
    pageSource.includes('const markPendingPricePatch = (row: ContainerDetail') &&
    pageSource.includes('const buildPendingPriceSavePlan = (): PendingContainerDetailPriceSavePlan | null => {') &&
    pageSource.includes('const confirmSavePendingPriceDetails = (plan: PendingContainerDetailPriceSavePlan) => new Promise<boolean>') &&
    pageSource.includes('const executePendingPriceSavePlan = async (plan: PendingContainerDetailPriceSavePlan) => {') &&
    pageSource.includes('const savePendingPriceDetails = async () => {'),
  true,
  '货柜明细页应维护进口价格和零售价的手动待保存状态',
)
assertEqual(
  pageSource.includes("const update: UpdateContainerDetailRequest = { hguid: patch.hguid }") &&
    pageSource.includes('if (patch.进口价格 != null) update.进口价格 = patch.进口价格') &&
    pageSource.includes('if (patch.贴牌价格 != null) update.贴牌价格 = patch.贴牌价格') &&
    pageSource.includes('await trackDetailSavePromise(plan.saveKeys, batchUpdateDetails(plan.detailUpdates))') &&
    !pageSource.includes('await batchUpdateWarehouseProducts(plan.warehouseUpdates)') &&
    !pageSource.includes("t('containers.messages.missingWarehouseProductCodeForRetailPrice'") &&
    pageSource.includes('const confirmed = await confirmSavePendingPriceDetails(savePlan)') &&
    pageSource.includes('await executePendingPriceSavePlan(savePlan)') &&
    pageSource.includes('setPendingPricePatches((current) => {') &&
    pageSource.includes("t('containers.messages.detailPricesSaved'"),
  true,
  '保存明细应只发明细事务接口，由后端统一同步已有商品关联价格',
)
assertEqual(
    pageSource.includes("Modal.confirm({") &&
    pageSource.includes("t('containers.modals.savePendingPriceDetailsTitle', '确认保存明细价格')") &&
    pageSource.includes("t('containers.modals.savePendingPriceDetailsUpdateTitle', '更新说明')") &&
    pageSource.includes("'containers.modals.savePendingPriceDetailsSummary'") &&
    pageSource.includes("t('containers.modals.savePendingPriceDetailsExistingRetailHint'") &&
    pageSource.includes("t('containers.modals.savePendingPriceDetailsNewRetailHint'") &&
    pageSource.includes("t('containers.modals.savePendingPriceDetailsRetryHint'"),
  true,
  '保存明细应在落库前弹出二次确认，并展示更新说明',
)
assertEqual(
  pageSource.includes("icon={<SaveOutlined />}") &&
    pageSource.includes('loading={priceDetailsSaving}') &&
    pageSource.includes('disabled={!pendingPricePatchCount || priceDetailsSaving}') &&
    pageSource.includes('onClick={() => void savePendingPriceDetails()}') &&
    pageSource.includes("t('containers.actions.saveDetails', '保存明细')"),
  true,
  '批量价格操作区应提供保存明细按钮，且无待保存价格时禁用',
)
const pendingPriceGuardSource = pageSource.slice(
  pageSource.indexOf('const ensureNoPendingPriceDetails = () => {'),
  pageSource.indexOf('const patchRow = (key: string, patch: Partial<ContainerDetail>) => {'),
)
assertEqual(
  pendingPriceGuardSource.includes('if (!pendingPricePatchCount) return true') &&
    pendingPriceGuardSource.includes("t('containers.messages.savePendingPriceDetailsFirst', '请先点击“保存明细”保存进口价格/零售价')") &&
    pendingPriceGuardSource.includes('return false'),
  true,
  '货柜明细页应提供未保存价格拦截提示，要求用户先点保存明细',
)
assertEqual(
  columnsSource.includes('function renderOemPriceCell(row: ContainerDetail)') &&
    columnsSource.includes("formatCurrency(getContainerDetailVisibleOemPrice(row), '$')") &&
    columnsSource.includes('function renderImportPriceCell(row: ContainerDetail, input?: ReactNode)') &&
    pageSource.includes('renderImportPriceCell(row, (') &&
    pageSource.includes(': renderImportPriceCell(row)') &&
    pageSource.includes("formatCurrency(v, '¥')") &&
    pageSource.includes("prefix=\"$\""),
  true,
  '价格列应按国内价格人民币、其它价格美元显示货币符号',
)
assertEqual(
  containerDetailLogicSource.includes("return currentImportPrice > realtimeImportPrice ? 'up' : 'down'") &&
    columnsSource.includes('getContainerDetailImportPriceTrend(row)') &&
    columnsSource.includes("const Icon = trend === 'up' ? ArrowUpOutlined : ArrowDownOutlined") &&
    columnsSource.includes("return <Icon className={className} />") &&
    pageSource.includes("onChange={(value) => markPendingPricePatch(row, { 进口价格: value == null ? undefined : Number(value) })") &&
    pageSource.includes("onChange={(value) => markPendingPricePatch(row, { 贴牌价格: value == null ? undefined : Number(value) })") &&
    !pageSource.includes("saveRowPatch(row, { 进口价格: event.target.value ? Number(event.target.value) : undefined })") &&
    !pageSource.includes("saveRowPatch(row, { 贴牌价格: event.target.value ? Number(event.target.value) : undefined })") &&
    pageStyleSource.includes('.container-detail-import-price-trend-up') &&
    pageStyleSource.includes('color: #52c41a') &&
    pageStyleSource.includes('.container-detail-import-price-trend-down') &&
    pageStyleSource.includes('color: #ff4d4f'),
  true,
  '进口价格和零售价应改为手动保存明细，不能再失焦自动落库',
)
assertEqual(
  columnsSource.includes('零售价只读单元格：新商品显示明细价，已有商品显示仓库实时价') &&
    pageSource.includes('value={getContainerDetailVisibleOemPrice(row)}') &&
    pageSource.includes('warehouseOEMPrice: patch.贴牌价格') &&
    !columnsSource.includes("source === 'warehouse'") &&
    !pageSource.includes('className={getOemPriceSourceClassName(row)}') &&
    !pageStyleSource.includes('.container-detail-oem-price-cell-warehouse') &&
    !pageStyleSource.includes('.container-detail-oem-price-cell-fallback'),
  true,
  '零售价应按新商品/已有商品分流显示和编辑，不再沿用仓库快照来源底色',
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
    pageSource.includes('disabled={row.是否新商品 || !productCode || isWarehouseStatusPending}'),
  true,
  '行内仓库状态更新应显示提交中状态，并阻止新商品或重复点击',
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
  pageSource.includes('applyContainerPricesByScope(containerGuid, buildDetailBatchScope(batchModalScopeRows)') &&
    pageSource.includes("const scopedRows = await confirmBatchRows(t(isActive ? 'containers.actions.batchActivate' : 'containers.actions.batchDeactivate'))") &&
    pageSource.includes('return await fetchAllRowsForCurrentQuery()') &&
    pageSource.includes('const productCodes = eligibleRows'),
  true,
  '批量修改价格应使用 HGUID scope，批量上下架未选择时应提示后作用于当前筛选结果全量中的已有商品',
)
assertEqual(
  pageSource.includes('className="container-detail-bulk-input"') ||
    pageSource.includes("t('containers.actions.applyFloatRate')") ||
    pageSource.includes("t('containers.actions.applyPrices')") ||
    pageSource.includes("t('containers.actions.recalculateCosts')") ||
    pageSource.includes('onClick={() => void handleRecalculateCosts()}'),
  false,
  '工具栏不应再保留批量浮率、批量价格输入框或独立重算/应用按钮',
)
assertEqual(
  pageSource.includes("key: 'batchFloatRate'") &&
    pageSource.includes("key: 'batchPrices'") &&
    pageSource.includes("key: 'matchDomesticData'") &&
    pageSource.includes("t('containers.actions.batchUpdateFloatRate'") &&
    pageSource.includes("t('containers.actions.batchUpdatePrices'") &&
    !pageSource.includes("key: 'backfillLastPrices'"),
  true,
  '批量操作菜单应包含批量修改浮率、批量修改价格和匹配国内数据，并移除回填上次价格入口',
)
assertDeepEqual(
  [
    "const scopedRows = await confirmBatchRows(t('containers.actions.matchDomesticData'))",
    "const scopedRows = await confirmBatchRows(t(isActive ? 'containers.actions.batchActivate' : 'containers.actions.batchDeactivate'))",
    "const scopedRows = await confirmBatchRows(t('containers.actions.batchTranslate'))",
    "const scopedRows = await confirmBatchRows(t('containers.actions.clearEnglishNames'), { danger: true })",
    "const scopedRows = await confirmBatchRows(t('containers.actions.createNewProducts'))",
    "const confirmed = await confirmBatchRowsWithUpdateFields(",
    "title={t('containers.modals.batchUpdateFloatRateTitle'",
    "title={t('containers.modals.batchUpdatePricesTitle'",
  ].filter((snippet) => !pageSource.includes(snippet)),
  [],
  '写入类批量操作应统一经过确认弹窗或输入确认弹窗',
)
assertEqual(
  pageSource.includes('const resolveBatchActionTargetRows = async () => {') &&
    pageSource.includes('return await fetchAllRowsForCurrentQuery()') &&
    pageSource.includes('const scopedRows = await resolveBatchActionTargetRows()') &&
    pageSource.includes('renderBatchActionContent(batchModalTargetCount)') &&
    pageSource.includes('const [batchModalScopeRows, setBatchModalScopeRows] = useState<ContainerDetail[]>([])') &&
    pageSource.includes('setBatchModalScopeRows(scopedRows)') &&
    pageSource.includes('buildDetailBatchScope(batchModalScopeRows)') &&
    pageSource.includes('selectedHguids: getRowsHguids(scopeRows)') &&
    pageSource.includes("t('containers.modals.batchActionAllHint'"),
  true,
  '未选择商品时应先解析前端完整可见行，弹窗和提交都使用同一批 HGUID 范围',
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
  false,
  '货柜明细页不应再暴露独立重算成本 scope 写回入口',
)
assertEqual(
  pageSource.includes("await loadDetailChunk(1, 'reset')"),
  true,
  '重算成本写回成功后应重载当前查询首块',
)
assertEqual(pageSource.includes("t('containers.messages.missingFreightForCost'"), true, '缺少运费时应通过 i18n 提示且不写库')
assertEqual(pageSource.includes("t('containers.messages.missingTotalVolumeForCost'"), true, '缺少总体积时应通过 i18n 提示且不写库')
assertEqual(
  pageSource.includes("message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))"),
  true,
  '重算成本成功后应提示更新条数',
)
assertEqual(pageSource.includes('loading={recalculateCostsLoading}'), false, '工具栏不应再渲染独立重算成本按钮 loading')
assertEqual(pageSource.includes('onClick={() => void handleRecalculateCosts()}'), false, '工具栏不应再调用独立重算成本入口')
assertEqual(pageSource.includes("t('containers.actions.recalculateCosts')"), false, '工具栏不应再渲染重算成本按钮文案')
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
  "renderColumnTitle('packingQuantity'",
  "renderColumnTitle('containerQuantity'",
  "renderColumnTitle('unitVolume'",
  "renderColumnTitle('domesticPrice'",
  "renderColumnTitle('transportCost'",
  "renderColumnTitle('unitTransportCost'",
  "renderColumnTitle('floatRate'",
  "renderColumnTitle('middlePackQuantity'",
  "renderColumnTitle('warehouseImportPrice'",
  "renderColumnTitle('importPrice'",
  "renderColumnTitle('oemPrice'",
  "renderColumnTitle('lastOEMPrice'",
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
    categoryColumnSource.includes('openRowCategoryModal(row)') &&
    categoryColumnSource.includes('renderContainerDetailCategoryCell(row, categoryLookup, i18n.language') &&
    columnsSource.includes("const displayName = getContainerDetailCategoryName(record) || '--'"),
  true,
  '分类列应显示分类名称，Tooltip 使用完整路径 helper，缺失时显示 --，且有权限时可打开单行目标分类修改弹窗',
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
    pageSource.includes('render: (_, row) => renderReadonlyOemPriceCell(row)') &&
    columnsSource.includes('function renderReadonlyOemPriceCell(row: ContainerDetail)') &&
    !pageSource.slice(pageSource.indexOf('const readonlyOemPriceColumn'), pageSource.indexOf('const baseColumns')).includes("fixed: 'left'") &&
    !pageSource.slice(pageSource.indexOf('const readonlyOemPriceColumn'), pageSource.indexOf('const baseColumns')).includes('<InputNumber'),
  true,
  '条码列后应按开关插入只读零售价列，便于横向滚动前快速核价',
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
  pageSource.includes("const CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.containerDetail.columnOrder.v3'") &&
    pageSource.includes('localStorage.setItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY') &&
    pageSource.includes('mergeContainerDetailColumnOrder('),
  true,
  '货柜明细列顺序应保存到 v3 localStorage key，覆盖旧默认顺序并兼容新增列',
)
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY = 'hbweb_rv.containerDetail.columnWidths.v1'") &&
    pageSource.includes('const [columnWidths, setColumnWidths]') &&
    pageSource.includes('normalizeContainerDetailColumnWidths(') &&
    pageSource.includes('localStorage.setItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY'),
  true,
  '货柜明细列宽应独立保存到 v1 localStorage key，并按当前业务列过滤旧宽度',
)
assertEqual(
  pageSource.includes('data-column-width') &&
    pageSource.includes('onColumnResizeStart: handleColumnResizeStart') &&
    pageSource.includes('className="container-detail-column-resize-handle"') &&
    pageSource.includes('event.stopPropagation()') &&
    pageSource.includes('<div className="container-detail-draggable-header" {...attributes} {...listeners}>') &&
    !pageSource.includes('<th ref={setNodeRef} style={headerStyle} {...props} {...attributes} {...listeners}>'),
  true,
  '货柜明细列宽拖拽应使用独立表头手柄，并避免触发表头排序拖拽',
)
assertEqual(
  pageSource.includes('const tableScrollX = Math.max(') &&
    pageSource.includes('CONTAINER_DETAIL_SELECTION_COLUMN_WIDTH + columns.reduce') &&
    pageSource.includes('scroll={{ x: tableScrollX, y: tableScrollY }}'),
  true,
  '货柜明细横向滚动宽度应跟随当前业务列宽总和更新',
)
assertEqual(
  pageStyleSource.includes('.container-detail-column-resize-handle') &&
    pageStyleSource.includes('cursor: col-resize') &&
    pageStyleSource.includes('touch-action: none'),
  true,
  '货柜明细列宽拖拽手柄应有独立命中区域和 col-resize 光标',
)
assertEqual(
  pageSource.includes("containers.actions.resetColumns") &&
    pageSource.includes('const isColumnSettingsCustomized = isColumnOrderCustomized || isColumnWidthCustomized') &&
    pageSource.includes('setColumnOrder(draggableColumnKeys)') &&
    pageSource.includes('setColumnWidths({})') &&
    pageSource.includes('localStorage.removeItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY)') &&
    pageSource.includes('localStorage.removeItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY)'),
  true,
  '货柜明细手动拖拽列或列宽后应提供重置列按钮并清除本地列设置',
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
    pageSource.includes('void openBatchCategory()') &&
    pageSource.includes('handleBatchCategorySave') &&
    pageSource.includes('getContainerDetailBatchCategoryProductCodes(batchCategoryTargetRows)') &&
    pageSource.includes('batchAssignProducts(targetCategoryGuid, productCodes)'),
  true,
  '批量操作菜单应包含批量分类，并提交当前目标行的去重商品编码',
)
assertEqual(
  pageSource.includes('const canBackfillLastPrices = access.isAdmin || access.isWarehouseManager') ||
    pageSource.includes('if (!canBackfillLastPrices) return') ||
    pageSource.includes("key: 'backfillLastPrices'") ||
    pageSource.includes('backfillContainerLastPricesByScope(containerGuid, scope)'),
  false,
  'web 货柜明细批量操作菜单不应再暴露回填上次价格入口',
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
    batchCategorySaveSource.includes('buildContainerDetailCategoryPatch(item, targetCategoryGuid, selectedTargetCategory, selectedTargetCategoryPath)') &&
    pageSource.includes('WarehouseCategoryGUID: categoryGuid') &&
    pageSource.includes('ProductCategoryGUID: categoryGuid'),
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
const rowCategoryModalSource = pageSource.slice(
  pageSource.indexOf("title={t('containers.modals.rowCategoryTitle'"),
  pageSource.indexOf('<div className={pageClassName}>'),
)
assertEqual(
  rowCategoryModalSource.includes("'目标分类修改'") &&
    rowCategoryModalSource.includes('open={rowCategoryOpen}') &&
    rowCategoryModalSource.includes('selectedKey={rowTargetCategoryGuid}') &&
    rowCategoryModalSource.includes('onOk={() => void handleRowCategorySave()}') &&
    pageSource.includes('await batchUpdateDetails([{ hguid: rowCategoryEditingRow.hguid, ProductCategoryGUID: rowTargetCategoryGuid }])') &&
    pageSource.includes('rowKey(item) !== rowKey(rowCategoryEditingRow)') &&
    pageSource.includes('setRowCategoryOpen(false)'),
  true,
  '单行目标分类修改弹窗应只提交当前行 ProductCategoryGUID，并在保存后只更新当前已加载行',
)
assertEqual(
  pageSource.includes("t('common.copyValue'") &&
    pageSource.includes("t('containers.setCode.pricesTitle'") &&
    pageSource.includes("okText={t('common.save')}") &&
    !pageSource.includes('>重算成本</Button>') &&
    !pageSource.includes('>匹配国内数据</Button>'),
  true,
  '货柜明细复制、重算、匹配和套装多码弹窗应使用 i18n 文案',
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
const stickyControlsStart = pageSource.indexOf('className="container-detail-sticky-controls"')
const detailTableStart = pageSource.indexOf('className="container-detail-table"', stickyControlsStart)
const stickyControlsSource = pageSource.slice(stickyControlsStart, detailTableStart)
assertEqual(
  stickyControlsStart >= 0 &&
    detailTableStart > stickyControlsStart &&
    pageSource.includes('<Card className="container-detail-grid-card">') &&
    pageSource.includes('className="container-detail-table-region"') &&
    !pageSource.includes('className="container-detail-scroll-spacer"') &&
    stickyControlsSource.includes('className="container-detail-toolbar"') &&
    stickyControlsSource.includes('className="container-detail-action-row"') &&
    stickyControlsSource.includes('className="container-detail-action-meta"') &&
    stickyControlsSource.includes('className="container-detail-bulk-row"') &&
    stickyControlsSource.includes('<ContainerTagFilters') &&
    stickyControlsSource.includes('{exporting ? <Progress percent={exportProgress} size="small" /> : null}'),
  true,
  '货柜明细操作按钮、状态信息、批量操作、统计标签和导出进度应在表格前的紧凑 sticky 控制区内',
)
assertEqual(
  pageSource.includes('ref={setGridContentElement}') &&
    pageSource.includes('ref={setToolbarElement}') &&
    pageSource.includes('ref={setTableRegionElement}') &&
    pageSource.includes('ResizeObserver') &&
    pageSource.includes('const [detailLayoutMetrics, setDetailLayoutMetrics] = useState') &&
    pageSource.includes("querySelector('.ant-table-thead')") &&
    pageSource.includes("querySelector('.ant-table-footer')") &&
    pageSource.includes('horizontalScrollbarHeight') &&
    !pageSource.includes("window.addEventListener('scroll', scheduleMeasure") &&
    !pageSource.includes("window.removeEventListener('scroll', scheduleMeasure") &&
    !pageSource.includes("window.addEventListener('scroll', scheduleMeasure, true)") &&
    pageSource.includes('calculateContainerDetailTableScrollY({') &&
    !pageSource.includes('contentTop: detailLayoutMetrics.contentTop,') &&
    pageSource.includes('toolbarHeight: detailLayoutMetrics.toolbarHeight,') &&
    pageSource.includes('tableChromeHeight: detailLayoutMetrics.tableChromeHeight,'),
  true,
  '货柜明细表格高度应只根据工具栏和表格头尾实测高度动态计算，不监听滚动',
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
  pageStyleSource.includes('.container-detail-sticky-controls') &&
    pageStyleSource.includes('position: relative') &&
    !pageStyleSource.includes('top: 138px') &&
    !pageStyleSource.includes('top: 104px') &&
    pageStyleSource.includes('.container-detail-grid-card') &&
    pageStyleSource.includes('position: sticky') &&
    pageStyleSource.includes('top: 150px') &&
    pageStyleSource.includes('.container-detail-grid-card > .ant-card-body') &&
    pageStyleSource.includes('.container-detail-table-region') &&
    pageStyleSource.includes('min-height: 0') &&
    pageStyleSource.includes('overflow: hidden') &&
    pageStyleSource.includes('flex: 1 1 auto') &&
    !pageStyleSource.includes('.container-detail-scroll-spacer') &&
    pageStyleSource.includes('.container-detail-page-small-portrait .container-detail-grid-card') &&
    pageStyleSource.includes('.container-detail-page-small-landscape .container-detail-grid-card') &&
    pageStyleSource.includes('.container-detail-toolbar') &&
    pageStyleSource.includes('.container-detail-action-row') &&
    pageStyleSource.includes('.container-detail-action-meta') &&
    pageStyleSource.includes('.container-detail-bulk-row') &&
    pageStyleSource.includes('overflow-x: auto') &&
    pageStyleSource.includes('flex-wrap: nowrap !important') &&
    pageStyleSource.includes('.container-detail-page-small .container-detail-table .ant-table-footer') &&
    pageStyleSource.includes('.container-detail-page-small-landscape .container-detail-sticky-controls'),
  true,
  '货柜明细控制区应使用局部紧凑布局，不再依赖会被全局标签栏遮挡的固定 sticky top',
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
    pageSource.includes('const imageUrl = getContainerDetailImageUrl(row)') &&
    pageSource.includes('src={imageUrl}') &&
    pageSource.includes('className="container-detail-product-image"') &&
    pageSource.includes('preview={{ mask: t(\'containers.actions.previewImage\', \'查看大图\') }}'),
  true,
  '货柜明细商品图片应使用统一图片兜底并可点击放大预览',
)
assertEqual(
  setCodeHookSource.includes('useTranslation()') &&
    setCodeHookSource.includes("t('containers.setCode.missingProductCode')") &&
    setCodeHookSource.includes("t('containers.setCode.itemNumber')") &&
    setCodeHookSource.includes("t('containers.setCode.purchasePrice')") &&
    !setCodeHookSource.includes("title: '套装货号'") &&
    !setCodeHookSource.includes("message.success('保存成功')"),
  true,
  '套装多码弹窗消息和列标题应走 i18n，不能在英文模式显示中文',
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
    pageSource.includes('const loadedDetailQueryKey = lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid') &&
    pageSource.includes('loadedDetailQueryKey: lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid') &&
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
  setCodeHookSource.includes('getContainerDomesticSetCodes(productCode, abortController.signal)') &&
    setCodeHookSource.includes('setCodeAbortControllerRef.current?.abort()') &&
    setCodeHookSource.includes('updateContainerDomesticSetCodePrices(productCode, changedSetCodePriceItems)'),
  true,
  '套装多码弹窗应支持中止旧请求，并通过国内套装价格接口保存变更',
)
assertEqual(
  setCodeHookSource.includes('const mainPurchasePrice = setCodeModalRow?.进口价格') &&
    setCodeHookSource.includes('calculateContainerSetCodePurchasePrice(mainPurchasePrice, nextRetailPrice, totalRetailPrice)') &&
    !setCodeHookSource.includes('setCodeModalRow?.warehouseImportPrice'),
  true,
  '套装子项进货价应按货柜明细当前行进口价格分摊，不能使用仓库当前进货价',
)
assertEqual(
  pageStyleSource.includes('.container-detail-product-type-tag-clickable'),
  true,
  '可点击商品类型 Tag 应有专属样式提示可操作',
)

console.log('containerDetailLogic.test: ok')
