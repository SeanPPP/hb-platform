import { Alert, Button, Drawer, Skeleton } from 'antd'
import { useMemo, useState } from 'react'
import { MetricPair, useReportText } from '../ReportWorkbench/ReportControls'
import { growth } from '../ReportWorkbench/logic'
import { useReportQuery } from '../ReportWorkbench/useReportQuery'
import { productBranchDrawerQuery } from './logic'
import { fetchSalesDetailReport, type SalesDetailQuery, type SalesDetailRow } from './reportService'
import styles from './styles.module.css'

interface ProductBranchDrawerProps {
  open: boolean
  product: SalesDetailRow | null
  baseQuery: SalesDetailQuery
  onClose: () => void
}

function GrowthCell({ current, previous, compare }: { current: number; previous: number | null; compare: boolean }) {
  const text = useReportText()
  const value = compare ? growth(current, previous) : null
  return <span className={typeof value === 'number' ? value > 0 ? styles.positive : value < 0 ? styles.negative : styles.muted : styles.muted}>
    {value === null ? '—' : value === 'new' ? text('新增', 'New') : `${value > 0 ? '+' : ''}${(value * 100).toFixed(1)}%`}
  </span>
}

/** 商品反查独立读取分店段，关闭或切换商品时由 useReportQuery 取消在途请求。 */
export default function ProductBranchDrawer({ open, product, baseQuery, onClose }: ProductBranchDrawerProps) {
  const text = useReportText()
  const [refresh, setRefresh] = useState(0)
  const drawerQuery = useMemo(() => product ? productBranchDrawerQuery(baseQuery, product.code) : baseQuery,
    [baseQuery, product])
  const queryKey = JSON.stringify([drawerQuery, product?.code])
  const branches = useReportQuery(
    `sales-detail:product-branches:${queryKey}`,
    signal => fetchSalesDetailReport(drawerQuery, signal, ['branches']),
    { active: open, enabled: open && !!product, refresh, metricId: 'sales-detail-product-branches' },
  )
  const page = branches.data?.branches
  const period = baseQuery.compareStartDate && baseQuery.compareEndDate
    ? `${text('同期', 'Previous')} ${baseQuery.compareStartDate} — ${baseQuery.compareEndDate}`
    : undefined

  return <Drawer
    className={styles.productDrawer}
    open={open}
    placement="right"
    width="min(760px, 100vw)"
    destroyOnHidden
    onClose={onClose}
    title={product && <div className={styles.drawerTitle}>
      <strong>{product.name || product.code}</strong>
      <span>{text('货号', 'Item')} {product.itemNumber || product.code}</span>
      <small>{baseQuery.startDate} — {baseQuery.endDate}{period ? ` · ${period}` : ''}</small>
    </div>}
  >
    {!product ? null : <div className={styles.drawerBody} data-testid="product-branch-drawer">
      <div className={styles.drawerIntro}>
        <span>{text('商品在各分店的表现', 'Product performance by store')}</span>
        {page && <small>{page.total} {text('家分店', 'stores')}</small>}
      </div>
      <div className={styles.drawerLegend}>{period ? text('本期在上 · 同期在下', 'Current above · Previous below') : text('当前期间', 'Current period')} · AUD</div>
      {branches.error ? <div className={styles.drawerEmpty}>
        <Alert type="warning" message={branches.error} />
        <Button onClick={() => setRefresh(value => value + 1)}>{text('重试', 'Retry')}</Button>
      </div> : branches.loading ? <div className={styles.drawerSkeleton}>
        <Skeleton active paragraph={{ rows: 8 }} title={false} />
      </div> : !page || page.total === 0 ? <div className={styles.drawerEmpty}>
        {text('当前条件下没有分店数据', 'No store data for these filters')}
      </div> : <div className={styles.drawerScroll} tabIndex={0} aria-label={text('商品分店数据可滚动表格', 'Product store data scrollable table')}>
        <table className={styles.drawerTable}>
          <thead><tr>
            <th>{text('分店名称 / 编码', 'Store / Code')}</th>
            <th>{text('营业额', 'Revenue')}</th>
            <th>{text('商品数量', 'Product quantity')}</th>
            <th>{text('均价', 'Average price')}</th>
            <th>{text('同比', 'Growth')}</th>
            <th>{text('毛利额', 'Gross profit')}</th>
            <th>{text('毛利率', 'Margin')}</th>
          </tr></thead>
          <tbody>{page.rows.map(row => <tr key={row.code}>
            <td><strong>{row.name || row.code}</strong><small>{row.code}</small></td>
            <td><MetricPair current={row.revenue} previous={row.compareRevenue} compare={!!baseQuery.compareStartDate} /></td>
            <td><MetricPair current={row.quantity} previous={row.compareQuantity} compare={!!baseQuery.compareStartDate} format="integer" /></td>
            <td><MetricPair current={row.averageUnitPrice} previous={row.compareAverageUnitPrice} compare={!!baseQuery.compareStartDate} /></td>
            <td><GrowthCell current={row.revenue} previous={row.compareRevenue} compare={!!baseQuery.compareStartDate} /></td>
            <td><MetricPair current={row.grossProfit} previous={row.compareGrossProfit} compare={!!baseQuery.compareStartDate} costMetric revenue={row.revenue} compareRevenue={row.compareRevenue} /></td>
            <td><MetricPair current={row.grossMarginRate} previous={row.compareGrossMarginRate} compare={!!baseQuery.compareStartDate} format="rate" costMetric revenue={row.revenue} compareRevenue={row.compareRevenue} /></td>
          </tr>)}</tbody>
        </table>
      </div>}
    </div>}
  </Drawer>
}
