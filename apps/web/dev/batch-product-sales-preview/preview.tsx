import ReactDOM from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { ConfigProvider } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import enUS from 'antd/locale/en_US'
import 'antd/dist/reset.css'
import '../../src/styles/global.css'
import i18n from '../../src/i18n'
import AdminLayout from '../../src/layout/AdminLayout'
import { useAuthStore } from '../../src/store/auth'
import { buildAccess } from '../../src/utils/access'
import type { BatchSalesMetrics, BatchSalesProduct, BatchSalesScope } from '../../src/types/batchProductSalesAnalysis'

// 独立本地验收入口，未登记生产路由；模拟接口仅在本文件加载时启用。
const qa = (window as any).__batchSalesQA = {
  requests: [] as any[], errors: [] as string[], delay: 120, fail: '', discountState: 'Fresh', statisticState: 'Fresh', productDelays: {} as Record<string, number>,
}
window.addEventListener('error', event => qa.errors.push(event.message))
window.addEventListener('unhandledrejection', event => qa.errors.push(String(event.reason)))
const stores = [
  { code: 'B1', name: 'Springfield' }, { code: 'B2', name: 'Sunnybank' },
  { code: 'B3', name: 'Browns Plains' }, { code: 'B4', name: 'Logan Central' },
]
const definitions: Array<[string, string, number, number]> = [
  ['001236', '透明收纳盒 1.5L', 268, 72], ['HB24018', '厨房清洁海绵 6片装', 186, 44],
  ['006821', '不锈钢保温杯 500ml', 142, 24], ['HB23056', '防滑衣架 10只装', 98, 26],
  ['000875', '抽屉分隔收纳盒', 64, 14], ['HB25012', '微纤维清洁布 4片装', 52, 14],
  ['009102', '食品密封夹 12只装', 31, 7], ['HB22034', '陶瓷马克杯', 0, 0],
]
// 54 行验收独立滚动；仅本地预览使用。
for (let index = 9; index <= 54; index += 1) definitions.push([`QA-${String(index).padStart(3, '0')}`, `滚动验收商品 ${index}`, index, 0])
Object.assign(qa, { itemNumbers: definitions.map(item => item[0]) })
const products: BatchSalesProduct[] = definitions.map(([itemNumber, productName], index) => ({
  productCode: `P${index + 1}`, itemNumber, productName, englishName: `Preview product ${index + 1}`,
}))
const allocate = (total: number, weights: number[]) => {
  const sum = weights.reduce((a, b) => a + b, 0)
  let cumulative = 0
  return weights.map(weight => {
    const before = Math.round(total * cumulative / sum)
    cumulative += weight
    return Math.round(total * cumulative / sum) - before
  })
}
const zero = (): BatchSalesMetrics => ({
  quantity: 0, regularQuantity: 0, discountQuantity: 0, unknownQuantity: 0, returnQuantity: 0,
  salesAmount: 0, discountStatus: 'complete', originalPriceMin: null, originalPriceMax: null,
  discountPriceMin: null, discountPriceMax: null,
})
const add = (rows: BatchSalesMetrics[]) => rows.reduce((sum, row) => ({
  ...sum, quantity: sum.quantity + row.quantity, regularQuantity: sum.regularQuantity + row.regularQuantity,
  discountQuantity: sum.discountQuantity + row.discountQuantity, unknownQuantity: sum.unknownQuantity + row.unknownQuantity,
  returnQuantity: sum.returnQuantity + row.returnQuantity, salesAmount: sum.salesAmount + row.salesAmount,
  originalPriceMin: row.originalPriceMin ?? sum.originalPriceMin, originalPriceMax: row.originalPriceMax ?? sum.originalPriceMax,
  discountPriceMin: row.discountPriceMin ?? sum.discountPriceMin, discountPriceMax: row.discountPriceMax ?? sum.discountPriceMax,
}), zero())
const facts = products.flatMap((product, productIndex) => {
  const quantity = definitions[productIndex][2], discount = definitions[productIndex][3]
  const regularByBranch = productIndex === 0 ? [60, 54, 39, 43] : allocate(quantity - discount, [4, 3, 2, 1])
  const discountByBranch = productIndex === 0 ? [26, 18, 22, 6] : allocate(discount, [4, 3, 2, 1])
  return stores.flatMap((store, index) => {
    const regular = allocate(regularByBranch[index], [12, 18, 20, 16, 14, 12, 18, 12, 18, 14, 10, 9, 23])
    const discounted = allocate(discountByBranch[index], [0, 0, 0, 0, 16, 26, 18, 12, 0, 0, 0, 0, 0])
    return regular.map((value, day) => ({
      productCode: product.productCode, branchCode: store.code,
      date: `2026-09-${String(day + 1).padStart(2, '0')}`,
      metrics: { ...zero(), quantity: value + discounted[day], regularQuantity: value,
        discountQuantity: discounted[day], salesAmount: value * 6 + discounted[day] * 4.5,
        originalPriceMin: value + discounted[day] > 0 ? 6 : null, originalPriceMax: value + discounted[day] > 0 ? 6 : null,
        discountPriceMin: discounted[day] > 0 ? 4.5 : null, discountPriceMax: discounted[day] > 0 ? 4.5 : null },
    }))
  })
})
const effectiveScope = (input: BatchSalesScope): BatchSalesScope => ({
  startDate: input.startDate, endDate: input.endDate,
  storeCodes: input.storeCodes?.length ? input.storeCodes : stores.map(store => store.code),
})
const scopedFacts = (scope: BatchSalesScope, productCode: string) => facts.filter(row =>
  row.productCode === productCode && row.date >= scope.startDate && row.date <= scope.endDate && scope.storeCodes.includes(row.branchCode))
const dayRows = (scope: BatchSalesScope, rows: typeof facts) => {
  const result = []
  for (let current = Date.parse(`${scope.startDate}T00:00:00Z`); current <= Date.parse(`${scope.endDate}T00:00:00Z`); current += 86_400_000) {
    const date = new Date(current).toISOString().slice(0, 10)
    result.push({ date, metrics: add(rows.filter(row => row.date === date).map(row => row.metrics)) })
  }
  return result
}
const originalFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const url = String(input)
  if (!url.includes('/api/')) return originalFetch(input, init)
  if (!url.includes('/batch-product-sales-analysis/')) return new Response(JSON.stringify({ success: true, data: {} }), { headers: { 'content-type': 'application/json' } })
  const endpoint = url.split('/').at(-1)!, body = init?.body ? JSON.parse(String(init.body)) : {}
  const record = { endpoint, body, aborted: false, finished: false }
  qa.requests.push(record)
  await new Promise<void>((resolve, reject) => {
    const cancel = () => { clearTimeout(timer); record.aborted = true; reject(new DOMException('Aborted', 'AbortError')) }
    const timer = setTimeout(() => { init?.signal?.removeEventListener('abort', cancel); resolve() }, qa.productDelays[body.productCode] ?? qa.delay)
    if (init?.signal?.aborted) cancel()
    else init?.signal?.addEventListener('abort', cancel, { once: true })
  })
  record.finished = true
  if (qa.fail === endpoint) return new Response(JSON.stringify({ success: false, message: '演示接口：临时读取失败' }), { status: 503, headers: { 'content-type': 'application/json' } })
  let data: unknown
  if (endpoint === 'options') data = { stores, maxItemNumbers: 500, maxDays: 366 }
  else {
    const scope = effectiveScope(body)
    if (endpoint === 'query') {
      const matches = body.itemNumbers.map((value: string) => {
        const found = products.filter(product => product.itemNumber.toLowerCase() === value.toLowerCase())
        return { itemNumber: value, status: value === 'DUPLICATE' ? 'ambiguous' : found.length ? 'matched' : 'notFound', productCodes: value === 'DUPLICATE' ? ['P8', 'P9'] : found.map(product => product.productCode) }
      })
      const matchedCodes = new Set(matches.filter((row: any) => row.status === 'matched').flatMap((row: any) => row.productCodes))
      data = { ...scope, matches, products: products.filter(product => matchedCodes.has(product.productCode)).map(product => ({ ...product,
        quantity: add(scopedFacts(scope, product.productCode).map(row => row.metrics)).quantity })), warnings: [], statisticStatus: 'Fresh' }
    } else if (endpoint === 'detail') {
      const product = products.find(product => product.productCode === body.productCode)!
      const rows = scopedFacts(scope, product.productCode)
      data = { ...scope, product, metrics: add(rows.map(row => row.metrics)), daily: dayRows(scope, rows),
        branches: stores.filter(store => scope.storeCodes.includes(store.code)).map(store => {
          const branchFacts = rows.filter(row => row.branchCode === store.code)
          return { branchCode: store.code, branchName: store.name, metrics: add(branchFacts.map(row => row.metrics)), daily: dayRows(scope, branchFacts) }
        }), warnings: [] }
    }
  }
  if (endpoint === 'detail' && data) {
    const result = data as any
    result.statisticStatus = qa.statisticState
    result.discountStatisticStatus = qa.discountState
    if (qa.discountState !== 'Fresh') {
      const pending = (m: BatchSalesMetrics) => ({ ...m, regularQuantity: 0, discountQuantity: 0, unknownQuantity: m.quantity, returnQuantity: 0, discountStatus: 'pending', originalPriceMin: null, originalPriceMax: null, discountPriceMin: null, discountPriceMax: null })
      result.metrics = pending(result.metrics)
      result.daily.forEach((day: any) => { day.metrics = pending(day.metrics) })
      result.branches.forEach((branch: any) => { branch.metrics = pending(branch.metrics); branch.daily.forEach((day: any) => { day.metrics = pending(day.metrics) }) })
    }
    if (qa.statisticState !== 'Fresh') { result.daily = []; result.branches = [] }
  }
  return new Response(JSON.stringify({ success: true, data }), { headers: { 'content-type': 'application/json' } })
}
const language = new URLSearchParams(location.search).get('lang') === 'en' ? 'en' : 'zh'
await i18n.changeLanguage(language)
const user = { userGUID: 'batch-sales-preview', username: '本地验收', email: '',
  permissions: ['SalesDashboard.BatchProductSales.View'], exactPermissions: ['SalesDashboard.BatchProductSales.View'],
  roleNames: [], storeNames: stores.map(store => store.name) }
useAuthStore.setState({ currentUser: user, access: buildAccess(user), initialized: true })
ReactDOM.createRoot(document.getElementById('root')!).render(
  <ConfigProvider locale={language === 'en' ? enUS : zhCN} theme={{ token: { colorPrimary: '#1677ff', borderRadius: 6,
    fontFamily: '-apple-system,BlinkMacSystemFont,Segoe UI,PingFang SC,sans-serif' } }}>
    <div style={{ padding: '4px 18px', background: '#fff8e7', color: '#775b22', fontSize: 12 }}>本地交互验收 · 演示数据 · 使用正式页面、客户端与权限菜单</div>
    <MemoryRouter initialEntries={['/executive-sales-intelligence/batch-product-sales-analysis']}><AdminLayout /></MemoryRouter>
  </ConfigProvider>,
)
