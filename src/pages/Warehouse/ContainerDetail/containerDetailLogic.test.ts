import { readFileSync } from 'node:fs'
import type { ContainerDetail } from '../../../types/container'
import {
  applyContainerDetailEnglishNameUpdates,
  buildContainerDetailTagStats,
  buildContainerDetailFloatRateUpdates,
  buildContainerDetailHqPushSelection,
  buildContainerDetailTranslationUpdates,
  calculateContainerDetailImportPrice,
  calculateContainerDetailTransportCost,
  extractPushToHqErrorResult,
  getContainerDetailEnglishName,
  matchesContainerDetailTagFilter,
  normalizeContainerDetailPushToHqPayload,
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
  { id: 31, hguid: 'tag-31', 是否新商品: true, 贴牌价格: 0, 进口价格: 1 },
  { id: 32, hguid: 'tag-32', 是否新商品: true, 贴牌价格: 2, 进口价格: 0 },
  { id: 33, hguid: 'tag-33', 是否新商品: false, 贴牌价格: 3, 进口价格: 4 },
  { id: 34, hguid: 'tag-34', 是否新商品: false, 贴牌价格: 0, 进口价格: undefined },
]

assertDeepEqual(
  buildContainerDetailTagStats(tagRows),
  {
    all: 4,
    new: 2,
    existing: 2,
    noOemPrice: 1,
    abnormalImport: 2,
  },
  '统计栏应按当前基础结果统计全部、新商品、已有商品、缺贴牌价和进口价异常数量',
)
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'new'), true, '新商品 tag 应匹配是否新商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'new'), false, '新商品 tag 不应匹配已有商品行')
assertEqual(matchesContainerDetailTagFilter(tagRows[0], 'noOemPrice'), true, '缺贴牌价只统计新商品且贴牌价为空或不大于 0 的行')
assertEqual(matchesContainerDetailTagFilter(tagRows[3], 'noOemPrice'), false, '已有商品缺贴牌价不进入缺贴牌价 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[1], 'abnormalImport'), true, '进口价为 0 应进入进口价异常 tag')
assertEqual(matchesContainerDetailTagFilter(tagRows[2], 'all'), true, '全部 tag 应匹配所有行')

const pageSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.tsx', 'utf8')
const pageStyleSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.css', 'utf8')
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
  pageSource.includes('[itemNumberFilter, productTypeFilter, tagFilter]'),
  true,
  '清空已选明细的 effect 应监听货号、商品类型和统计 tag 筛选',
)
assertEqual(
  pageSource.includes('const targetRows = selectedRowKeys.length ? selectedRows : filteredRows'),
  true,
  '批量目标行应按是否存在选择意图判断，隐藏选中行时不能退回当前全部可见行',
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
    skippedNewProductCount: 1,
    missingProductCodeCount: 1,
  },
  '发送到 HQ 应只收集本地已有且有商品编码的选中明细，并对商品编码去重',
)

assertDeepEqual(
  buildContainerDetailHqPushSelection([
    { id: 15, hguid: 'detail-15', 商品编码: ' HB015 ', 是否新商品: 1 as unknown as boolean },
    { id: 16, hguid: 'detail-16', 商品编码: '   ', 商品信息: { 商品编码: ' HB016 ' }, 是否新商品: false },
    { id: 17, hguid: 'detail-17', 商品编码: 'HB017', 是否新商品: true },
  ]),
  {
    productCodes: ['HB015', 'HB016'],
    skippedNewProductCount: 1,
    missingProductCodeCount: 0,
  },
  '发送到 HQ 只有是否新商品严格为 true 时才跳过，明细编码为空白时应回退商品信息编码',
)

assertDeepEqual(
  buildContainerDetailHqPushSelection([
    { id: 20, hguid: 'detail-20', 商品编码: 'HB020', 是否新商品: true },
    { id: 21, hguid: 'detail-21', 是否新商品: false },
  ]),
  {
    productCodes: [],
    skippedNewProductCount: 1,
    missingProductCodeCount: 1,
  },
  '全部不可发送时应返回空商品编码并保留跳过统计',
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
assertEqual(pageSource.includes('const pushToHqLoadingRef = useRef(false)'), true, '页面应使用 ref 锁防止连续点击重复发送')
assertEqual(pageSource.includes("showPushToHqResult(errorResult, selection, 'failed')"), true, '后端明确失败时应进入失败结果弹窗路径')
assertEqual(pageSource.includes("title: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败')"), true, '后端明确失败时应展示失败弹窗而不是部分成功')
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
  true,
  '更新已有商品进货价应汇总 POS 商品更新失败编码',
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
  '重算成本应基于当前目标行和货柜信息计算更新',
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
  pageSource.includes("title: t('containers.fields.itemNumber'), width: 160, fixed: 'left'"),
  true,
  '货号列应固定在左侧，横向滚动时保持可见',
)
assertEqual(
  pageSource.includes('rowSelection={{ selectedRowKeys, onChange: setSelectedRowKeys, fixed: true }}'),
  true,
  '选择框列应随左侧固定列一起固定，避免横向滚动时列位错开',
)
assertEqual(pageSource.includes('className="container-detail-table"'), true, '货柜明细表格应挂载专属 class 以隔离垂直对齐样式')
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

console.log('containerDetailLogic.test: ok')
