import { DownloadOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, DatePicker, Dropdown, Empty, Input, Select, Skeleton, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { MeasuredTable } from '../../../components/MeasuredTable'
import { batchProductSalesApi } from '../../../services/batchProductSalesAnalysisService'
import type { BatchProductSalesApi, BatchSalesDaily, BatchSalesDetail, BatchSalesMetrics, BatchSalesProductSummary, BatchSalesQueryResult, BatchSalesScope } from '../../../types/batchProductSalesAnalysis'
import ProductImage from '../ProductFlowShared/ProductImage'
import { parsePastedItemNumbers, type ImportResult } from './import'
import {
  formatCsvRow,
  getBatchProductSalesClassifiedQuantity,
  getBatchProductSalesDateRangeError,
  hasBatchProductSalesDiscountStatisticsNotice,
  hasBatchProductSalesDailyActivity,
  shouldRefreshBatchProductSalesDiscountStatistics,
  sortBatchProductSalesBranchesByQuantity,
} from './logic'
import DiscountDailyChart from './DiscountDailyChart'
import ProductScopeModal from './ProductScopeModal'
import styles from './index.module.css'

const { RangePicker } = DatePicker
const quantityFormatter = new Intl.NumberFormat('en-AU')
const audFormatter = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' })

interface BatchProductSalesAnalysisPageProps { api?: BatchProductSalesApi }

function businessToday() {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Brisbane', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date())
  const value = (type: string) => parts.find((part) => part.type === type)?.value ?? ''
  return `${value('year')}-${value('month')}-${value('day')}`
}

function defaultRange(today: string): [Dayjs, Dayjs] {
  const end = dayjs(today)
  return [end.subtract(29, 'day'), end]
}

function number(value: number | null | undefined) { return quantityFormatter.format(value ?? 0) }
function money(value: number | null | undefined) { return value === null || value === undefined || !Number.isFinite(value) ? '—' : audFormatter.format(value) }
function errorKind(error: unknown) {
  const text = error instanceof Error ? error.message : String(error ?? '')
  if (/403|forbidden|权限/i.test(text)) return 'forbidden'
  if (/abort|timeout|超时/i.test(text)) return 'timeout'
  return 'load'
}
function isAbort(error: unknown) { return error instanceof Error && error.name === 'AbortError' }
function isPartial(warnings: string[]) { return warnings.some((warning) => /partial|incomplete|missing|不完整|缺失/i.test(warning)) }

/** 仅在服务已成功返回且没有声明局部/缺失数据时补齐业务日期，避免把未知数据伪装为 0。 */
function fillKnownDays(data: BatchSalesDaily[], scope: BatchSalesScope, canFill: boolean): BatchSalesDaily[] {
  if (!canFill) return data
  const byDate = new Map(data.map((item) => [item.date, item]))
  const dates: BatchSalesDaily[] = []
  for (let date = dayjs(scope.startDate); !date.isAfter(scope.endDate, 'day'); date = date.add(1, 'day')) {
    const key = date.format('YYYY-MM-DD')
    dates.push(byDate.get(key) ?? { date: key, metrics: { quantity: 0, regularQuantity: 0, discountQuantity: 0, unknownQuantity: 0, returnQuantity: 0, salesAmount: 0, discountStatus: 'complete', originalPriceMin: null, originalPriceMax: null, discountPriceMin: null, discountPriceMax: null } })
  }
  return dates
}

function classified(metrics: BatchSalesMetrics, field: 'regularQuantity' | 'discountQuantity' | 'unknownQuantity', classificationUnavailable = false) {
  const value = getBatchProductSalesClassifiedQuantity(metrics, field, classificationUnavailable)
  return value === null ? '—' : number(value)
}

function discountRate(metrics: BatchSalesMetrics) {
  if (metrics.quantity <= 0 || metrics.discountStatus !== 'complete' || metrics.unknownQuantity !== 0 || metrics.regularQuantity < 0 || metrics.discountQuantity < 0) return '—'
  return `${((metrics.discountQuantity / metrics.quantity) * 100).toFixed(1)}%`
}
function branchProductShare(branch: BatchSalesMetrics, product: BatchSalesMetrics) {
  if (product.quantity === 0) return '—'
  return `${((branch.quantity / product.quantity) * 100).toFixed(1)}%`
}
function metricStatus(metrics: BatchSalesMetrics, t: (key: string) => string) {
  if (metrics.discountStatus === 'pending') return t('batchProductSalesAnalysis.discountPending')
  if (metrics.discountStatus === 'complete' && metrics.quantity === 0 && metrics.regularQuantity === 0 && metrics.discountQuantity === 0 && metrics.unknownQuantity === 0 && metrics.returnQuantity === 0) return t('batchProductSalesAnalysis.noSalesStatus')
  if (metrics.unknownQuantity !== 0 || metrics.discountStatus !== 'complete') return t('batchProductSalesAnalysis.unknownStatus')
  if (metrics.regularQuantity !== 0 && metrics.discountQuantity !== 0) return t('batchProductSalesAnalysis.mixedStatus')
  return metrics.discountQuantity !== 0 ? t('batchProductSalesAnalysis.discountStatus') : t('batchProductSalesAnalysis.regularStatus')
}
function range(value1: number | null, value2: number | null) { return value1 === null || value2 === null ? '—' : value1 === value2 ? money(value1) : `${money(value1)} – ${money(value2)}` }

function downloadCsv(fileName: string, rows: unknown[][]) {
  const content = `\uFEFF${rows.map(formatCsvRow).join('\r\n')}\r\n`
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }))
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

function LoadState({ loading, error, empty, onRetry, children }: { loading: boolean; error?: string; empty?: boolean; onRetry?: () => void; children: React.ReactNode }) {
  const { t } = useTranslation()
  if (loading) return <div className={styles.state}><Skeleton active title={false} paragraph={{ rows: 4 }} /></div>
  if (error) return <Alert type="error" showIcon message={t('batchProductSalesAnalysis.errors.title')} description={t(`batchProductSalesAnalysis.errors.${error}`)} action={onRetry ? <Button size="small" onClick={onRetry}>{t('batchProductSalesAnalysis.retry')}</Button> : undefined} />
  if (empty) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noDailyData')} />
  return <>{children}</>
}

export default function BatchProductSalesAnalysisPage({ api = batchProductSalesApi }: BatchProductSalesAnalysisPageProps) {
  const { t } = useTranslation()
  const today = useMemo(businessToday, [])
  const [draftRange, setDraftRange] = useState<[Dayjs, Dayjs]>(() => defaultRange(today))
  const [draftStores, setDraftStores] = useState<string[]>([])
  const [pastedText, setPastedText] = useState('')
  const [importResult, setImportResult] = useState<ImportResult>(() => parsePastedItemNumbers(''))
  const [options, setOptions] = useState<{ stores: { code: string; name: string }[]; maxItemNumbers: number; maxDays: number }>()
  const [optionsLoading, setOptionsLoading] = useState(true)
  const [optionsError, setOptionsError] = useState<string>()
  const [queryResult, setQueryResult] = useState<BatchSalesQueryResult>()
  const [appliedScope, setAppliedScope] = useState<BatchSalesScope>()
  const [submittedInput, setSubmittedInput] = useState<{ scope: BatchSalesScope; itemNumbers: string[] }>()
  const [queryLoading, setQueryLoading] = useState(false)
  const [queryError, setQueryError] = useState<string>()
  const [selectedProductCode, setSelectedProductCode] = useState<string>()
  const [detail, setDetail] = useState<BatchSalesDetail>()
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailError, setDetailError] = useState<string>()
  const [selectedBranchCode, setSelectedBranchCode] = useState<string>()
  const [scopeOpen, setScopeOpen] = useState(false)
  const [productSearch, setProductSearch] = useState('')
  const queryAbortRef = useRef<AbortController>()
  const detailAbortRef = useRef<AbortController>()
  const queryRequestRef = useRef(0)
  const detailRequestRef = useRef(0)
  const optionsRequestRef = useRef(0)

  const loadOptions = useCallback(() => {
    const controller = new AbortController()
    const request = ++optionsRequestRef.current
    setOptionsLoading(true); setOptionsError(undefined)
    api.getOptions(controller.signal).then((result) => { if (request === optionsRequestRef.current) setOptions(result) }).catch((error) => { if (!isAbort(error) && request === optionsRequestRef.current) setOptionsError(errorKind(error)) }).finally(() => { if (request === optionsRequestRef.current) setOptionsLoading(false) })
    return () => { controller.abort(); if (request === optionsRequestRef.current) optionsRequestRef.current += 1 }
  }, [api])
  useEffect(() => loadOptions(), [loadOptions])
  useEffect(() => () => { queryAbortRef.current?.abort(); detailAbortRef.current?.abort() }, [])

  const dirty = !!submittedInput && (submittedInput.scope.startDate !== draftRange[0].format('YYYY-MM-DD') || submittedInput.scope.endDate !== draftRange[1].format('YYYY-MM-DD') || submittedInput.scope.storeCodes.join('|') !== draftStores.join('|') || submittedInput.itemNumbers.join('\u0001') !== importResult.itemNumbers.join('\u0001'))
  const selectedProduct = queryResult?.products.find((product) => product.productCode === selectedProductCode)
  const selectedBranch = detail?.branches.find((branch) => branch.branchCode === selectedBranchCode)
  const appliedStoreText = useMemo(() => {
    if (!appliedScope?.storeCodes.length) return t('batchProductSalesAnalysis.allStores')
    const storesByCode = new Map((options?.stores ?? []).map((store) => [store.code, store]))
    if (appliedScope.storeCodes.length > 3) return t('batchProductSalesAnalysis.storeCount', { count: appliedScope.storeCodes.length })
    return appliedScope.storeCodes.map((code) => {
      const store = storesByCode.get(code)
      return store ? `${store.name} (${code})` : code
    }).join(' · ')
  }, [appliedScope, options?.stores, t])

  const loadDetail = useCallback((productCode: string, scope: BatchSalesScope, background = false) => {
    detailAbortRef.current?.abort()
    const controller = new AbortController(); detailAbortRef.current = controller
    const request = ++detailRequestRef.current
    setDetailError(undefined)
    if (!background) { setDetailLoading(true); setDetail(undefined); setSelectedBranchCode(undefined) }
    api.getDetail({ ...scope, productCode }, controller.signal).then((result) => {
      if (request !== detailRequestRef.current) return
      if (result.statisticStatus && result.statisticStatus.toLowerCase() !== 'fresh') {
        setDetail(undefined); setDetailError('statistics'); return
      }
      const branches = sortBatchProductSalesBranchesByQuantity(result.branches)
      setDetail({ ...result, branches })
      setSelectedBranchCode((current) => background && branches.some((branch) => branch.branchCode === current) ? current : branches[0]?.branchCode)
    }).catch((error) => { if (!isAbort(error) && request === detailRequestRef.current) setDetailError(errorKind(error)) }).finally(() => { if (request === detailRequestRef.current) setDetailLoading(false) })
  }, [api])

  // 后台刷新保留已显示的数量和分店；切换商品时 effect 清理定时器且请求序号阻止旧结果覆盖。
  useEffect(() => {
    if (!detail || detailError || !appliedScope || !selectedProductCode || !shouldRefreshBatchProductSalesDiscountStatistics(detail.discountStatisticStatus)) return
    const timer = window.setTimeout(() => loadDetail(selectedProductCode, appliedScope, true), 5000)
    return () => window.clearTimeout(timer)
  }, [detail, detailError, appliedScope, selectedProductCode, loadDetail])

  const query = useCallback(() => {
    const startDate = draftRange[0].format('YYYY-MM-DD'); const endDate = draftRange[1].format('YYYY-MM-DD')
    if (!importResult.itemNumbers.length) { setQueryError('load'); return }
    if (getBatchProductSalesDateRangeError(startDate, endDate, today)) { setQueryError('date'); return }
    queryAbortRef.current?.abort(); detailAbortRef.current?.abort()
    const controller = new AbortController(); queryAbortRef.current = controller
    const request = ++queryRequestRef.current
    ++detailRequestRef.current
    const scope = { startDate, endDate, storeCodes: [...draftStores] }
    setProductSearch(''); setQueryLoading(true); setQueryError(undefined); setQueryResult(undefined); setAppliedScope(undefined); setDetail(undefined); setDetailError(undefined); setSelectedProductCode(undefined); setSelectedBranchCode(undefined)
    api.query({ ...scope, itemNumbers: importResult.itemNumbers }, controller.signal).then((result) => {
      if (request !== queryRequestRef.current) return
      setQueryResult(result); setAppliedScope({ startDate: result.startDate, endDate: result.endDate, storeCodes: result.storeCodes }); setSubmittedInput({ scope, itemNumbers: [...importResult.itemNumbers] })
      const first = result.products[0]
      if (first) { setSelectedProductCode(first.productCode); loadDetail(first.productCode, { startDate: result.startDate, endDate: result.endDate, storeCodes: result.storeCodes }) }
    }).catch((error) => { if (!isAbort(error) && request === queryRequestRef.current) setQueryError(errorKind(error)) }).finally(() => { if (request === queryRequestRef.current) setQueryLoading(false) })
  }, [api, draftRange, draftStores, importResult.itemNumbers, loadDetail, today])

  const chooseProduct = (product: BatchSalesProductSummary) => {
    if (!appliedScope || product.productCode === selectedProductCode) return
    setSelectedProductCode(product.productCode)
    loadDetail(product.productCode, appliedScope)
  }
  const currentDaily = detail && appliedScope ? fillKnownDays(detail.daily, appliedScope, !isPartial(detail.warnings)) : []
  const dailyDetailRows = currentDaily.filter(hasBatchProductSalesDailyActivity)
  const branchDaily = selectedBranch && appliedScope && detail ? fillKnownDays(selectedBranch.daily, appliedScope, !isPartial(detail.warnings)) : []
  const classificationUnavailable = !!detail && detail.metrics.discountStatus !== 'pending'
    && !!detail.discountStatisticStatus && detail.discountStatisticStatus.toLowerCase() !== 'fresh'
  const exportClassifiedQuantity = (metrics: BatchSalesMetrics, field: 'regularQuantity' | 'discountQuantity' | 'unknownQuantity') => (
    getBatchProductSalesClassifiedQuantity(metrics, field, classificationUnavailable) ?? ''
  )

  const dailyColumns: ColumnsType<BatchSalesDaily> = [
    { title: t('batchProductSalesAnalysis.columns.date'), dataIndex: 'date', width: 86 },
    { title: t('batchProductSalesAnalysis.columns.quantity'), align: 'right', width: 78, render: (_, row) => number(row.metrics.quantity) },
    { title: t('batchProductSalesAnalysis.columns.regular'), align: 'right', width: 68, render: (_, row) => classified(row.metrics, 'regularQuantity', classificationUnavailable) },
    { title: t('batchProductSalesAnalysis.columns.discount'), align: 'right', width: 68, render: (_, row) => classified(row.metrics, 'discountQuantity', classificationUnavailable) },
    { title: t('batchProductSalesAnalysis.columns.unknown'), align: 'right', width: 68, render: (_, row) => classified(row.metrics, 'unknownQuantity', classificationUnavailable) },
    { title: t('batchProductSalesAnalysis.columns.amount'), align: 'right', width: 106, render: (_, row) => money(row.metrics.salesAmount) },
    { title: t('batchProductSalesAnalysis.columns.status'), width: 72, render: (_, row) => metricStatus(row.metrics, t) },
  ]

  const statisticsPending = !!queryResult?.statisticStatus && queryResult.statisticStatus.toLowerCase() !== 'fresh'
  const exportSummary = () => queryResult && downloadCsv('batch-product-sales-summary.csv', [
    [t('batchProductSalesAnalysis.export.scope'), appliedScope?.startDate ?? '', appliedScope?.endDate ?? '', appliedScope?.storeCodes.join(' | ') || t('batchProductSalesAnalysis.allStores')],
    [t('batchProductSalesAnalysis.columns.itemNumber'), t('batchProductSalesAnalysis.columns.product'), t('batchProductSalesAnalysis.columns.quantity')],
    ...queryResult.products.map((product) => [product.itemNumber, product.productName || product.englishName || product.productCode, product.quantity]),
  ])
  const exportDetail = () => detail && detail.metrics.discountStatus !== 'pending' && downloadCsv('batch-product-sales-detail.csv', [
    [t('batchProductSalesAnalysis.export.product'), detail.product.productCode, detail.product.itemNumber, detail.product.productName || detail.product.englishName || ''],
    [t('batchProductSalesAnalysis.export.scope'), appliedScope?.startDate ?? '', appliedScope?.endDate ?? '', appliedScope?.storeCodes.join(' | ') || t('batchProductSalesAnalysis.allStores')],
    [t('batchProductSalesAnalysis.export.daily')],
    [t('batchProductSalesAnalysis.columns.date'), t('batchProductSalesAnalysis.columns.quantity'), t('batchProductSalesAnalysis.columns.regular'), t('batchProductSalesAnalysis.columns.discount'), t('batchProductSalesAnalysis.columns.unknown'), t('batchProductSalesAnalysis.columns.amount')],
    ...dailyDetailRows.map((day) => [day.date, day.metrics.quantity, exportClassifiedQuantity(day.metrics, 'regularQuantity'), exportClassifiedQuantity(day.metrics, 'discountQuantity'), exportClassifiedQuantity(day.metrics, 'unknownQuantity'), day.metrics.salesAmount]),
    [], [t('batchProductSalesAnalysis.export.branches')],
    [t('batchProductSalesAnalysis.columns.branch'), t('batchProductSalesAnalysis.columns.quantity'), t('batchProductSalesAnalysis.columns.regular'), t('batchProductSalesAnalysis.columns.discount'), t('batchProductSalesAnalysis.columns.unknown'), t('batchProductSalesAnalysis.columns.amount')],
    ...detail.branches.map((branch) => [branch.branchName || branch.branchCode, branch.metrics.quantity, exportClassifiedQuantity(branch.metrics, 'regularQuantity'), exportClassifiedQuantity(branch.metrics, 'discountQuantity'), exportClassifiedQuantity(branch.metrics, 'unknownQuantity'), branch.metrics.salesAmount]),
    [], [t('batchProductSalesAnalysis.export.branchDaily')],
    [t('batchProductSalesAnalysis.columns.branch'), t('batchProductSalesAnalysis.columns.date'), t('batchProductSalesAnalysis.columns.quantity'), t('batchProductSalesAnalysis.columns.regular'), t('batchProductSalesAnalysis.columns.discount'), t('batchProductSalesAnalysis.columns.unknown'), t('batchProductSalesAnalysis.columns.amount')],
    ...detail.branches.flatMap((branch) => fillKnownDays(branch.daily, appliedScope ?? detail, !isPartial(detail.warnings)).filter(hasBatchProductSalesDailyActivity).map((day) => [branch.branchName || branch.branchCode, day.date, day.metrics.quantity, exportClassifiedQuantity(day.metrics, 'regularQuantity'), exportClassifiedQuantity(day.metrics, 'discountQuantity'), exportClassifiedQuantity(day.metrics, 'unknownQuantity'), day.metrics.salesAmount])),
  ])

  return <div className={styles.screen}><PageContainer title={t('batchProductSalesAnalysis.title')} subtitle={t('batchProductSalesAnalysis.subtitle')}>
    <div className={styles.page}>
      <section className={styles.toolbar} aria-label={t('batchProductSalesAnalysis.query')}>
        <Button onClick={() => setScopeOpen(true)}>{t('batchProductSalesAnalysis.selectItems', { count: importResult.itemNumbers.length })}</Button>
        <label className={styles.field}><span>{t('batchProductSalesAnalysis.dateRange')}</span><RangePicker value={draftRange} allowClear={false} disabledDate={(date) => date.isAfter(dayjs(today), 'day')} onChange={(value) => value?.[0] && value?.[1] && setDraftRange([value[0], value[1]])} /></label>
        <label className={styles.field}><span>{t('batchProductSalesAnalysis.stores')}</span><Select mode="multiple" value={draftStores} loading={optionsLoading} className={styles.storeSelect} maxTagCount="responsive" placeholder={t('batchProductSalesAnalysis.allStores')} options={options?.stores.map((store) => ({ value: store.code, label: `${store.name} (${store.code})` }))} onChange={setDraftStores} /></label>
        <Button type="primary" icon={<SearchOutlined />} loading={queryLoading} disabled={!importResult.itemNumbers.length} onClick={query}>{queryLoading ? t('batchProductSalesAnalysis.querying') : t('batchProductSalesAnalysis.query')}</Button>
        <Dropdown trigger={['click']} menu={{ items: [{ key: 'summary', label: t('batchProductSalesAnalysis.downloadSummary'), disabled: !queryResult || statisticsPending, onClick: exportSummary }, { key: 'detail', label: t('batchProductSalesAnalysis.downloadDetail'), disabled: !detail || detail.metrics.discountStatus === 'pending', onClick: exportDetail }] }}><Button icon={<DownloadOutlined />}>{t('batchProductSalesAnalysis.exportResults')}</Button></Dropdown>
      </section>
      {optionsError ? <Alert type="warning" showIcon message={t('batchProductSalesAnalysis.errors.load')} action={<Button size="small" icon={<ReloadOutlined />} onClick={loadOptions}>{t('batchProductSalesAnalysis.retry')}</Button>} /> : null}
      {dirty ? <Alert type="info" showIcon message={t('batchProductSalesAnalysis.pendingQuery')} /> : null}
      {appliedScope ? <div className={styles.appliedScope}>{t('batchProductSalesAnalysis.appliedScope', { startDate: appliedScope.startDate, endDate: appliedScope.endDate, stores: appliedStoreText })}</div> : null}
      {!statisticsPending && queryResult?.warnings.length ? <details className={styles.hint}><summary>{t('batchProductSalesAnalysis.dataNotes')}</summary>{queryResult.warnings.map((warning) => <p key={warning}>{warning}</p>)}</details> : null}
      {hasBatchProductSalesDiscountStatisticsNotice(detail?.discountStatisticStatus) ? <Alert type={detail?.metrics.discountStatus === 'pending' ? 'info' : 'warning'} showIcon message={t(`batchProductSalesAnalysis.discountStates.${detail?.discountStatisticStatus ?? 'Unavailable'}`)} action={<Button size="small" onClick={() => selectedProductCode && appliedScope && loadDetail(selectedProductCode, appliedScope, true)}>{t('batchProductSalesAnalysis.refreshStatus')}</Button>} /> : null}
      <main className={styles.layout}>
        <aside className={`${styles.column} ${styles.leftColumn}`}>
          <section className={`${styles.panel} ${styles.productPanel}`}><header className={styles.panelHeader}><h2>{t('batchProductSalesAnalysis.productList', { count: queryResult?.products.length ?? 0 })}</h2>{queryResult && !statisticsPending ? <span className={styles.panelMeta}>{t('batchProductSalesAnalysis.totalQuantity', { value: number(queryResult.products.reduce((sum, product) => sum + product.quantity, 0)) })}</span> : null}</header>
            <Input allowClear prefix={<SearchOutlined />} aria-label={t('batchProductSalesAnalysis.searchProducts')} placeholder={t('batchProductSalesAnalysis.searchProducts')} value={productSearch} onChange={event => setProductSearch(event.target.value)} />
            {statisticsPending && !queryLoading ? <Alert type="info" showIcon message={t('batchProductSalesAnalysis.statisticsPending')} description={queryResult?.warnings.map((warning) => <div key={warning}>{warning}</div>)} action={<Button size="small" onClick={query}>{t('batchProductSalesAnalysis.retry')}</Button>} /> : <LoadState loading={queryLoading} error={queryError} empty={!!queryResult && !queryResult.products.length} onRetry={query}>{queryResult ? <div className={styles.productList} role="region" aria-label={t('batchProductSalesAnalysis.productList', { count: queryResult.products.length })} tabIndex={0}>{queryResult.products.filter(product => `${product.itemNumber} ${product.productName} ${product.englishName ?? ''}`.toLocaleLowerCase().includes(productSearch.trim().toLocaleLowerCase())).map((product) => <button key={product.productCode} className={`${styles.productRow} ${selectedProductCode === product.productCode ? styles.productCurrent : ''}`} aria-pressed={selectedProductCode === product.productCode} onClick={() => chooseProduct(product)}><ProductImage src={product.imageUrl} alt={product.productName || product.itemNumber} /><span className={styles.productInfo}><strong>{product.itemNumber}</strong><span>{product.productName || product.englishName || product.productCode}</span></span><b>{number(product.quantity)}</b></button>)}</div> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noQueryResult')} />}</LoadState>}
            {queryResult?.matches.length ? <details className={styles.details}><summary>{t('batchProductSalesAnalysis.matched')} {queryResult.matches.filter((match) => match.status === 'matched').length} · {t('batchProductSalesAnalysis.notFound')} {queryResult.matches.filter((match) => match.status === 'notFound').length} · {t('batchProductSalesAnalysis.ambiguous')} {queryResult.matches.filter((match) => match.status === 'ambiguous').length}</summary>{queryResult.matches.map((match) => <div key={match.itemNumber}><span>{match.itemNumber}</span><Tag color={match.status === 'matched' ? 'success' : match.status === 'ambiguous' ? 'warning' : 'error'}>{t(`batchProductSalesAnalysis.${match.status === 'notFound' ? 'notFound' : match.status}`)}</Tag></div>)}</details> : null}
          </section>
        </aside>
        <section className={`${styles.panel} ${styles.totalTrendPanel}`}>
            <header className={styles.panelHeader}><h2>{t('batchProductSalesAnalysis.dailyTrend')}</h2>{selectedProduct ? <span className={styles.panelMeta}>{selectedProduct.itemNumber} · {selectedProduct.productName || selectedProduct.englishName || selectedProduct.productCode}</span> : null}</header>
            <LoadState loading={detailLoading} error={detailError} empty={!detailLoading && !!detail && !currentDaily.length} onRetry={() => selectedProductCode && appliedScope && loadDetail(selectedProductCode, appliedScope)}>{detail ? <><DiscountDailyChart data={currentDaily} ariaLabel={t('batchProductSalesAnalysis.dailyTrend')} classificationUnavailable={classificationUnavailable} /><div className={styles.branchTotals}><span>{t('batchProductSalesAnalysis.metrics.quantity')} <b>{number(detail.metrics.quantity)}</b></span><span className={styles.amount}>{t('batchProductSalesAnalysis.metrics.amount')} <b>{money(detail.metrics.salesAmount)}</b></span><span className={styles.regular}>{t('batchProductSalesAnalysis.metrics.regular')} <b>{classified(detail.metrics, 'regularQuantity', classificationUnavailable)}</b></span><span className={styles.discount}>{t('batchProductSalesAnalysis.metrics.discount')} <b>{classified(detail.metrics, 'discountQuantity', classificationUnavailable)}</b></span><span>{t('batchProductSalesAnalysis.discountRate')} <b>{discountRate(detail.metrics)}</b></span><Tag color={detail.metrics.discountStatus === 'complete' ? (detail.metrics.regularQuantity !== 0 && detail.metrics.discountQuantity !== 0 ? 'orange' : detail.metrics.discountQuantity !== 0 ? 'gold' : 'blue') : 'default'}>{metricStatus(detail.metrics, t)}</Tag></div>{detail.metrics.returnQuantity ? <p className={styles.returnHint}>{t('batchProductSalesAnalysis.returnHint', { value: number(detail.metrics.returnQuantity) })}</p> : null}<p className={styles.priceHint}>{t('batchProductSalesAnalysis.priceRange', { original: range(detail.metrics.originalPriceMin, detail.metrics.originalPriceMax), discount: range(detail.metrics.discountPriceMin, detail.metrics.discountPriceMax) })}</p></> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noQueryResult')} />}</LoadState>
        </section>
        <section className={`${styles.panel} ${styles.branchTrendPanel}`}><header className={styles.panelHeader}><h2>{t('batchProductSalesAnalysis.branchDailyTrend')}</h2>{selectedBranch ? <span className={styles.panelMeta}>{selectedBranch.branchName || selectedBranch.branchCode}</span> : null}</header>{selectedBranch ? <><DiscountDailyChart data={branchDaily} ariaLabel={t('batchProductSalesAnalysis.branchTrend', { branch: selectedBranch.branchName || selectedBranch.branchCode })} classificationUnavailable={classificationUnavailable} /><div className={styles.branchTotals}><span>{t('batchProductSalesAnalysis.metrics.quantity')} <b>{number(selectedBranch.metrics.quantity)}</b></span><span className={styles.amount}>{t('batchProductSalesAnalysis.metrics.amount')} <b>{money(selectedBranch.metrics.salesAmount)}</b></span><span className={styles.regular}>{t('batchProductSalesAnalysis.metrics.regular')} <b>{classified(selectedBranch.metrics, 'regularQuantity', classificationUnavailable)}</b></span><span className={styles.discount}>{t('batchProductSalesAnalysis.metrics.discount')} <b>{classified(selectedBranch.metrics, 'discountQuantity', classificationUnavailable)}</b></span></div></> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.selectBranch')} />}</section>
        <section className={`${styles.panel} ${styles.dailyDetailPanel}`}><header className={styles.panelHeader}><h2>{t('batchProductSalesAnalysis.dailyDetail')}</h2></header>{detail?.warnings.map((warning) => <Alert key={warning} className={styles.warning} type="warning" showIcon message={warning} />)}<div className={styles.tableWrap}>{detail ? dailyDetailRows.length ? <MeasuredTable metricId="executive-sales-intelligence.batch-product-sales-analysis.daily" size="small" rowKey="date" columns={dailyColumns} dataSource={dailyDetailRows} pagination={false} scroll={{ x: 550 }} /> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.noDailyData')} /> : null}</div></section>
        <section className={`${styles.panel} ${styles.branchSalesPanel}`}><header className={styles.panelHeader}><h2>{t('batchProductSalesAnalysis.branchSales')}</h2><span className={styles.panelMeta}>{detail?.branches.length ? `${detail.branches.length}` : ''}</span></header><LoadState loading={detailLoading} error={detailError} empty={!detailLoading && !!detail && !detail.branches.length} onRetry={() => selectedProductCode && appliedScope && loadDetail(selectedProductCode, appliedScope)}>{detail ? <div className={styles.branchGrid}><div className={styles.branchGridHead}><span>{t('batchProductSalesAnalysis.columns.branch')}</span><span>{t('batchProductSalesAnalysis.columns.quantity')}</span><span>{t('batchProductSalesAnalysis.columns.regular')}</span><span>{t('batchProductSalesAnalysis.columns.discount')}</span><span>{t('batchProductSalesAnalysis.columns.amount')}</span><span>{t('batchProductSalesAnalysis.columns.productShare')}</span></div>{detail.branches.map((branch) => <div className={`${styles.branchGridRow} ${selectedBranchCode === branch.branchCode ? styles.branchCurrent : ''}`} key={branch.branchCode}><button className={styles.branchButton} onClick={() => setSelectedBranchCode(branch.branchCode)}>{branch.branchName || branch.branchCode}</button><span>{number(branch.metrics.quantity)}</span><span className={styles.regular}>{classified(branch.metrics, 'regularQuantity', classificationUnavailable)}</span><span className={styles.discount}>{classified(branch.metrics, 'discountQuantity', classificationUnavailable)}</span><span className={styles.amount}>{money(branch.metrics.salesAmount)}</span><span>{branchProductShare(branch.metrics, detail.metrics)}</span></div>)}<div className={styles.branchGridTotal}><strong>{t('batchProductSalesAnalysis.total')}</strong><strong>{number(detail.metrics.quantity)}</strong><strong className={styles.regular}>{classified(detail.metrics, 'regularQuantity', classificationUnavailable)}</strong><strong className={styles.discount}>{classified(detail.metrics, 'discountQuantity', classificationUnavailable)}</strong><strong className={styles.amount}>{money(detail.metrics.salesAmount)}</strong><strong>{branchProductShare(detail.metrics, detail.metrics)}</strong></div></div> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('batchProductSalesAnalysis.selectBranch')} />}</LoadState></section>
      </main>
      {scopeOpen ? <ProductScopeModal initialText={pastedText} initialResult={importResult} maxItems={options?.maxItemNumbers ?? 500} onCancel={() => setScopeOpen(false)} onApply={(text, result) => { setPastedText(text); setImportResult(result); setScopeOpen(false) }} /> : null}
    </div>
  </PageContainer></div>
}
