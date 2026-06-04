import { readFileSync } from 'node:fs'
import type { ContainerDetail } from '../../../types/container'
import type { DetectionResult } from '../../../services/warehouseProductService'
import {
  applyContainerDetailEnglishNameUpdates,
  applyContainerDetailWarehouseStatusByProductCodes,
  applyContainerDetailColumnState,
  buildContainerDetailMatchedDomesticDataUpdates,
  buildContainerDetailTagStats,
  buildContainerDetailFloatRateUpdates,
  buildContainerDetailHqPushSelection,
  buildContainerDetailTranslationUpdates,
  calculateContainerDetailImportPrice,
  calculateContainerDetailTransportCost,
  extractPushToHqErrorResult,
  getContainerDetailEnglishName,
  getContainerDetailProductCode,
  getContainerDetailWarehouseActionFailureMessage,
  isContainerDetailSortField,
  matchesContainerDetailSelectedTags,
  matchesContainerDetailTagFilter,
  normalizeContainerDetailPushToHqPayload,
  type ContainerDetailColumnFilters,
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

assertEqual(
  getContainerDetailEnglishName(rows[0]),
  'Detail Strawberry',
  '英文名称展示应优先读取货柜明细字段',
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

const updatedRows = applyContainerDetailEnglishNameUpdates(rows, [
  { hguid: 'detail-1', 英文名称: 'Large Strawberry' },
])

assertEqual(updatedRows[0].英文名称, 'Large Strawberry', '本地行应写入明细级英文名称')
assertEqual(updatedRows[0].商品信息?.英文名称, 'Large Strawberry', '本地行应同步商品信息英文名称用于展示兜底')
assertEqual(updatedRows[1].商品信息?.英文名称, 'TPR Shark', '未命中的行应保持原值')

const tagRows: ContainerDetail[] = [
  { id: 31, hguid: 'tag-31', 是否新商品: true, 贴牌价格: 0, 进口价格: 1, warehouseIsActive: true },
  { id: 32, hguid: 'tag-32', 是否新商品: true, 贴牌价格: 2, 进口价格: 0, warehouseIsActive: false },
  { id: 33, hguid: 'tag-33', 是否新商品: false, 贴牌价格: 3, 进口价格: 4, warehouseIsActive: true },
  { id: 34, hguid: 'tag-34', 是否新商品: false, 贴牌价格: 0, 进口价格: undefined, warehouseIsActive: undefined },
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
  },
  '统计栏应按当前基础结果统计全部、新商品、已有商品、缺贴牌价、进口价异常和上下架数量',
)
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'new'), true, '新商品 tag 应匹配是否新商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'new'), false, '新商品 tag 不应匹配已有商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'noOemPrice'), true, '缺贴牌价只统计新商品且贴牌价为空或不大于 0 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'noOemPrice'), false, '已有商品缺贴牌价不进入缺贴牌价 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[1], 'abnormalImport'), true, '进口价为 0 应进入进口价异常 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'all'), true, '全部 tag 应匹配所有行')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'active'), true, '上架 tag 应匹配 warehouseIsActive 为 true 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'inactive'), true, '下架 tag 应匹配 warehouseIsActive 非 true 的行')
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], []), true, '未选择 tag 时应显示全部行')
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ['new', 'inactive']), true, '新商品与下架属于不同分组，应同时满足')
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], ['new', 'inactive']), false, '新商品但已上架时不应命中新商品加下架组合')
assertEqual(matchesContainerDetailSelectedTags(tagRows[2], ['new', 'existing']), true, '新商品和已有商品同组多选应按 OR 匹配')
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
    装柜件数: 8,
    装柜数量: 1152,
    国内价格: 12,
    调整浮率: 1.2,
    运输成本: 0.35,
    进口价格: 3.22,
    贴牌价格: 3.88,
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
    装柜件数: 30,
    装柜数量: 4320,
    国内价格: 0,
    调整浮率: 1.1,
    运输成本: undefined,
    进口价格: 0,
    贴牌价格: 0,
    备注: '需要补价格',
    warehouseIsActive: false,
    商品信息: { 货号: 'HB386-013', 条形码: '9527938600047', 英文名称: 'Cube Beads' },
  },
  {
    id: 203,
    hguid: 'column-203',
    商品类型: '套装子商品',
    是否新商品: false,
    装柜件数: 2,
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
assertDeepEqual(columnState({ productTypes: ['set', 'setChild'] }), ['column-202', 'column-203'], '商品类型列头过滤应支持多选枚举')
assertDeepEqual(columnState({ newProductStates: ['new'] }), ['column-202'], '新商品列头过滤应支持筛出新商品')
assertDeepEqual(columnState({ warehouseStatus: ['inactive'] }), ['column-202', 'column-203'], '仓库状态列头过滤应把非 true 视为下架')
assertDeepEqual(columnState({ containerQuantity: { min: 500, max: 2000 } }), ['column-201'], '装柜数量列头范围过滤应同时支持最小值和最大值')
assertDeepEqual(columnState({ domesticPrice: { min: 0, max: 0 } }), ['column-202'], '数字列头范围过滤应正确匹配 0 值')
assertDeepEqual(columnState({ transportCost: { min: 0 } }), ['column-201', 'column-203'], '数字列头范围过滤应排除空值')
assertDeepEqual(columnState({ oemPrice: { min: 2 } }, { field: 'containerPieces', order: 'ascend' }), ['column-203', 'column-201'], '列头过滤后排序应只作用于过滤后的可见行')
assertDeepEqual(columnState({}, { field: 'itemNumber', order: 'ascend' }), ['column-201', 'column-203', 'column-202'], '货号排序应按文本升序且保持稳定输出')
assertDeepEqual(columnState({}, { field: 'transportCost', order: 'ascend' }), ['column-203', 'column-201', 'column-202'], '数字排序应把空值排在最后')
assertDeepEqual(columnState({}, { field: 'warehouseStatus', order: 'descend' }), ['column-201', 'column-202', 'column-203'], '仓库状态排序应支持上架优先且同值保持原始顺序')
const sortableFields: ContainerDetailSortField[] = [
  'itemNumber',
  'barcode',
  'productName',
  'englishName',
  'productType',
  'newProduct',
  'containerPieces',
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
const warehouseProductServiceSource = readFileSync('src/services/warehouseProductService.ts', 'utf8')

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
    单件装箱数: 12,
    单件体积: 0.2,
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
  ] satisfies DetectionResult[],
  matchedPriceContainer,
)

assertDeepEqual(
  matchedPriceUpdates,
  [
    {
      hguid: 'match-price-701',
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
      商品名称: '覆盖名称',
      英文名称: 'Override English',
      单件装箱数: 24,
      单件体积: 0.33,
      进口价格: 1.78,
    },
    {
      hguid: 'match-price-703',
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
      贴牌价格: 7.7,
      商品名称: '商品编码优先商品',
      单件装箱数: 8,
      装柜数量: 32,
    },
    {
      hguid: 'match-price-706',
      贴牌价格: 15.5,
      商品名称: '金/黑框混30X40',
      单件装箱数: 24,
      装柜数量: 288,
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
  pageSource.includes('buildContainerDetailMatchedDomesticDataUpdates(targetRows, detected, container)'),
  true,
  '页面应调用匹配国内数据 helper 并沿用当前目标行范围',
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
  pageSource.includes('[itemNumberFilter, productTypeFilter, selectedTagFilters, columnFilters, sortState]'),
  true,
  '清空已选明细的 effect 应监听顶部筛选、列头过滤和列头排序',
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
        domesticPrice: 0,
        importPrice: 0,
        oemPrice: 0,
        isNewProduct: false,
        warehouseIsActive: undefined,
      },
      {
        productCode: 'HB003',
        localSupplierCode: undefined,
        itemNumber: undefined,
        domesticPrice: 0,
        importPrice: 0,
        oemPrice: 0,
        isNewProduct: false,
        warehouseIsActive: undefined,
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
      商品信息: { 商品编码: ' HB016 ', 货号: '72654', localSupplierCode: 'COS' },
      是否新商品: false,
      国内价格: 5.1,
      进口价格: 1.88,
      贴牌价格: 2.01,
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
        domesticPrice: 5.1,
        importPrice: 1.88,
        oemPrice: 2.01,
        isNewProduct: false,
        warehouseIsActive: false,
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
        domesticPrice: 6.2,
        importPrice: 2.11,
        oemPrice: 2.34,
        isNewProduct: false,
        warehouseIsActive: true,
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
    },
    {
      id: 23,
      hguid: 'detail-23',
      商品编码: 'HB023',
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
      domesticPrice: 3.2,
      importPrice: 1.08,
      oemPrice: 1.2,
      isNewProduct: false,
      warehouseIsActive: true,
    },
    {
      productCode: 'HB023',
      localSupplierCode: undefined,
      itemNumber: undefined,
      domesticPrice: 3.5,
      importPrice: 1.18,
      oemPrice: 1.3,
      isNewProduct: false,
      warehouseIsActive: false,
    },
  ],
  '发送到 HQ 的候选项应保留上下架状态，供 HQ 按明细同步仓库状态',
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

assertEqual(pageSource.includes('pushProductsToHq({'), true, '页面应调用现有商品发送到 HQ 接口')
assertEqual(pageSource.includes('buildContainerDetailHqPushSelection(selectedRows)'), true, '页面应只基于手动选中的明细构建发送范围')
assertEqual(pageSource.includes('items: selection.items'), true, '页面发送到 HQ 时应把候选 items 一并发送给后端')
assertEqual(pageSource.includes('const pushToHqLoadingRef = useRef(false)'), true, '页面应使用 ref 锁防止连续点击重复发送')
assertEqual(pageSource.includes("showPushToHqResult(errorResult, selection, 'failed')"), true, '后端明确失败时应进入失败结果弹窗路径')
assertEqual(pageSource.includes("title: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败')"), true, '后端明确失败时应展示失败弹窗而不是部分成功')
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
assertEqual(
  pageSource.includes("import { pushProductsToHq } from '../../../services/posProductService'"),
  true,
  '更新已有商品进货价不应导入普通 POS 商品整对象更新接口',
)
assertEqual(
  pageSource.includes('updateProduct(code, { purchasePrice: row.进口价格 ?? 0 })'),
  false,
  '更新已有商品进货价不应调用普通 POS 商品整对象更新接口，避免清空名称、条码和上下架状态',
)
assertEqual(
  pageSource.indexOf('await batchUpdateWarehouseProducts(updates.map') < pageSource.indexOf('await upsertRetailForActiveStores(updates.map'),
  true,
  '更新已有商品进货价应先确认仓库商品批量更新成功，再继续分店价格 upsert',
)
assertEqual(
  pageSource.includes("message.error(error instanceof Error ? error.message : t('containers.messages.purchasePricesUpdateFailed', '更新已有商品进货价失败'))"),
  true,
  '更新已有商品进货价失败时应给用户可见错误提示',
)
assertEqual(
  pageSource.indexOf('await batchUpdateWarehouseProducts(updates.map') < pageSource.indexOf('await upsertMultiCodeForActiveStores(updates.map'),
  true,
  '更新已有商品进货价应先确认仓库商品批量更新成功，再继续多码价格 upsert',
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
  '更新已有商品进货价不应吞掉 POS 商品更新失败',
)
assertEqual(
  pageSource.includes('posUpdateFailures'),
  false,
  '更新已有商品进货价不应再保留 POS 商品整对象更新失败分支',
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
assertEqual(pageSource.includes('value={row.调整浮率}'), true, '调整浮率输入框应受控，批量应用后立即刷新显示')
assertEqual(pageSource.includes('defaultValue={row.调整浮率}'), false, '调整浮率输入框不能使用 defaultValue')
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
  pageSource.includes('buildContainerDetailFloatRateUpdates(targetRows, container)'),
  true,
  '重算成本应基于当前最终可见目标行和货柜信息计算更新',
)
assertEqual(
  pageSource.includes('dataSource={displayRows}'),
  true,
  '货柜明细表格应使用列头过滤和排序后的 displayRows',
)
assertEqual(
  pageSource.includes('applyContainerDetailColumnState(filteredRows, columnFilters, sortState)'),
  true,
  '顶部筛选结果应继续叠加列头过滤和排序生成 displayRows',
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
    pageSource.includes("renderColumnTitle('containerQuantity'") &&
    pageSource.includes("renderColumnTitle('importPrice'") &&
    pageSource.includes("renderColumnTitle('warehouseStatus'"),
  true,
  '关键业务列应挂载列头排序或过滤配置',
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
  pageSource.includes('const updates = targetRows') &&
    pageSource.includes('const productCodes = targetRows'),
  true,
  '应用价格和批量上下架应在未选择时作用于最终可见行',
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
  pageSource.includes('await batchUpdateDetails(updates)'),
  true,
  '重算成本应通过明细批量更新接口写回后端',
)
assertEqual(
  pageSource.includes('applyDetailUpdatesToRows(updates)'),
  true,
  '重算成本写回成功后应同步更新本地行状态',
)
assertEqual(pageSource.includes('缺少运费，无法重算成本'), true, '缺少运费时应提示 warning 且不写库')
assertEqual(pageSource.includes('缺少总体积，无法重算成本'), true, '缺少总体积时应提示 warning 且不写库')
assertEqual(pageSource.includes('没有可重算的明细'), true, '无目标行时应提示 warning 且不写库')
assertEqual(pageSource.includes('没有可写回的成本更新'), true, '没有生成更新时应提示 warning 且不写库')
assertEqual(
  pageSource.includes("message.success(t('containers.messages.detailsUpdated', { count: updates.length }))"),
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
assertEqual(
  pageSource.includes('rowSelection={{ selectedRowKeys, onChange: setSelectedRowKeys, fixed: true }}'),
  true,
  '选择框列应随左侧固定列一起固定，避免横向滚动时列位错开',
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
assertEqual(pageSource.includes('className="container-detail-table"'), true, '货柜明细表格应挂载专属 class 以隔离垂直对齐样式')
assertEqual(
  pageSource.includes("rowClassName={(_, index) => index % 2 === 1 ? 'container-detail-row-striped' : ''}"),
  true,
  '货柜明细表格应按当前显示顺序给偶数视觉行添加隔行色 class',
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

console.log('containerDetailLogic.test: ok')
