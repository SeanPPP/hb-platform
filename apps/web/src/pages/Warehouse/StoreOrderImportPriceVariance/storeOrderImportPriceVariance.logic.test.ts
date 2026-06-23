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
    pageSource.includes("dataIndex: 'firstContainerImportPrice'") &&
    pageSource.includes("dataIndex: 'originalImportAmountTotal'") &&
    pageSource.includes("dataIndex: 'baselineImportAmountTotal'") &&
    pageSource.includes("dataIndex: 'varianceAmountTotal'"),
  '商品汇总主表必须包含商品图片、国内价格、体积、装箱数、首次进货价和三项金额合计列',
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
assert(routeBlock.includes("accessKey: 'canManageWarehouseOrders'"), '路由权限必须沿用仓库订单管理权限')

const fallbackStart = routeSource.indexOf('function buildWarehouseStaffMenus')
const fallbackEnd = routeSource.indexOf('export function buildMenus', fallbackStart)
const fallbackBlock = routeSource.slice(fallbackStart, fallbackEnd)

assert(
  fallbackBlock.includes("key: '/warehouse/store-orders'") &&
    fallbackBlock.includes("key: '/warehouse/store-order-import-price-variance'"),
  '仓库员工 fallback 菜单必须同时包含分店订货列表和统计页',
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
    zhLocale.storeOrders.importPriceVariance.unitVolume === '体积' &&
    zhLocale.storeOrders.importPriceVariance.packingQuantity === '装箱数',
  '中文商品汇总列文案必须存在',
)

assert(
  zhLocale.storeOrders.importPriceVariance.directionIncrease === '多收' &&
    zhLocale.storeOrders.importPriceVariance.directionDecrease === '少收' &&
    enLocale.storeOrders.importPriceVariance.directionIncrease === 'Overcharged' &&
    enLocale.storeOrders.importPriceVariance.directionDecrease === 'Undercharged',
  '差额方向文案必须表达订单进货价相对首次货柜价的多收/少收语义',
)

console.log('storeOrderImportPriceVariance.logic.test: ok')
