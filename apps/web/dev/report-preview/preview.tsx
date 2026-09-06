import ReactDOM from 'react-dom/client'
import { MemoryRouter, useLocation, useNavigate, useRoutes } from 'react-router-dom'
import { ConfigProvider } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import enUS from 'antd/locale/en_US'
import dayjs from 'dayjs'
import 'antd/dist/reset.css'
import '../../src/i18n'
import i18n from '../../src/i18n'
import { useAuthStore } from '../../src/store/auth'
import { buildAccess } from '../../src/utils/access'
import SalesDetail from '../../src/pages/ExecutiveSalesIntelligence/SalesDetailAnalysisV2'
import Revenue from '../../src/pages/ExecutiveSalesIntelligence'
import RouteKeepAlive from '../../src/components/RouteKeepAlive'
import { quickDateSelection } from '../../src/pages/ExecutiveSalesIntelligence/ReportWorkbench/logic'
import { computeReport, products } from '../../../../docs/design/sales-detail-three-column-v2/model.mjs'

// 仅本地独立验收入口；不会进入生产路由或构建。模拟数据不得用于 3 秒真实接口验收。
const qa = window.__reportQA = { requests: [], delay: 120, failSection: '', pendingSection: '', version: 'qa-v1' }
const originalFetch = window.fetch.bind(window)
const metric = row => ({ ...row, orderCount: row.orders ?? null, compareOrderCount: row.compareOrders ?? null,
  averageTransaction: row.aov, compareAverageTransaction: row.compareAov, averageUnitPrice: row.aov, compareAverageUnitPrice: row.compareAov,
  grossMarginRate: row.margin == null ? null : row.margin / 100, compareGrossMarginRate: row.compareMargin == null ? null : row.compareMargin / 100,
  share: row.share == null ? null : row.share / 100, compareShare: row.compareShare == null ? null : row.compareShare / 100,
  chinaShare: row.chinaShare == null ? null : row.chinaShare / 100, compareChinaShare: row.compareChinaShare == null ? null : row.compareChinaShare / 100 })
const totalNode = (base, children) => {
  const totals = children.reduce((sum, row) => ({revenue:sum.revenue+row.revenue,revenueLY:sum.revenueLY+row.revenueLY,orders:sum.orders+row.orders,ordersLY:sum.ordersLY+row.ordersLY}),{revenue:0,revenueLY:0,orders:0,ordersLY:0})
  return {...base,...totals,aov:totals.orders ? totals.revenue/totals.orders : 0,aovLY:totals.ordersLY ? totals.revenueLY/totals.ordersLY : 0,children}
}
// 演示周树与筛选区间/分店保持一致；仅用于交互预览，不冒充真实日统计。
const makeWeeks = (rows, start, end) => {
  const days = Math.min(366, dayjs(end).diff(dayjs(start),'day')+1)
  const groups = new Map()
  const part = (value,index,scale=1) => (Math.round(value*scale*(index+1)/days)-Math.round(value*scale*index/days))/scale
  for(let i=0;i<days;i++) {
    const date=dayjs(start).add(i,'day'), week=`${date.isoWeekYear()}-${String(date.isoWeek()).padStart(2,'0')}`
    if(!groups.has(week)) groups.set(week,new Map())
    for(const b of rows) {
      const branches=groups.get(week)
      if(!branches.has(b.branchCode)) branches.set(b.branchCode,{b,children:[]})
      const revenue=part(b.revenue,i,100), revenueLY=part(b.revenueLY,i,100), orders=part(b.orderCount,i), ordersLY=part(b.orderCountLY,i)
      branches.get(b.branchCode).children.push({key:`w${week}-${b.branchCode}-${date.format('YYYYMMDD')}`,level:'date',hierarchy:date.format('YYYY-MM-DD'),revenue,revenueLY,orders,ordersLY,aov:orders?revenue/orders:0,aovLY:ordersLY?revenueLY/ordersLY:0})
    }
  }
  return [...groups].map(([week,branches])=>totalNode({key:`w${week}`,level:'week',hierarchy:week.replace('-','-W')},[...branches.values()].map(({b,children})=>totalNode({key:`w${week}-${b.branchCode}`,level:'branch',hierarchy:b.branchName},children))))
}
window.fetch = async (url, init) => {
  if (!String(url).includes('/api/react/v1/dashboard/')) return String(url).startsWith('/api/') ? new Response('{}', {headers:{'content-type':'application/json'}}) : originalFetch(url, init)
  const parsed = new URL(String(url), location.origin), p = parsed.searchParams
  const section = p.get('section') || parsed.pathname.split('/').at(-1)
  const record = { section, query: Object.fromEntries(p), aborted: false, settled: false }
  qa.requests.push(record)
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => {record.settled = true;resolve()}, qa.delay)
    init?.signal?.addEventListener('abort', () => { if(record.settled) return; clearTimeout(timer); record.aborted = true; reject(new DOMException('Aborted', 'AbortError')) }, { once: true })
  })
  if (qa.failSection === section) return new Response(JSON.stringify({ success: false, message: '验收模拟：读取失败' }), { status: 503, headers: { 'content-type': 'application/json' } })
  const range = ['today','yesterday','thisWeek','lastWeek','thisMonth','lastMonth'].find(key => {const candidate=quickDateSelection(key);return candidate.startDate===p.get('startDate')&&candidate.endDate===p.get('endDate')}) || (dayjs(p.get('endDate')).diff(dayjs(p.get('startDate')),'day') < 2 ? 'today' : 'thisWeek')
  const report = computeReport({ kind: p.get('kind') || 'china', range, compareMode:p.get('compareMode')==='ByDate'?'date':'week', autoCompare: p.has('compareStartDate'),
    selectedSupplier: p.get('selectedSupplierCode'), selectedBranch: p.get('selectedBranchCode')?.replace(/^B/, 'branch-'),
    selectedProduct: products.find(x => x.code === p.get('selectedProductCode'))?.id,
    search: p.get('search'), page: Number(p.get('pageIndex') || 1), pageSize: Number(p.get('pageSize') || 20) })
  let data
  if (section === 'suppliers') data = { rows: report.supplierRows.map(metric), total: report.supplierRows.length }
  if (section === 'branches') data = { rows: report.branchRows.map(metric), total: report.branchRows.length }
  if (section === 'products') data = { rows: report.productRows.map(metric), total: report.productTotal, summary: metric(report.pageSummary) }
  if (section === 'summary') data = { rows: [], total: 1, summary: metric(report.summary) }
  const requestedBranches=p.getAll('branchCodes')
  const branchData = report.branchRows.filter(x=>!requestedBranches.length||requestedBranches.includes(x.code)).map((x, i) => ({ rank:i+1, branchCode:x.code, branchName:x.name, revenue:x.revenue, revenueLY:x.compareRevenue ?? 0, orderCount:x.orders, orderCountLY:x.compareOrders ?? 0, aov:x.aov, aovLY:x.compareAov ?? 0 }))
  if (section === 'executive-branch-performance') data = branchData
  if (section === 'executive-hourly-traffic') data = ['08:00','09:00','10:00','11:00','12:00','13:00','14:00','15:00'].map((hour,i) => ({ hour,revenue:(i+1)*375*(p.has('branchCodes')?0.18:1),revenueLY:(i+1)*340,percentage:80,isPeak:false }))
  if (section === 'weekly-performance-hierarchy') data = makeWeeks(branchData,p.get('startDate'),p.get('endDate'))
  return new Response(JSON.stringify({ success:true,data,statisticStatus:qa.pendingSection === section ? 'Pending' : 'Fresh',statisticMessage:qa.pendingSection === section ? '验收模拟：统计准备中' : null,cacheVersion:qa.version,statisticUpdatedAt:'2026-09-06T06:00:00Z' }), { headers: { 'content-type':'application/json' } })
}
const user = { userGUID:'report-preview',username:'preview',email:'',permissions:[],roleNames:['Admin'],storeNames:[] }
useAuthStore.setState({ currentUser:user, access:buildAccess(user), initialized:true })
i18n.changeLanguage(new URLSearchParams(location.search).get('lang') || 'zh')
const page = new URLSearchParams(location.search).get('page') === 'overview' ? 'overview' : 'sales-detail-v2'
function PreviewRoutes() {
  const current = useLocation()
  const navigate = useNavigate()
  window.__reportQA.route = current.pathname + current.search
  const element = useRoutes([{path:'/executive-sales-intelligence/sales-detail-v2',element:<SalesDetail/>},{path:'/executive-sales-intelligence/overview',element:<Revenue/>}])
  return <><div style={{background:'#fff8e7',color:'#775b22',padding:'8px 22px',fontSize:12,display:'flex',gap:20,alignItems:'center'}}>正式 Web 组件 · 演示数据 / 不代表真实接口性能<button onClick={() => navigate(-1)}>返回上一页</button></div><RouteKeepAlive activeKey={current.pathname} include={['/executive-sales-intelligence/sales-detail-v2','/executive-sales-intelligence/overview']} currentElement={element!}/></>
}
ReactDOM.createRoot(document.getElementById('root')!).render(<ConfigProvider locale={new URLSearchParams(location.search).get('lang') === 'en' ? enUS : zhCN} theme={{ token:{colorPrimary:'#003670',fontFamily:'-apple-system,BlinkMacSystemFont,Segoe UI,PingFang SC,sans-serif',borderRadius:6} }}>
  <MemoryRouter initialEntries={[`/executive-sales-intelligence/${page}${location.search}`]}><PreviewRoutes/></MemoryRouter>
</ConfigProvider>)
