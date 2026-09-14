import { RightOutlined } from '@ant-design/icons'
import { Alert, Button, Empty, Skeleton } from 'antd'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../store/auth'
import { reportPeriod, type DateSelection } from './ReportWorkbench/logic'
import { ReportControls, useReportText } from './ReportWorkbench/ReportControls'
import { useReportQuery } from './ReportWorkbench/useReportQuery'
import RevenueWeeklyHierarchy from './RevenueReport/RevenueWeeklyHierarchy'
import {
  aggregateHourlyRows,
  buildRevenueOverviewSearch,
  buildSalesDetailPath,
  formatAud,
  formatInteger,
  getRevenueSummary,
  getRevenueTrend,
  makeRevenueQueryScope,
  parseRevenueOverviewSearch,
  sortRevenueBranchesByRevenue,
} from './RevenueReport/logic'
import {
  fetchRevenueReportSnapshot,
  type RevenueReportSnapshot,
} from './RevenueReport/revenueReportService'
import styles from './styles.module.css'

const INITIAL_BRANCH_COUNT = 8

function normalizeCode(value: string | null | undefined) {
  return value?.trim().toLocaleLowerCase('en-AU') ?? ''
}

export default function ExecutiveSalesIntelligence() {
  const text = useReportText()
  const navigate = useNavigate()
  const location = useLocation()
  const { active } = useKeepAliveContext()
  const access = useAuthStore(state => state.access)
  const currentUser = useAuthStore(state => state.currentUser)
  const initialQuery = useRef(parseRevenueOverviewSearch(location.search)).current
  const [selection, setSelection] = useState<DateSelection>(initialQuery.selection)
  const [selectedBranch, setSelectedBranch] = useState<{ branchCode: string; branchName: string } | null>(
    initialQuery.branchCode ? { branchCode: initialQuery.branchCode, branchName: initialQuery.branchCode } : null,
  )
  const [showAllBranches, setShowAllBranches] = useState(false)
  const [refresh, setRefresh] = useState(0)

  const managedStoreCodes = useMemo(() => {
    // 销售报表按全部关联分店读取，普通关联分店也可查看销售数据。
    const codes = access.visibleStoreCodes()
    return codes == null
      ? null
      : [...new Set(codes.map(code => code.trim()).filter(Boolean))].sort((left, right) => left.localeCompare(right))
  }, [access])
  const period = useMemo(() => reportPeriod(selection), [selection])
  const periodKey = `${period.startDate}:${period.endDate}:${period.compareStartDate ?? '-'}:${period.compareEndDate ?? '-'}:${period.compareMode}`
  const queryScope = makeRevenueQueryScope(currentUser?.userGUID, managedStoreCodes)
  const hourlyBranchCodes = selectedBranch ? [selectedBranch.branchCode] : managedStoreCodes
  const hourlyScope = makeRevenueQueryScope(currentUser?.userGUID, hourlyBranchCodes)
  const hasStoreScope = Boolean(currentUser) && (managedStoreCodes == null || managedStoreCodes.length > 0)

  const reportQuery = useReportQuery<RevenueReportSnapshot>(
    `revenue:whole-page:${queryScope}:${hourlyScope}:${periodKey}`,
    signal => fetchRevenueReportSnapshot(period, managedStoreCodes, hourlyBranchCodes, signal),
    { active, enabled: hasStoreScope, refresh, metricId: 'revenue-whole-page' },
  )
  const branchQuery = { ...reportQuery, data: reportQuery.data?.branches }
  const hourlyQuery = { ...reportQuery, data: reportQuery.data?.hourly }
  const weeklyQuery = { ...reportQuery, data: reportQuery.data?.weekly }

  useEffect(() => {
    if (!active) return
    const expected = buildRevenueOverviewSearch(selection, selectedBranch?.branchCode ?? null)
    if (location.search === expected) return
    const incoming = parseRevenueOverviewSearch(location.search)
    setSelection(incoming.selection)
    setSelectedBranch(incoming.branchCode ? { branchCode: incoming.branchCode, branchName: incoming.branchCode } : null)
  }, [active, location.search])

  const branches = useMemo(
    () => sortRevenueBranchesByRevenue(branchQuery.data ?? []),
    [branchQuery.data],
  )
  // 核心 KPI、排行和周层级同源于 StoreSales；总体 comparePeriodPending 也可能只由小时缺口触发。
  const coreComparePending = selection.compare && reportQuery.data?.weeklyComparePending === true
  const hourlyCurrentPending = reportQuery.data?.hourlyCurrentPending === true
  const hourlyComparePending = selection.compare && reportQuery.data?.hourlyComparePending === true
  const weeklyComparePending = selection.compare && reportQuery.data?.weeklyComparePending === true
  const coreCompareAvailable = selection.compare && !coreComparePending
  const hourlyCompareAvailable = selection.compare && !hourlyComparePending
  const weeklyCompareAvailable = selection.compare && !weeklyComparePending
  useEffect(() => {
    if (!selectedBranch || !branchQuery.data) return
    const match = branchQuery.data.find(branch => normalizeCode(branch.branchCode) === normalizeCode(selectedBranch.branchCode))
    if (match && match.branchName !== selectedBranch.branchName) {
      setSelectedBranch({ branchCode: match.branchCode, branchName: match.branchName })
    }
  }, [branchQuery.data, selectedBranch])
  const summary = useMemo(
    () => getRevenueSummary(branches, selectedBranch?.branchCode ?? null, coreCompareAvailable),
    [branches, selectedBranch?.branchCode, coreCompareAvailable],
  )
  const hourlyRows = useMemo(
    () => aggregateHourlyRows(hourlyQuery.data ?? [], hourlyCompareAvailable),
    [hourlyQuery.data, hourlyCompareAvailable],
  )
  const visibleBranches = showAllBranches ? branches : branches.slice(0, INITIAL_BRANCH_COUNT)
  const hasBranchMetrics = branchQuery.data !== undefined && branches.length > 0 && !branchQuery.error
    && (!selectedBranch || branches.some(branch => normalizeCode(branch.branchCode) === normalizeCode(selectedBranch.branchCode)))
  const selectedDate = selection.startDate === selection.endDate ? selection.startDate : null
  const allLoading = reportQuery.loading
  const anySlow = reportQuery.slow
  const scopeLabel = selectedBranch?.branchName || text('全部门店', 'All branches')
  const localizeTrend = (trend: ReturnType<typeof getRevenueTrend>) => ({
    ...trend,
    text: trend.text === 'new' ? text('新增', 'New') : trend.text,
  })
  const revenueTrend = localizeTrend(getRevenueTrend(hasBranchMetrics ? summary.revenue : null, hasBranchMetrics ? summary.revenueLY : null))
  const orderTrend = localizeTrend(getRevenueTrend(hasBranchMetrics ? summary.orders : null, hasBranchMetrics ? summary.ordersLY : null))
  const aovTrend = localizeTrend(getRevenueTrend(hasBranchMetrics ? summary.aov : null, hasBranchMetrics ? summary.aovLY : null))
  const signalValue = !hasStoreScope
    ? text('无可访问分店', 'No accessible branches')
    : branchQuery.error
      ? text('核心数据加载失败', 'Core data unavailable')
      : !hasBranchMetrics
        ? text('暂无业绩数据', 'No performance data')
        : !selection.compare
          ? text('自动对比已暂停', 'Comparison paused')
          : coreComparePending
            ? text('同期统计未齐', 'Comparison snapshot incomplete')
          : selectedBranch
            ? revenueTrend.text
            : text(`${summary.decliningBranches} 家门店需关注`, `${summary.decliningBranches} branches need attention`)
  const signalDetail = !hasBranchMetrics
    ? '—'
    : selection.compare
      ? coreComparePending
        ? text('本期业绩已显示；同期完成后再计算同比。', 'Current performance is shown; comparison trends will follow when ready.')
        : selectedBranch
          ? text(`订单同比 ${orderTrend.text} · 客单价同比 ${aovTrend.text}`, `Orders ${orderTrend.text} · AOV ${aovTrend.text}`)
          : text(`${branches.length - summary.decliningBranches - summary.newBranches} 家增长或持平 · ${summary.newBranches} 家无同期`, `${branches.length - summary.decliningBranches - summary.newBranches} growing or flat · ${summary.newBranches} new`)
      : text('开启自动对比后显示同期趋势', 'Enable comparison to see trends')

  const refreshAllSections = () => {
    setRefresh(value => value + 1)
  }

  const commitFilters = (nextSelection: DateSelection, nextBranch: { branchCode: string; branchName: string } | null) => {
    setSelection(nextSelection)
    setSelectedBranch(nextBranch)
    navigate({ pathname: location.pathname, search: buildRevenueOverviewSearch(nextSelection, nextBranch?.branchCode ?? null) }, { replace: true })
  }

  const selectBranch = (branchCode: string, branchName: string) => {
    const nextBranch = normalizeCode(selectedBranch?.branchCode) === normalizeCode(branchCode)
      ? null
      : { branchCode, branchName }
    commitFilters(selection, nextBranch)
  }

  const updateRange = (range: { startDate: string; endDate: string }) => {
    const nextSelection: DateSelection = { ...selection, ...range, quick: 'custom' }
    commitFilters(nextSelection, selectedBranch)
  }

  const openSalesDetail = (branchCode: string) => {
    navigate(buildSalesDetailPath(branchCode, selection))
  }

  const renderTrend = (current: number | null | undefined, previous: number | null | undefined) => {
    const trend = localizeTrend(getRevenueTrend(current, previous))
    return <span className={`${styles.trend} ${styles[trend.tone]}`}>{trend.text}</span>
  }

  return (
    <main className={styles.pageContainer}>
      <header className={styles.pageHeader}>
        <div>
          <h1>{text('营业额报告', 'Revenue report')}</h1>
          <p>{text('关键业绩、门店排名与时段表现集中在一个决策视图。', 'Revenue, branch rankings and hourly performance in one decision view.')}</p>
        </div>
        <div className={styles.scopeContext} aria-live="polite">
          <span>{text('当前范围', 'Current scope')}</span>
          <strong>{scopeLabel}</strong>
          {selectedBranch && (
            <button type="button" onClick={() => commitFilters(selection, null)}>
              {text('清除门店', 'Clear branch')}
            </button>
          )}
        </div>
      </header>

      <ReportControls
        value={selection}
        onChange={nextSelection => commitFilters(nextSelection, selectedBranch)}
        onRefresh={refreshAllSections}
        loading={allLoading}
      />

      {!hasStoreScope && currentUser && (
        <Alert className={styles.scopeAlert} type="info" showIcon
          message={text('当前账号没有可访问的分店范围', 'This account has no accessible branch scope')} />
      )}

      <div className={styles.queryStatus} role="status" aria-live="polite">
        {!hasStoreScope
          ? text('未发送营业额查询。', 'No revenue query was sent.')
          : anySlow
          ? text('查询已超过 3 秒，整页统计快照仍在读取。', 'The query has taken over 3 seconds. The whole-page snapshot is still loading.')
          : reportQuery.cached
            ? text('已显示 30 秒内缓存，可手动刷新。', 'Showing the 30-second cache. Refresh to query again.')
            : reportQuery.durationMs != null
              ? text(`整页快照返回 ${Math.round(reportQuery.durationMs)} ms`, `Whole-page snapshot returned in ${Math.round(reportQuery.durationMs)} ms`)
              : text('门店、时段与周层级从同一统计快照加载。', 'Branches, hourly data and weekly hierarchy load from one snapshot.')}
      </div>

      {coreComparePending && (
        <Alert className={styles.scopeAlert} type="info" showIcon
          message={text('同期门店统计尚未齐全；本期业绩正常显示，同比暂显示 —。', 'Branch comparison statistics are incomplete. Current performance remains visible and comparisons show —.')} />
      )}

      <section className={styles.summaryGrid} aria-label={text('核心业绩', 'Key metrics')}>
        <MetricCard label={text('销售额', 'Revenue')} value={formatAud(hasBranchMetrics ? summary.revenue : null)}
          previous={coreCompareAvailable && hasBranchMetrics ? `${text('同期', 'Previous')} ${formatAud(summary.revenueLY)}` : '—'}
          trend={revenueTrend} caption={scopeLabel} loading={branchQuery.loading && !branchQuery.data} />
        <MetricCard label={text('订单数', 'Orders')} value={formatInteger(hasBranchMetrics ? summary.orders : null)}
          previous={coreCompareAvailable && hasBranchMetrics ? `${text('同期', 'Previous')} ${formatInteger(summary.ordersLY)}` : '—'}
          trend={orderTrend} caption={scopeLabel} loading={branchQuery.loading && !branchQuery.data} />
        <MetricCard label={text('客单价', 'AOV')} value={formatAud(hasBranchMetrics ? summary.aov : null, 2)}
          previous={coreCompareAvailable && hasBranchMetrics ? `${text('同期', 'Previous')} ${formatAud(summary.aovLY, 2)}` : '—'}
          trend={aovTrend} caption={text('销售额 ÷ 订单数', 'Revenue ÷ orders')} loading={branchQuery.loading && !branchQuery.data} />
        <article className={styles.metricCard}>
          <span className={styles.metricLabel}>{text('经营信号', 'Business signal')}</span>
          {branchQuery.loading && !branchQuery.data ? <Skeleton.Button active block /> : (
            <>
              <strong className={`${styles.metricValue} ${styles[revenueTrend.tone]}`}>
                {signalValue}
              </strong>
              <div className={styles.metricComparison}>{signalDetail}</div>
              <small>{hasBranchMetrics
                ? selectedBranch
                  ? text('已联动门店与时段数据', 'Branch and hourly data linked')
                  : text('选择门店查看单店表现', 'Select a branch for its performance')
                : text('请检查范围或刷新重试', 'Check the scope or refresh to retry')}</small>
            </>
          )}
        </article>
      </section>

      <section className={styles.analysisGrid} aria-label={text('门店与时段分析', 'Branch and hourly analysis')}>
        <article className={styles.panel}>
          <div className={styles.panelHeader}>
            <div>
              <h2>{text('分店表现', 'Branch performance')}</h2>
              <p>{selectedBranch ? text(`已选择 ${selectedBranch.branchName}`, `${selectedBranch.branchName} selected`) : text('选择门店联动 KPI 与时段表现', 'Select a branch to link KPIs and hourly data')}</p>
            </div>
            <Button type="link" aria-expanded={showAllBranches} onClick={() => setShowAllBranches(value => !value)}>
              {showAllBranches ? text('收起排名', 'Show less') : text(`查看全部 ${branches.length} 家`, `View all ${branches.length}`)}
            </Button>
          </div>
          {branchQuery.error ? (
            <SectionError message={branchQuery.error} text={text} onRetry={refreshAllSections} />
          ) : branchQuery.loading && !branchQuery.data ? (
            <TableSkeleton />
          ) : visibleBranches.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={text('所选范围暂无分店营业额', 'No branch revenue in this range')} />
          ) : (
            <div className={styles.tableScroll}>
              <table className={`${styles.dataTable} ${styles.branchTable}`}>
                <thead>
                  <tr>
                    <th scope="col">{text('排名', 'Rank')}</th>
                    <th scope="col">{text('分店', 'Branch')}</th>
                    <th scope="col"><ColumnPairLabel label={text('销售额', 'Revenue')} text={text} /></th>
                    <th scope="col">{text('销售同比', 'Revenue YoY')}</th>
                    <th scope="col"><ColumnPairLabel label={text('订单数', 'Orders')} text={text} /></th>
                    <th scope="col">{text('订单同比', 'Orders YoY')}</th>
                    <th scope="col"><ColumnPairLabel label={text('客单价', 'AOV')} text={text} /></th>
                    <th scope="col">{text('客单同比', 'AOV YoY')}</th>
                    <th scope="col">{text('反查', 'Drill-down')}</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleBranches.map(branch => {
                    const selected = normalizeCode(selectedBranch?.branchCode) === normalizeCode(branch.branchCode)
                    return (
                      <tr key={branch.branchCode} className={selected ? styles.selectedRow : undefined}>
                        <td className={styles.rankCell}>{String(branch.rank).padStart(2, '0')}</td>
                        <th scope="row">
                          <button type="button" className={styles.branchSelect} aria-pressed={selected}
                            onClick={() => selectBranch(branch.branchCode, branch.branchName)}>
                            <strong>{branch.branchName}</strong><small>{branch.branchCode}</small>
                          </button>
                        </th>
                        <td><ValuePair current={formatAud(branch.revenue)} previous={coreCompareAvailable ? formatAud(branch.revenueLY) : '—'} /></td>
                        <td>{renderTrend(branch.revenue, coreCompareAvailable ? branch.revenueLY : null)}</td>
                        <td><ValuePair current={formatInteger(branch.orderCount)} previous={coreCompareAvailable ? formatInteger(branch.orderCountLY) : '—'} /></td>
                        <td>{renderTrend(branch.orderCount, coreCompareAvailable ? branch.orderCountLY : null)}</td>
                        <td><ValuePair current={formatAud(branch.aov, 2)} previous={coreCompareAvailable ? formatAud(branch.aovLY, 2) : '—'} /></td>
                        <td>{renderTrend(branch.aov, coreCompareAvailable ? branch.aovLY : null)}</td>
                        <td>{access.canViewSalesDetail && <button type="button" className={styles.detailButton} onClick={() => openSalesDetail(branch.branchCode)}>
                          {text('销售明细', 'Sales detail')} <RightOutlined aria-hidden="true" />
                        </button>}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
          <footer className={styles.panelFooter}>
            <span>{text(`显示 ${visibleBranches.length} / ${branches.length} 家门店`, `Showing ${visibleBranches.length} of ${branches.length} branches`)}</span>
            <span>{text('按销售额从高到低', 'Sorted by revenue')}</span>
          </footer>
        </article>

        <article className={styles.panel}>
          <div className={styles.panelHeader}>
            <div><h2>{text('时段表现', 'Hourly performance')}</h2><p>{scopeLabel} · {text('销售密度', 'Sales density')}</p></div>
            {selectedBranch && <Button type="link" onClick={() => commitFilters(selection, null)}>{text('全部门店', 'All branches')}</Button>}
          </div>
          {(hourlyCurrentPending || hourlyComparePending) && (
            <Alert className={styles.sectionAlert} type="info" showIcon message={hourlyCurrentPending
              ? hourlyComparePending
                ? text('本期与同期时段统计尚未齐全；已返回的本期时段继续显示，同比暂显示 —。', 'Current and comparison hourly statistics are incomplete. Available current-hour data remains visible and comparisons show —.')
                : text('本期时段统计尚未齐全；已返回的真实时段继续显示。', 'Current hourly statistics are incomplete; available current-hour data remains visible.')
              : text('同期时段统计尚未齐全；本期时段正常显示，同比暂显示 —。', 'Hourly comparison statistics are incomplete. Current hourly data remains visible and comparisons show —.')} />
          )}
          {hourlyQuery.error ? (
            <SectionError message={hourlyQuery.error} text={text} onRetry={refreshAllSections} />
          ) : hourlyQuery.loading && !hourlyQuery.data ? (
            <TableSkeleton compact />
          ) : hourlyRows.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={text('所选范围暂无时段数据', 'No hourly data in this range')} />
          ) : (
            <div className={styles.tableScroll}>
              <table className={`${styles.dataTable} ${styles.hourlyTable}`}>
                <thead><tr>
                  <th scope="col">{text('时段', 'Hour')}</th><th scope="col">{text('本期', 'Current')}</th>
                  <th scope="col">{text('同期', 'Previous')}</th><th scope="col">{text('同比', 'YoY')}</th><th scope="col">{text('销售密度', 'Density')}</th>
                </tr></thead>
                <tbody>{hourlyRows.map(row => (
                  <tr key={row.hour} className={row.isPeak ? styles.peakRow : undefined}>
                    <th scope="row">{row.hour}</th><td className={styles.numeric}>{formatAud(row.revenue)}</td>
                    <td className={`${styles.numeric} ${styles.mutedValue}`}>{formatAud(hourlyCompareAvailable ? row.revenueLY : null)}</td>
                    <td>{renderTrend(row.revenue, hourlyCompareAvailable ? row.revenueLY : null)}</td>
                    <td><div className={styles.progressCell}><progress value={row.percentage} max={100}>{row.percentage}%</progress><span>{row.percentage}%</span></div></td>
                  </tr>
                ))}</tbody>
              </table>
            </div>
          )}
        </article>
      </section>

      <section className={styles.panel} aria-labelledby="weekly-revenue-title">
        <div className={styles.panelHeader}>
          <div><h2 id="weekly-revenue-title">{text('周业绩层级', 'Weekly performance hierarchy')}</h2>
            <p>{text('展开周与分店，再选择周、分店或日期联动上方分析。', 'Expand weeks and branches, then select a week, branch or date to update the analysis above.')}</p></div>
          <span className={styles.panelStatus}>{selection.startDate} — {selection.endDate} · {scopeLabel}</span>
        </div>
        {weeklyComparePending && (
          <Alert className={styles.sectionAlert} type="info" showIcon
            message={text('同期周统计尚未齐全；本期周层级正常显示，同比暂显示 —。', 'Weekly comparison statistics are incomplete. Current hierarchy remains visible and comparisons show —.')} />
        )}
        {weeklyQuery.error ? (
          <SectionError message={weeklyQuery.error} text={text} onRetry={refreshAllSections} />
        ) : weeklyQuery.loading && !weeklyQuery.data ? (
          <TableSkeleton />
        ) : !weeklyQuery.data?.length ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={text('所选范围暂无周业绩数据', 'No weekly data in this range')} />
        ) : (
          <RevenueWeeklyHierarchy data={weeklyQuery.data} compare={weeklyCompareAvailable}
            selectedBranchCode={selectedBranch?.branchCode ?? null} selectedDate={selectedDate}
            queryRange={{ startDate: selection.startDate, endDate: selection.endDate }}
            onSelectWeek={updateRange} onSelectBranch={selectBranch}
            onSelectDate={(date, branchCode, branchName) => {
              const nextSelection: DateSelection = { ...selection, startDate: date, endDate: date, quick: 'custom' }
              const nextBranch = branchCode ? { branchCode, branchName: branchName || branchCode } : selectedBranch
              commitFilters(nextSelection, nextBranch)
            }} />
        )}
      </section>
    </main>
  )
}

function MetricCard({ label, value, previous, trend, caption, loading }: {
  label: string
  value: string
  previous: string
  trend: { text: string; tone: 'positive' | 'negative' | 'neutral' }
  caption: string
  loading: boolean
}) {
  return <article className={styles.metricCard}>
    <span className={styles.metricLabel}>{label}</span>
    {loading ? <Skeleton.Button active block /> : <>
      <strong className={styles.metricValue}>{value}</strong>
      <div className={styles.metricComparison}><span>{previous}</span><span className={`${styles.trend} ${styles[trend.tone]}`}>{trend.text}</span></div>
      <small>{caption}</small>
    </>}
  </article>
}

function ColumnPairLabel({ label, text }: { label: string; text: (zh: string, en: string) => string }) {
  return <span className={styles.columnPairLabel}><strong>{label}</strong><small>{text('本期 / 同期', 'Current / previous')}</small></span>
}

function ValuePair({ current, previous }: { current: string; previous: string }) {
  return <span className={styles.valuePair}><strong>{current}</strong><small>{previous}</small></span>
}

function SectionError({ message, text, onRetry }: { message: string; text: (zh: string, en: string) => string; onRetry: () => void }) {
  return <Alert className={styles.sectionAlert} type="warning" showIcon
    message={text('此区域加载失败', 'This section could not load')} description={message}
    action={<Button size="small" onClick={onRetry}>{text('重试', 'Retry')}</Button>} />
}

function TableSkeleton({ compact = false }: { compact?: boolean }) {
  return <div className={styles.tableSkeleton} aria-label="loading">
    {Array.from({ length: compact ? 6 : 8 }, (_, index) => <Skeleton.Input key={index} active block />)}
  </div>
}
