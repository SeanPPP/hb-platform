import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { Alert, Button, Input, Pagination, Segmented, Skeleton, Tag, Tooltip } from 'antd'
import { CloseOutlined, FullscreenExitOutlined, FullscreenOutlined, SearchOutlined } from '@ant-design/icons'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../../store/auth'
import { MetricPair, ReportControls, useReportText } from '../ReportWorkbench/ReportControls'
import { growth, reportPeriod } from '../ReportWorkbench/logic'
import { useReportQuery, type ReportQueryState } from '../ReportWorkbench/useReportQuery'
import { applyKeyword, emptySelection, initialDetailState, resizeColumns, selectDimension, sumProductPage } from './logic'
import { fetchSalesDetailSection, sectionQuery, type ReportSection, type SalesDetailPage, type SalesDetailQuery, type SalesDetailRow } from './reportService'
import styles from './styles.module.css'

type MetricKey = 'revenue' | 'grossProfit' | 'grossMarginRate' | 'orderCount' | 'averageTransaction' | 'quantity' | 'averageUnitPrice' | 'share' | 'chinaShare'
type PanelKey = 'suppliers' | 'branches' | 'products'
type Sort = { key: MetricKey; ascending: boolean }

function GrowthCell({ current, previous, compare }: { current: number; previous: number | null; compare: boolean }) {
  const text = useReportText()
  const value = compare ? growth(current, previous) : null
  return <span className={typeof value === 'number' ? value > 0 ? styles.positive : value < 0 ? styles.negative : styles.muted : styles.muted}>
    {value === null ? '—' : value === 'new' ? text('新增', 'New') : `${value > 0 ? '+' : ''}${(value * 100).toFixed(1)}%`}
  </span>
}

function Panel({ id, title, subtitle, count, query, expanded, onExpand, filter, children, footer, onRetry }: {
  id: PanelKey; title: string; subtitle: string; count?: number; query: ReportQueryState<SalesDetailPage>
  expanded: boolean; onExpand: () => void; filter: ReactNode; children: ReactNode; footer?: ReactNode; onRetry: () => void
}) {
  const text = useReportText()
  return <section data-panel={id} className={`${styles.panel} ${expanded ? styles.expanded : ''}`} aria-busy={query.loading}>
    <header className={styles.panelHeader}><div><h2><span>{id === 'suppliers' ? '01' : id === 'branches' ? '02' : '03'}</span>{title}<small>{count ?? '—'}</small></h2><p>{subtitle}</p></div>
      <Button type="text" size="small" icon={expanded ? <FullscreenExitOutlined /> : <FullscreenOutlined />} onClick={onExpand}
        aria-label={expanded ? text(`收起${title}`, `Collapse ${title}`) : text(`展开${title}`, `Expand ${title}`)} /></header>
    <div className={styles.panelFilter}>{filter}</div>
    {query.slow && query.loading && <div className={styles.slow} role="status">{text('查询超过 3 秒，正在读取完整数据…', 'Over 3 seconds. Loading complete data…')}</div>}
    <div className={styles.scroll} tabIndex={0} aria-label={text(`${title}可滚动表格`, `${title} scrollable table`)}>
      {query.error ? <div className={styles.empty}><Alert type="warning" message={query.error} /><Button onClick={onRetry}>{text('重试', 'Retry')}</Button></div>
        : query.loading ? <div className={styles.skeleton}><Skeleton active paragraph={{ rows: 7 }} title={false} /><p>{query.snapshot?.statisticMessage}</p></div>
          : count === 0 ? <div className={styles.empty}>{text('当前条件下没有数据', 'No data for these filters')}</div> : children}
    </div>
    {footer && <footer className={styles.panelFooter}>{footer}</footer>}
  </section>
}

export default function SalesDetailAnalysisV2() {
  const text = useReportText()
  const location = useLocation()
  const navigate = useNavigate()
  const { active = true } = useKeepAliveContext()
  const { access, currentUser } = useAuthStore()
  const initial = useMemo(() => initialDetailState(location.search), [location.search])
  const [dates, setDates] = useState(initial.dates)
  const [kind, setKind] = useState(initial.kind)
  const [selection, setSelection] = useState(initial.selection)
  const [keywordDraft, setKeywordDraft] = useState('')
  const [composing, setComposing] = useState(false)
  const [supplierSearch, setSupplierSearch] = useState('')
  const [metricView, setMetricView] = useState('profit')
  const [expanded, setExpanded] = useState<PanelKey | null>(null)
  const [widths, setWidths] = useState([28, 27, 45])
  const [refresh, setRefresh] = useState<Record<ReportSection, number>>({ suppliers: 0, branches: 0, products: 0, summary: 0 })
  const retrySection = (section: ReportSection) => setRefresh(value => ({ ...value, [section]: value[section] + 1 }))
  const refreshAll = () => setRefresh(value => ({ suppliers: value.suppliers + 1, branches: value.branches + 1, products: value.products + 1, summary: value.summary + 1 }))
  const [sorts, setSorts] = useState<Record<'suppliers' | 'branches', Sort>>({ suppliers: { key: 'revenue', ascending: false }, branches: { key: 'revenue', ascending: false } })
  const grid = useRef<HTMLDivElement>(null)
  const drag = useRef<{ divider: number; startX: number; widths: number[]; width: number }>()
  const selectedNames = useRef<Record<string, string>>({})
  const searchClear = useRef(false)
  const appliedSearch = useRef(location.search)

  useEffect(() => {
    // KeepAlive 隐藏期间不消费其他页面的 URL；返回相同地址时保留三栏筛选。
    if (!active || !location.pathname.endsWith('/sales-detail-v2') || appliedSearch.current === location.search) return
    appliedSearch.current = location.search
    setDates(initial.dates); setKind(initial.kind); setSelection(initial.selection); setKeywordDraft(''); setSupplierSearch('')
  }, [active, initial, location.pathname, location.search])
  useEffect(() => {
    if (composing) return
    const timer = window.setTimeout(() => {
      if (searchClear.current) { searchClear.current = false; return }
      setSelection(value => applyKeyword(value, keywordDraft))
    }, 300)
    return () => window.clearTimeout(timer)
  }, [keywordDraft, composing])
  useEffect(() => {
    const handle = (event: KeyboardEvent) => { if (event.key === 'Escape') setExpanded(null) }
    window.addEventListener('keydown', handle)
    return () => window.removeEventListener('keydown', handle)
  }, [])
  useEffect(() => { grid.current?.querySelectorAll(`.${styles.scroll}`).forEach(node => { node.scrollLeft = 0 }) }, [metricView])

  const branches = useMemo(() => access.managedStoreCodes?.() ?? undefined, [access])
  const allowed = !!currentUser && (branches === undefined || branches.length > 0)
  const period = useMemo(() => reportPeriod(dates), [dates])
  const query: SalesDetailQuery = { ...period, kind, branchCodes: branches, selectedBranchCode: selection.branch,
    selectedSupplierCode: selection.supplier, selectedProductCode: selection.product, search: selection.keyword || undefined,
    pageIndex: selection.page, pageSize: selection.pageSize }
  const key = (section: ReportSection) => JSON.stringify([currentUser?.userGUID, branches, sectionQuery(section, query)])
  const options = { active, enabled: allowed }
  const suppliers = useReportQuery(key('suppliers'), signal => fetchSalesDetailSection('suppliers', query, signal), { ...options, refresh: refresh.suppliers, metricId: 'sales-detail-suppliers' })
  const stores = useReportQuery(key('branches'), signal => fetchSalesDetailSection('branches', query, signal), { ...options, refresh: refresh.branches, metricId: 'sales-detail-branches' })
  const products = useReportQuery(key('products'), signal => fetchSalesDetailSection('products', query, signal), { ...options, refresh: refresh.products, metricId: 'sales-detail-products' })
  const summary = useReportQuery(key('summary'), signal => fetchSalesDetailSection('summary', query, signal), { ...options, refresh: refresh.summary, metricId: 'sales-detail-summary' })
  const loading = suppliers.loading || stores.loading || products.loading || summary.loading
  const total = summary.data?.summary ?? summary.data?.rows[0]
  const pageTotal = products.data ? products.data.summary ?? sumProductPage(products.data.rows) : undefined
  const versions = new Set([suppliers, stores, products, summary].filter(item => item.data).map(item => item.snapshot?.cacheVersion).filter(Boolean))
  const mismatch = versions.size > 1
  const syncAttempt = useRef(0)
  useEffect(() => { syncAttempt.current = 0 }, [dates, kind, selection.supplier, selection.branch, selection.product, selection.keyword])
  useEffect(() => {
    if (!loading && mismatch && syncAttempt.current < 2) { syncAttempt.current++; refreshAll() }
  }, [loading, mismatch])
  useEffect(() => {
    if (products.data && selection.page > Math.max(1, Math.ceil(products.data.total / selection.pageSize)))
      setSelection(value => ({ ...value, page: Math.max(1, Math.ceil(products.data!.total / value.pageSize)) }))
  }, [products.data, selection.page, selection.pageSize])

  const labels: Record<MetricKey, string> = { revenue: text('营业额', 'Revenue'), grossProfit: text('毛利额', 'Gross profit'), grossMarginRate: text('毛利率', 'Margin'),
    orderCount: text('客单数', 'Transactions'), averageTransaction: text('客单价', 'Basket value'), quantity: text('数量', 'Quantity'),
    averageUnitPrice: text('均价', 'Unit price'), share: text('营业额占比', 'Revenue share'), chinaShare: text('中国货占比', 'China share') }
  const previous: Record<MetricKey, keyof SalesDetailRow> = { revenue: 'compareRevenue', grossProfit: 'compareGrossProfit', grossMarginRate: 'compareGrossMarginRate',
    orderCount: 'compareOrderCount', averageTransaction: 'compareAverageTransaction', quantity: 'compareQuantity', averageUnitPrice: 'compareAverageUnitPrice', share: 'compareShare', chinaShare: 'compareChinaShare' }
  const metrics = (panel: PanelKey): MetricKey[] => {
    const sales: MetricKey[] = panel === 'products' ? ['revenue', 'quantity', 'averageUnitPrice'] : ['revenue', 'orderCount', 'averageTransaction']
    const profit: MetricKey[] = ['grossProfit', 'grossMarginRate']
    return [...(metricView === 'profit' ? [...profit, ...sales] : [...sales, ...profit]), ...(panel === 'suppliers' ? ['share' as const, ...(kind === 'china' ? ['chinaShare' as const] : [])] : [])]
  }
  const metric = (row: SalesDetailRow, field: MetricKey) => <MetricPair current={row[field]} previous={row[previous[field]] as number | null}
    compare={dates.compare} revenue={row.revenue} compareRevenue={row.compareRevenue} costMetric={field === 'grossMarginRate' || field === 'grossProfit'} format={field.includes('Share') || field === 'share' || field === 'grossMarginRate' ? 'rate' : field === 'quantity' || field === 'orderCount' ? 'integer' : 'money'} />
  const pick = (dimension: 'supplier' | 'branch' | 'product', row: SalesDetailRow) => {
    selectedNames.current[`${dimension}:${row.code}`] = row.name
    setSelection(value => selectDimension(value, dimension, row.code))
  }
  const clear = () => { setSelection({ ...emptySelection, pageSize: selection.pageSize }); setKeywordDraft(''); setSupplierSearch('') }
  const switchKind = (value: typeof kind) => {
    if (value === kind) return
    setKind(value); clear()
    // 类别切换会清空三维筛选，日期与对比设置保持；刷新页面仍回到当前类别。
    const params = new URLSearchParams({ kind: value, startDate: dates.startDate, endDate: dates.endDate,
      compare: String(dates.compare), compareMode: dates.compareMode })
    appliedSearch.current = `?${params}`
    navigate({ pathname: location.pathname, search: appliedSearch.current }, { replace: true })
  }
  const sortedRows = (panel: 'suppliers' | 'branches', rows: SalesDetailRow[]) => [...rows].sort((a, b) => {
    const { key, ascending } = sorts[panel]
    if (a[key] == null) return b[key] == null ? a.code.localeCompare(b.code) : 1
    if (b[key] == null) return -1
    return (a[key]! - b[key]!) * (ascending ? 1 : -1) || a.code.localeCompare(b.code)
  })
  const supplierRows = sortedRows('suppliers', (suppliers.data?.rows ?? []).filter(row => `${row.name} ${row.code}`.toLowerCase().includes(supplierSearch.trim().toLowerCase())))
  const storeRows = sortedRows('branches', stores.data?.rows ?? [])
  const productRows = products.data?.rows ?? []
  const table = (panel: PanelKey, rows: SalesDetailRow[]) => {
    const dimension = panel === 'suppliers' ? 'supplier' : panel === 'branches' ? 'branch' : 'product'
    return <table className={styles.table}><thead><tr><th>{panel === 'products' ? text('货号 / 商品名称', 'Item / Product') : panel === 'suppliers' ? text('供应商 / 编码', 'Supplier / Code') : text('分店名称', 'Store')}</th>
      {metrics(panel).map(field => <th key={field} aria-sort={panel !== 'products' && sorts[panel].key === field ? sorts[panel].ascending ? 'ascending' : 'descending' : undefined}>
        <Tooltip title={field === 'share' ? text(kind === 'china' ? '分母：所选分店的国内供应商全量营业额，不受商品选择影响' : '分母：所选分店的全部营业额，不受商品选择影响', 'Denominator: all revenue in the selected store scope; not narrowed by product selection') : field === 'chinaShare' ? text('分母：所选分店的全部营业额', 'Denominator: all revenue in the selected store scope') : field === 'orderCount' ? text('沿用移动端客单数；无法去重的跨供应商汇总显示 —，不累加冒充客单', 'Uses mobile report transactions. Cross-supplier totals are not added as distinct baskets.') : undefined}>
          {panel === 'products' ? <span>{labels[field]}</span> : <button onClick={() => setSorts(value => ({ ...value, [panel]: { key: field, ascending: value[panel].key === field ? !value[panel].ascending : false } }))}>{labels[field]} ↕</button>}
        </Tooltip></th>)}<th>{text('增长率', 'Growth')}</th></tr></thead>
      <tbody>{rows.map((row, index) => <tr key={row.code} className={selection[dimension] === row.code ? styles.selected : ''}>
        <td><button data-code={row.code} aria-pressed={selection[dimension] === row.code} className={styles.nameButton} onClick={() => pick(dimension, row)} title={`${row.name} · ${row.code}`}>
          {panel === 'products' ? row.productImage ? <img src={row.productImage} alt="" loading="lazy" onError={event => { event.currentTarget.style.visibility = 'hidden' }} /> : <span className={styles.imagePlaceholder}>▦</span> : <span className={styles.rank}>{String(index + 1).padStart(2, '0')}</span>}
          <span className={styles.nameText}>{panel === 'products' && <small>{row.itemNumber || row.code}</small>}<strong>{row.name || row.code}</strong>{panel === 'suppliers' && <small>{row.code}</small>}</span>
        </button></td>{metrics(panel).map(field => <td key={field} data-metric={field}>{metric(row, field)}</td>)}<td><GrowthCell current={row.revenue} previous={row.compareRevenue} compare={dates.compare} /></td>
      </tr>)}</tbody></table>
  }
  const resizer = (divider: number) => <div role="separator" tabIndex={0} aria-orientation="vertical" aria-label={text('调整报表列宽', 'Resize report columns')}
    aria-valuemin={18} aria-valuemax={82} aria-valuenow={Math.round(widths[divider])} className={styles.resizer}
    onDoubleClick={() => setWidths([28, 27, 45])}
    onKeyDown={event => { if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') { event.preventDefault(); setWidths(value => resizeColumns(value, divider, event.key === 'ArrowLeft' ? -2 : 2)) } }}
    onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); drag.current = { divider, startX: event.clientX, widths, width: grid.current?.clientWidth || 1 } }}
    onPointerMove={event => { if (drag.current) setWidths(resizeColumns(drag.current.widths, drag.current.divider, (event.clientX - drag.current.startX) / drag.current.width * 100)) }}
    onPointerUp={() => { drag.current = undefined }} onPointerCancel={() => { drag.current = undefined }} />
  const hasFilters = !!(selection.supplier || selection.branch || selection.product || selection.keyword)

  return <main className={`${styles.page} ${metricView === 'profit' ? styles.profitView : ''}`} data-report="sales-detail">
    <div className={styles.heading}><div><span className={styles.eyebrow}>SALES EXPLORER</span><h1>{text('销售明细', 'Sales detail')}</h1><p>{text('供应商、分店与商品双向联动，从任意一栏开始分析。', 'Explore from any supplier, store or product.')}</p></div>
      <Button onClick={() => navigate(`/executive-sales-intelligence/overview?branch=${encodeURIComponent(selection.branch ?? '')}&startDate=${dates.startDate}&endDate=${dates.endDate}&compare=${dates.compare}&compareMode=${dates.compareMode}`)}>{text('营业额报告', 'Revenue report')}</Button></div>
    <ReportControls value={dates} onChange={value => { setDates(value); setSelection(current => ({ ...current, page: 1 })) }} onRefresh={refreshAll} loading={loading} />
    <div className={styles.tabsLine}><div role="tablist" aria-label={text('供应商类别', 'Supplier type')} className={styles.tabs}>
      {(['australia', 'china'] as const).map(value => <button role="tab" key={value} aria-selected={kind === value} onClick={() => switchKind(value)}>{value === 'china' ? text('HB 仓库 · 国内供应商', 'HB warehouse · China') : text('澳洲供应商', 'Australian suppliers')}</button>)}
    </div><Segmented value={metricView} options={[{ value: 'sales', label: text('销售指标', 'Sales') }, { value: 'profit', label: text('毛利指标', 'Profit') }]} onChange={setMetricView} /></div>
    {!allowed && <Alert type="warning" message={text('当前账号没有可查询的分店范围', 'No stores are available for this account')} />}
    {mismatch && <Alert type="info" message={text('统计版本正在对齐；各栏暂不可作为同批次对账。', 'Aligning snapshot versions. Do not reconcile panels until aligned.')} />}
    {summary.error && <Alert type="warning" message={summary.error} action={<Button onClick={() => retrySection('summary')}>{text('重试汇总', 'Retry totals')}</Button>} />}
    <section className={styles.summary} aria-label={text('全量筛选汇总', 'All matching totals')} data-testid="detail-summary">
      {(['revenue', 'orderCount', 'averageTransaction', 'grossProfit', 'grossMarginRate'] as MetricKey[]).map(field => <div key={field}>
        <span>{field === 'revenue' ? text(hasFilters ? '当前筛选营业额' : '当前标签营业额', 'Matching revenue') : labels[field]}</span>
        {total ? metric(total, field) : <strong className={styles.pendingTotal}>—</strong>}
      </div>)}
    </section>
    <div className={styles.scope}><div className={styles.chips}><span>{text('筛选条件', 'Filters')}</span>
      {(['supplier', 'branch', 'product'] as const).map(dimension => selection[dimension] && <Tag closable key={dimension} onClose={() => setSelection(value => ({ ...value, [dimension]: undefined, page: dimension === 'product' ? value.page : 1 }))}>
        {text(dimension === 'supplier' ? '供应商' : dimension === 'branch' ? '分店' : '商品', dimension)}: {selectedNames.current[`${dimension}:${selection[dimension]}`] || selection[dimension]}</Tag>)}
      {selection.keyword && <Tag closable onClose={() => { searchClear.current = true; setKeywordDraft(''); setSelection(value => ({ ...value, keyword: '', page: 1 })) }}>{text('关键字', 'Keyword')}: {selection.keyword}</Tag>}
      {hasFilters ? <Button type="text" size="small" onClick={clear}>{text('清除全部', 'Clear all')}</Button> : <span>{text('当前标签全部供应商 / 全部分店 / 全部商品', 'All suppliers / All stores / All products')}</span>}
    </div><span className={styles.legend}>{text('本期在上 · 同期在下', 'Current above · Previous below')}　|　AUD</span></div>
    <div ref={grid} className={`${styles.grid} ${expanded ? styles.expandedGrid : ''}`} style={{ '--supplier-width': `${widths[0]}fr`, '--branch-width': `${widths[1]}fr`, '--product-width': `${widths[2]}fr` } as CSSProperties}>
      <Panel id="suppliers" title={text('供应商', 'Suppliers')} subtitle={text('点击供应商，联动分店与商品', 'Select a supplier to filter stores and products')} count={suppliers.data ? supplierRows.length : undefined} query={suppliers}
        expanded={expanded === 'suppliers'} onExpand={() => setExpanded(value => value === 'suppliers' ? null : 'suppliers')} onRetry={() => retrySection('suppliers')}
        filter={<Input prefix={<SearchOutlined />} allowClear placeholder={text('搜索供应商名称 / 编码', 'Supplier name / code')} aria-label={text('搜索供应商', 'Search suppliers')} value={supplierSearch} onChange={event => setSupplierSearch(event.target.value)} />}
        footer={<><span>{supplierRows.length} {text('家供应商', 'suppliers')}</span><span>{text('左右滑动查看更多', 'Scroll for more')}</span></>}>{table('suppliers', supplierRows)}</Panel>
      {resizer(0)}
      <Panel id="branches" title={text('分店表现', 'Store performance')} subtitle={text('点击分店，反查供应商与商品', 'Select a store to find suppliers and products')} count={stores.data?.total} query={stores}
        expanded={expanded === 'branches'} onExpand={() => setExpanded(value => value === 'branches' ? null : 'branches')} onRetry={() => retrySection('branches')}
        filter={<span>{text('统计范围', 'Scope')}　<strong>{selection.product ? text('选中商品', 'Selected product') : selection.supplier ? selectedNames.current[`supplier:${selection.supplier}`] || selection.supplier : text('当前标签全部供应商', 'All suppliers in this tab')}</strong></span>}
        footer={<><span>{storeRows.length} {text('家分店', 'stores')}</span><span>{text('再次点击取消选择', 'Click again to deselect')}</span></>}>{table('branches', storeRows)}</Panel>
      {resizer(1)}
      <Panel id="products" title={text('商品明细', 'Product detail')} subtitle={text('全量关键字过滤 · 点击商品反查', 'Search all products · Select to reverse-filter')} count={products.data?.total} query={products}
        expanded={expanded === 'products'} onExpand={() => setExpanded(value => value === 'products' ? null : 'products')} onRetry={() => retrySection('products')}
        filter={<><Input prefix={<SearchOutlined />} value={keywordDraft} placeholder={text('名称 / 货号 / 条码 / 供应商', 'Name / item / barcode / supplier')} aria-label={text('商品关键字', 'Product keyword')}
          onChange={event => { searchClear.current = false; setKeywordDraft(event.target.value) }} onCompositionStart={() => setComposing(true)} onCompositionEnd={() => setComposing(false)} />
          {keywordDraft && <Button type="text" size="small" icon={<CloseOutlined />} aria-label={text('清除商品关键字', 'Clear product keyword')} onClick={() => { searchClear.current = true; setKeywordDraft(''); setSelection(value => ({ ...value, keyword: '', page: 1 })) }} />}</>}
        footer={<div className={styles.productFooter}>{pageTotal && !products.loading && <div className={styles.pageSummary}><span>{text('本页商品汇总', 'Page totals')}<small>{productRows.length} {text('件 · 本期 / 同期', 'items · current / previous')}</small></span>
          <div><small>{labels.revenue}</small><MetricPair current={pageTotal.revenue} previous={pageTotal.compareRevenue} compare={dates.compare} /></div>
          <div><small>{labels.grossProfit}</small><MetricPair current={pageTotal.grossProfit} previous={pageTotal.compareGrossProfit} compare={dates.compare} costMetric revenue={pageTotal.revenue} compareRevenue={pageTotal.compareRevenue} /></div>
          <div><small>{labels.grossMarginRate}</small><MetricPair current={pageTotal.grossMarginRate} previous={pageTotal.compareGrossMarginRate} compare={dates.compare} format="rate" costMetric revenue={pageTotal.revenue} compareRevenue={pageTotal.compareRevenue} /></div></div>}
          <Pagination size="small" simple showSizeChanger pageSizeOptions={[10,20,50,100]} current={selection.page} pageSize={selection.pageSize} total={products.data?.total ?? 0} disabled={products.loading}
            onChange={(page, pageSize) => setSelection(value => ({ ...value, page: value.pageSize === pageSize ? page : 1, pageSize }))} />
        </div>}>{table('products', productRows)}</Panel>
    </div>
    <div className={styles.foot}><span>{text('移动端报告同源统计 · 客单数 — 表示当前范围无法可靠去重', 'Mobile report statistics · — transactions means no reliable distinct-basket total')}</span>
      <span>{summary.snapshot?.statisticUpdatedAt ? `${text('统计水位', 'Snapshot')} ${new Date(summary.snapshot.statisticUpdatedAt).toLocaleString()}` : text('按完整统计快照读取', 'Reading complete snapshots')}</span></div>
  </main>
}
