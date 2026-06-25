import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: boolean, message: string) {
  if (!condition) {
    throw new Error(message)
  }
}

const pageSource = readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrderImportPriceVariance/index.tsx'), 'utf8')
const routeSource = readFileSync(path.resolve(process.cwd(), 'src/router/routes.tsx'), 'utf8')
const zhLocale = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))
const enLocale = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))

assert(
  pageSource.includes("dataIndex: 'productImage'") &&
	    pageSource.includes("dataIndex: 'domesticPrice'") &&
	    pageSource.includes("dataIndex: 'unitVolume'") &&
	    pageSource.includes("dataIndex: 'packingQuantity'") &&
	    pageSource.includes("dataIndex: 'warehouseImportPrice'") &&
	    pageSource.includes("dataIndex: 'firstContainerImportPrice'") &&
    pageSource.includes("dataIndex: 'originalImportAmountTotal'") &&
    pageSource.includes("dataIndex: 'baselineImportAmountTotal'") &&
    pageSource.includes("dataIndex: 'varianceAmountTotal'"),
	  '商品汇总主表必须包含商品图片、国内价格、体积、装箱数、当前仓库进货价格、首次进货价和三项金额合计列',
	)

const editablePriceBlockStart = pageSource.indexOf('const renderEditablePriceCell')
const editablePriceBlockEnd = pageSource.indexOf('const openBatchWarehouseImportPriceModal')
const editablePriceBlock = pageSource.slice(editablePriceBlockStart, editablePriceBlockEnd)

	assert(
	  pageSource.includes('updateStoreOrderImportPriceVarianceDomesticPrice') &&
	    pageSource.includes('updateStoreOrderImportPriceVarianceWarehouseImportPrice') &&
	    pageSource.includes('function parsePriceDraft') &&
	    pageSource.includes("type EditablePriceField = 'domesticPrice' | 'warehouseImportPrice'") &&
	    pageSource.includes('const priceInputRefs = useRef') &&
	    pageSource.includes('const savingPriceKeyRef = useRef') &&
	    pageSource.includes('savingPriceKeyRef.current === key') &&
	    pageSource.includes('inputMode="decimal"') &&
	    pageSource.includes('event.currentTarget.select()') &&
    pageSource.includes("event.key === 'ArrowUp'") &&
    pageSource.includes("event.key === 'ArrowDown'") &&
    pageSource.includes("event.key === 'Enter'") &&
    pageSource.includes("event.key === 'Escape'") &&
    editablePriceBlockStart >= 0 &&
    editablePriceBlockEnd > editablePriceBlockStart &&
    !editablePriceBlock.includes('<InputNumber') &&
    !pageSource.includes('type="number"'),
	  '国内价格和当前仓库进货价格列必须使用普通 Input 内联编辑，支持全选、方向键、回车保存、Esc 取消，且不能出现数字加减控件',
	)

const warehouseImportPriceColumnIndex = pageSource.indexOf("dataIndex: 'warehouseImportPrice'")
const firstContainerImportPriceColumnIndex = pageSource.indexOf("dataIndex: 'firstContainerImportPrice'")
assert(
  warehouseImportPriceColumnIndex >= 0 &&
    firstContainerImportPriceColumnIndex > warehouseImportPriceColumnIndex,
  '当前仓库进货价格列必须位于首次货柜价列前面',
)

assert(
  pageSource.includes('const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([])') &&
    pageSource.includes('rowSelection={{') &&
    pageSource.includes('selectedRowKeys,') &&
    pageSource.includes('preserveSelectedRowKeys: true') &&
    pageSource.includes('getCheckboxProps: (row) => ({ disabled: !row.productCode })') &&
    pageSource.includes('openBatchWarehouseImportPriceModal') &&
    pageSource.includes('handleBatchWarehouseImportPriceSave') &&
    pageSource.includes('batchUpdateStoreOrderImportPriceVarianceWarehouseImportPrice({') &&
    pageSource.includes('productCodes,') &&
    pageSource.includes('warehouseImportPrice: values.warehouseImportPrice ?? 0') &&
    pageSource.includes('setSelectedRowKeys([])') &&
    pageSource.includes('await loadData()') &&
    pageSource.includes("title={t('storeOrders.importPriceVariance.batchWarehouseImportPriceTitle'") &&
    pageSource.includes('<InputNumber') &&
    pageSource.includes('批量修改只提交商品编码和统一的新当前参考进货价'),
  '商品汇总主表必须支持勾选商品后批量修改当前参考进货价，成功后清空选择并刷新统计结果',
)

assert(
  pageSource.includes("dataIndex: 'supplierCode'") &&
    pageSource.includes("t('storeOrders.importPriceVariance.domesticSupplier')") &&
    pageSource.includes('name="supplierCode"'),
  '页面必须包含国内供应商筛选组件和国内供应商列',
)

assert(
  pageSource.includes("import { getActiveChinaSuppliers }") &&
    pageSource.includes('function DomesticSupplierFilterSelect') &&
    pageSource.includes('getActiveChinaSuppliers(currentController.signal)') &&
    pageSource.includes('onOpenChange={handleSupplierOpenChange}'),
  '国内供应商过滤组件必须复用 getActiveChinaSuppliers 并在首次展开时加载',
)

assert(
  pageSource.includes('const DEFAULT_PAGE_SIZE = 20') &&
    pageSource.includes("const DEFAULT_SORT_BY = 'absoluteVarianceAmount'") &&
    pageSource.includes('const DEFAULT_SORT_DESCENDING = true'),
  '页面默认分页和排序必须符合后端统计页契约',
)

assert(
  pageSource.includes("dataIndex: 'varianceAmountTotal'") &&
    pageSource.includes("key: 'varianceAmountTotal'") &&
    pageSource.includes("const DEFAULT_SORT_BY = 'absoluteVarianceAmount'"),
  '商品汇总差额合计列点击排序必须发送有符号 varianceAmountTotal，默认首屏才使用绝对差额排序',
)

assert(
  pageSource.includes('getStoreOrderImportPriceVariance(query)') &&
    pageSource.includes('onChange={handleTableChange}'),
  '主表必须通过服务端接口加载并响应表格分页排序',
)

assert(
  pageSource.includes('const [supplierSummaries, setSupplierSummaries]') &&
    pageSource.includes('setSupplierSummaries(result.supplierSummaries)') &&
    pageSource.includes('const supplierSummaryColumns') &&
    pageSource.includes('<Table<StoreOrderImportPriceVarianceSupplierSummary>') &&
    pageSource.includes('supplierVarianceRankingTitle') &&
    pageSource.includes('noSupplierVarianceData') &&
    pageSource.includes("dataIndex: 'increaseVarianceAmountTotal'") &&
    pageSource.includes("dataIndex: 'decreaseVarianceAmountTotal'") &&
    pageSource.includes('defaultPageSize: 50') &&
    pageSource.includes('pageSizeOptions: [20, 50, 100]') &&
    pageSource.includes('compareSupplierText') &&
    pageSource.includes('compareSupplierNumber') &&
    pageSource.includes('sorter: (left, right)') &&
    pageSource.includes('const supplierSummaryRegionRef = useRef<HTMLDivElement | null>(null)') &&
    pageSource.includes('const [supplierSummaryTableScrollY, setSupplierSummaryTableScrollY]') &&
    pageSource.includes("maxHeight: 'calc(100vh - 32px)'") &&
    pageSource.includes('scroll={{ x: 1120, y: supplierSummaryTableScrollY }}') &&
    !pageSource.includes('result.supplierSummaries.slice(0, 10)') &&
    !pageSource.includes('SUPPLIER_SUMMARY_PLACEHOLDER_COUNT'),
  '页面必须用单张一屏内可滚动、可排序的表格展示所有国内供应商差额统计，并默认每页 50 条',
)

assert(
  pageSource.includes('useLayoutEffect') &&
    pageSource.includes('const tableRegionRef = useRef<HTMLDivElement | null>(null)') &&
    pageSource.includes('const [tableScrollY, setTableScrollY]') &&
    pageSource.includes("height: 'calc(100vh - 32px)'") &&
    pageSource.includes('region.clientHeight') &&
    pageSource.includes('scroll={{ x: 2000, y: tableScrollY }}') &&
    pageSource.includes('主表和供应商统计都把滚动限制在表格 body 内') &&
    pageSource.includes("overflow: 'hidden'"),
  '主表区域必须按一屏高度展示，并根据表格区域自身高度计算 body 内部滚动高度',
)

assert(
  pageSource.includes('getStoreOrderImportPriceVarianceDetails({') &&
    pageSource.includes('productCode: selectedProduct.productCode') &&
    pageSource.includes('<Modal') &&
    pageSource.includes('onChange={handleDetailTableChange}'),
  '点击商品订单明细必须打开弹窗并通过 details 接口服务端分页加载',
)

assert(
  pageSource.includes('...filters') &&
    pageSource.includes('supplierCode: trimText(values.supplierCode)'),
  '主表筛选和弹窗明细必须共享当前筛选条件，包括国内供应商',
)

assert(
  pageSource.includes("import { useNavigate } from 'react-router-dom'") &&
    pageSource.includes('const navigate = useNavigate()'),
  '页面必须使用 useNavigate 打开订单和货柜明细页',
)

assert(
  pageSource.includes('navigate(`/warehouse/store-order/detail/${row.orderGUID}`, {') &&
    pageSource.includes('state: { orderNo: row.orderNo }'),
  '弹窗订单号列必须跳转到对应订货明细并传入订单号作为详情页初始标题',
)

assert(
  pageSource.includes('navigate(`/warehouse/container/detail/${row.firstContainerCode}`)'),
  '首次货柜编号列必须跳转到对应货柜明细页',
)

const routeStart = routeSource.indexOf("path: '/warehouse/store-order-import-price-variance'")
const routeEnd = routeSource.indexOf("path: '/warehouse/store-order/detail/:id'", routeStart)
const routeBlock = routeSource.slice(routeStart, routeEnd)

assert(routeStart >= 0 && routeEnd > routeStart, '路由必须注册首次货柜价差异统计页')
assert(routeBlock.includes("title: 'menu.storeOrderImportPriceVariance'"), '路由标题 key 必须符合菜单契约')
assert(routeBlock.includes("icon: 'BarChartOutlined'"), '路由图标应使用 BarChartOutlined')
assert(
  routeBlock.includes("accessKey: 'canManageStoreOrderImportPriceVariance'"),
  '路由权限必须收束到首柜价差异专用仓库管理员权限',
)

const fallbackStart = routeSource.indexOf('function buildWarehouseStaffMenus')
const fallbackEnd = routeSource.indexOf('export function buildMenus', fallbackStart)
const fallbackBlock = routeSource.slice(fallbackStart, fallbackEnd)

assert(
  fallbackBlock.includes("key: '/warehouse/store-orders'") &&
    !fallbackBlock.includes("key: '/warehouse/store-order-import-price-variance'"),
  '仓库员工 fallback 菜单只能保留分店订货列表，不能暴露首柜价差异统计页',
)

assert(
  zhLocale.menu.storeOrderImportPriceVariance === '首次货柜价差异统计' &&
    enLocale.menu.storeOrderImportPriceVariance === 'First Container Price Variance',
  '中英文菜单文案必须存在',
)

assert(
  zhLocale.storeOrders.importPriceVariance.originalImportAmount === '原始金额' &&
    zhLocale.storeOrders.importPriceVariance.baselineImportAmount === '基准金额' &&
    zhLocale.storeOrders.importPriceVariance.varianceAmount === '差额',
  '中文统计页核心明细列文案必须自然可读',
)

assert(
	  zhLocale.storeOrders.importPriceVariance.domesticSupplier === '国内供应商' &&
	    zhLocale.storeOrders.importPriceVariance.productImage === '商品图片' &&
	    zhLocale.storeOrders.importPriceVariance.domesticPrice === '国内价格' &&
	    zhLocale.storeOrders.importPriceVariance.warehouseImportPrice === '当前仓库进货价格' &&
	    enLocale.storeOrders.importPriceVariance.warehouseImportPrice === 'Current Warehouse Import Price' &&
	    zhLocale.storeOrders.importPriceVariance.unitVolume === '体积' &&
	    zhLocale.storeOrders.importPriceVariance.packingQuantity === '装箱数',
  '中文商品汇总列文案必须存在',
)

assert(
  zhLocale.storeOrders.importPriceVariance.batchWarehouseImportPrice === '批量修改当前参考进货价' &&
    zhLocale.storeOrders.importPriceVariance.batchWarehouseImportPriceTitle ===
      '批量修改当前参考进货价 ({{count}} 个商品)' &&
    zhLocale.storeOrders.importPriceVariance.batchSaveWarehouseImportPriceSuccess ===
      '已批量保存 {{count}} 个商品的仓库进货价格' &&
    enLocale.storeOrders.importPriceVariance.batchWarehouseImportPrice ===
      'Batch update reference import price' &&
    enLocale.storeOrders.importPriceVariance.batchWarehouseImportPriceTitle ===
      'Batch Update Reference Import Price ({{count}} products)' &&
    enLocale.storeOrders.importPriceVariance.batchSaveWarehouseImportPriceSuccess ===
      'Saved warehouse import price for {{count}} products',
  '批量修改当前参考进货价的中英文按钮、标题和成功文案必须存在',
)

assert(
  zhLocale.storeOrders.importPriceVariance.directionIncrease === '多收' &&
    zhLocale.storeOrders.importPriceVariance.directionDecrease === '少收' &&
    enLocale.storeOrders.importPriceVariance.directionIncrease === 'Overcharged' &&
    enLocale.storeOrders.importPriceVariance.directionDecrease === 'Undercharged',
  '差额方向文案必须表达订单进货价相对首次货柜价的多收/少收语义',
)

assert(
  zhLocale.storeOrders.importPriceVariance.supplierVarianceRankingTitle === '国内供应商差额统计' &&
    zhLocale.storeOrders.importPriceVariance.increaseVarianceAmountTotal === '多收合计' &&
    zhLocale.storeOrders.importPriceVariance.decreaseVarianceAmountTotal === '少收合计' &&
    zhLocale.storeOrders.importPriceVariance.productCount === '商品数' &&
    zhLocale.storeOrders.importPriceVariance.detailCount === '明细数' &&
    zhLocale.storeOrders.importPriceVariance.noSupplierVarianceData === '暂无供应商差额数据' &&
    zhLocale.storeOrders.importPriceVariance.totalSuppliers === '共 {{total}} 个供应商' &&
    enLocale.storeOrders.importPriceVariance.supplierVarianceRankingTitle === 'Domestic Supplier Variance' &&
    enLocale.storeOrders.importPriceVariance.increaseVarianceAmountTotal === 'Overcharged Total' &&
    enLocale.storeOrders.importPriceVariance.decreaseVarianceAmountTotal === 'Undercharged Total' &&
    enLocale.storeOrders.importPriceVariance.productCount === 'Products' &&
    enLocale.storeOrders.importPriceVariance.detailCount === 'Details' &&
    enLocale.storeOrders.importPriceVariance.noSupplierVarianceData === 'No supplier variance data' &&
    enLocale.storeOrders.importPriceVariance.totalSuppliers === '{{total}} suppliers',
  '中英文供应商差额统计表格文案必须存在',
)

console.log('storeOrderImportPriceVariance.logic.test: ok')
