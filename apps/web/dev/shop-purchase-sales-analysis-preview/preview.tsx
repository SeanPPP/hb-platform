import ReactDOM from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ConfigProvider } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import enUS from 'antd/locale/en_US'
import 'antd/dist/reset.css'
import '../../src/styles/global.css'
import i18n from '../../src/i18n'
import ShopLayout from '../../src/layout/ShopLayout'
import { ShopPreorderLeaveProvider } from '../../src/pages/ShopPreorder/preorderLeaveContext'
import { useAuthStore } from '../../src/store/auth'
import { buildAccess } from '../../src/utils/access'
import AdminLayout from '../../src/layout/AdminLayout'
import ShopPurchaseSalesAnalysisPage from '../../src/pages/ShopPurchaseSalesAnalysis'
import type { LocalSupplierPurchaseSalesDailyPointDto } from '../../src/types/localSupplierInvoice'

// 本地交互验收入口：默认挂载真实 ShopLayout + 前台「进货销量分析」页面；?mode=admin 挂载 AdminLayout + 销售看板下的后台页面。
// 仅在本入口内模拟 fetch，未登记生产路由。
const store = { storeGUID: 'store-b1', storeName: 'Springfield', storeCode: 'B1', isActive: true, isManageable: true, assignedAt: '2026-01-01T00:00:00Z' }
const suppliers = [{ label: 'Brazco', value: 'BRZ' }, { label: 'Orion Trading', value: 'ORI' }, { label: 'Pacific Homeware', value: 'PAC' }]
const TODAY = '2026-09-18'
// [名称, 货号, 条码, 上次进货日, 上次数量, 最近进货日, 最近数量, 间隔天数, 间隔销量, 日均基准, 波动种子]
const rows = [
  ['Reusable Rect 750ml PK20 Col', 'KI148416', '9328644148416', '2026-05-26', 96, '2026-07-28', 144, 63, 88, 2.2, 3],
  ['Wacky Pool Noodles', 'TY022181', '9328644022181', '2026-04-30', 192, '2026-06-24', 192, 55, 152, 1.8, 7],
  ['Reusable Rect 500ml Pk25', 'KI148461', '9328644148461', '2026-04-30', 192, '2026-05-26', 48, 26, 80, 0.5, 11],
  ['Mask Smile Creep', 'HW174835', '9328644174835', null, null, '2026-08-10', 96, null, null, 2.0, 5],
  ['Reusable Rect 750ml Pk20', 'KI148447', '9328644148447', '2026-04-30', 144, '2026-05-26', 48, 26, 112, 0.4, 2],
  ['Eco Cutlery Fork Pk24', 'KI090135', '9328644090135', '2026-06-24', 192, '2026-07-28', 96, 34, 52, 1.4, 9],
  ['Glass Storage Jar 1L', 'KI120911', '9328644120911', '2026-05-12', 96, '2026-08-19', 96, 99, 72, 2.2, 4],
  ['Microfibre Cloth 4pk', 'CL033120', '9328644033120', '2026-06-02', 240, '2026-08-04', 240, 63, 228, 4.2, 6],
] as const
const daily = (from: string, base: number, seed: number) => {
  const out: LocalSupplierPurchaseSalesDailyPointDto[] = []
  for (let t = Date.parse(`${from}T00:00:00Z`), i = 0; t <= Date.parse(`${TODAY}T00:00:00Z`); t += 86_400_000, i += 1) {
    const day = new Date(t)
    const weekend = [0, 6].includes(day.getUTCDay()) ? 1.8 : 1
    const noise = ((i * seed * 7919) % 17) / 17
    out.push({ date: day.toISOString().slice(0, 10), quantity: Math.round(base * weekend * (0.3 + noise * 1.5)) })
  }
  return out
}
const shift = (date: string, days: number) => new Date(Date.parse(`${date}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10)
const items = rows.map(([productName, itemNumber, barcode, previousPurchaseDate, previousPurchaseQty, latestPurchaseDate, latestPurchaseQty, purchaseIntervalDays, salesBetweenPurchases, base, seed]) => ({
  storeCode: 'B1', storeName: 'Springfield', productCode: `P-${itemNumber}`, itemNumber, barcode, productName, productImage: '',
  supplierCode: 'BRZ', supplierName: 'Brazco', salesQty30: 0, salesQty60: 0, salesQty90: 0, salesStatisticLastUpdate: '2026-09-18T06:03:49',
  previousPurchaseDate, previousPurchaseQty, latestPurchaseDate, latestPurchaseQty, purchaseIntervalDays, salesBetweenPurchases,
  // 图表窗口从上次进货（无则最近进货前 30 天）开始，让两次进货都落在图内。
  dailySales: daily(previousPurchaseDate ?? shift(latestPurchaseDate, -30), base, seed),
  // 总销量与后端口径一致：最近进货当天起的累计净销量。
  totalSalesSinceLatestPurchase: daily(previousPurchaseDate ?? shift(latestPurchaseDate, -30), base, seed)
    .filter(point => point.date >= latestPurchaseDate)
    .reduce((sum, point) => sum + point.quantity, 0),
  purchases: [...(previousPurchaseDate ? [{ date: previousPurchaseDate, quantity: previousPurchaseQty! }] : []), { date: latestPurchaseDate, quantity: latestPurchaseQty }],
}))

const originalFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const url = String(input)
  if (!url.includes('/api/')) return originalFetch(input, init)
  const json = (data: unknown) => new Response(JSON.stringify({ success: true, data }), { headers: { 'content-type': 'application/json' } })
  if (/\/api\/Users\/guid\/[^/]+\/stores/.test(url)) return json([store])
  if (url.includes('/warehouse-categories/tree')) return json([])
  if (url.includes('/store-order/cart/')) return json({ items: [], totalQuantity: 0, totalAmount: 0 })
  if (url.includes('/preorders/active')) return json({ normalOrderBlocked: false, activations: [] })
  if (url.includes('/purchase-sales-analysis/store-options')) return json([{ label: 'Springfield', value: 'B1' }, { label: 'Sunnybank', value: 'B2' }])
  if (url.includes('/purchase-sales-analysis/supplier-options')) return json(suppliers)
  if (url.includes('/purchase-sales-analysis')) {
    await new Promise(resolve => setTimeout(resolve, 150))
    const params = new URL(url, location.origin).searchParams
    const keyword = (params.get('keyword') ?? '').trim().toLowerCase()
    const filtered = items.filter(item => !keyword || `${item.productName} ${item.itemNumber} ${item.barcode}`.toLowerCase().includes(keyword))
    // 排序在服务端完成：按请求的 sortBy/sortOrder 重排整页，模拟真实后端行为。
    const sortBy = params.get('sortBy') ?? 'latestPurchaseDate'
    const descending = (params.get('sortOrder') ?? 'desc') !== 'asc'
    filtered.sort((left, right) => {
      const pick = (row: typeof left) => (sortBy === 'totalSalesSinceLatestPurchase' ? row.totalSalesSinceLatestPurchase : row.latestPurchaseDate)
      const a = pick(left), b = pick(right)
      const diff = typeof a === 'number' && typeof b === 'number' ? a - b : String(a).localeCompare(String(b))
      return descending ? -diff : diff
    })
    return json({ items: filtered, total: filtered.length, page: 1, pageSize: 100, salesStatisticLastUpdate: '2026-09-18T06:03:49',
      calculationNote: '进货按订单日期范围过滤、按进货发生日期汇总；日销量从最近进货当天起逐日统计。' })
  }
  return json({})
}

const language = new URLSearchParams(location.search).get('lang') === 'en' ? 'en' : 'zh'
await i18n.changeLanguage(language)
const adminMode = new URLSearchParams(location.search).get('mode') === 'admin'
// 前台：订货员仅前台权限、一家门店；后台：仅持销售看板「分店进货销量分析」权限的用户。
const user = adminMode
  ? { userGUID: 'purchase-analysis-admin-preview', username: '本地验收', email: '', roleNames: [], storeNames: ['Springfield', 'Sunnybank'],
      permissions: ['SalesDashboard.LocalSupplierPurchaseSales.View'], exactPermissions: ['SalesDashboard.LocalSupplierPurchaseSales.View'],
      stores: [store, { ...store, storeGUID: 'store-b2', storeName: 'Sunnybank', storeCode: 'B2' }] }
  : { userGUID: 'shop-purchase-analysis-preview', username: 'springfield.staff', email: '',
      permissions: ['OrderFront'], exactPermissions: ['OrderFront'], roleNames: ['订货员'], storeNames: ['Springfield'], stores: [store] }
useAuthStore.setState({ currentUser: user, access: buildAccess(user), initialized: true })

ReactDOM.createRoot(document.getElementById('root')!).render(
  <ConfigProvider locale={language === 'en' ? enUS : zhCN} theme={{ token: { colorPrimary: '#1677ff', borderRadius: 6,
    fontFamily: '-apple-system,BlinkMacSystemFont,Segoe UI,PingFang SC,sans-serif' } }}>
    <div style={{ padding: '4px 18px', background: '#fff8e7', color: '#775b22', fontSize: 12 }}>本地交互验收 · 演示数据 · {adminMode ? '后台 AdminLayout + 销售看板「分店进货销量分析」' : '订货前台 ShopLayout + 「进货销量分析」'}</div>
    {adminMode ? (
      <MemoryRouter initialEntries={['/executive-sales-intelligence/local-supplier-purchase-sales-analysis']}><AdminLayout /></MemoryRouter>
    ) : (
      <MemoryRouter initialEntries={['/shop/purchase-sales-analysis']}>
        <Routes>
          <Route path="/shop" element={<ShopPreorderLeaveProvider><ShopLayout /></ShopPreorderLeaveProvider>}>
            <Route path="purchase-sales-analysis" element={<ShopPurchaseSalesAnalysisPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    )}
  </ConfigProvider>,
)

// ?auto=1：自动选择供应商并搜索（首行自动展开日销量图），供无头浏览器整页截图。
if (new URLSearchParams(location.search).get('auto') === '1') {
  const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))
  const waitFor = async <T,>(probe: () => T | null | undefined, timeoutMs = 20_000): Promise<T> => {
    for (let elapsed = 0; elapsed < timeoutMs; elapsed += 200) { const value = probe(); if (value) return value; await sleep(200) }
    throw new Error('preview-auto: 等待超时')
  }
  void (async () => {
    const pick = async (index: number, title: string) => {
      const selector = await waitFor(() => document.querySelectorAll<HTMLElement>('.ant-card .ant-select .ant-select-selector')[index])
      selector.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
      ;(await waitFor(() => document.querySelector<HTMLElement>(`.ant-select-item-option[title="${title}"]`))).click()
      await sleep(400)
    }
    if (adminMode) await pick(0, 'Springfield')
    await pick(adminMode ? 1 : 0, 'Brazco')
    await sleep(300)
    ;(await waitFor(() => [...document.querySelectorAll('button')].find(button => /搜索|查询|Search/.test(button.textContent ?? '') && !button.hasAttribute('disabled')))).click()
    await waitFor(() => (/共 8|8 rows|of 8/.test(document.querySelector('.ant-pagination')?.textContent ?? '') ? true : null))
    // ?sort=total 时按总销量降序，用于验收排序效果。
    if (new URLSearchParams(location.search).get('sort') === 'total') {
      const header = [...document.querySelectorAll<HTMLElement>('.ant-table-thead th')].find(item => /日销量与进货|Daily Sales/.test(item.innerText))
      header?.click()
      await sleep(1600)
    }
    // 后台表格不默认展开，自动点开第一行以便截图里包含大图。
    if (adminMode) document.querySelector<HTMLElement>('.ant-table-row-expand-icon')?.click()
    await sleep(600)
    document.body.setAttribute('data-preview-ready', '1')
  })().catch(error => console.error('[preview-auto] failed', error))
}
