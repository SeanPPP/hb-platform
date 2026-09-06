import { useEffect, useMemo, useState } from 'react'
import { MetricPair, useReportText } from '../ReportWorkbench/ReportControls'
import { getHierarchyBranchCode, getHierarchyDateRange, getRevenueTrend } from './logic'
import type { RevenueWeeklyNode } from './types'
import styles from '../styles.module.css'

interface RevenueWeeklyHierarchyProps {
  data: RevenueWeeklyNode[]
  compare: boolean
  selectedBranchCode: string | null
  selectedDate: string | null
  queryRange: { startDate: string; endDate: string }
  onSelectWeek: (range: { startDate: string; endDate: string }) => void
  onSelectBranch: (branchCode: string, branchName: string) => void
  onSelectDate: (date: string, branchCode: string | null, branchName: string | null) => void
}

function normalize(value: string | null | undefined) {
  return value?.trim().toLocaleLowerCase('en-AU') ?? ''
}

export default function RevenueWeeklyHierarchy({
  data,
  compare,
  selectedBranchCode,
  selectedDate,
  queryRange,
  onSelectWeek,
  onSelectBranch,
  onSelectDate,
}: RevenueWeeklyHierarchyProps) {
  const text = useReportText()
  const [expandedKeys, setExpandedKeys] = useState<Set<string>>(() => new Set())

  useEffect(() => {
    // 首次展示一层真实分店，不要求用户先猜测层级表是否可展开。
    const validKeys = new Set<string>()
    const collect = (node: RevenueWeeklyNode) => {
      validKeys.add(node.key)
      node.children?.forEach(collect)
    }
    data.forEach(collect)
    setExpandedKeys(current => {
      const next = new Set([...current].filter(key => validKeys.has(key)))
      if (data[0] && !data.some(node => next.has(node.key))) next.add(data[0].key)
      return next.size === current.size && [...next].every(key => current.has(key)) ? current : next
    })
  }, [data])

  const rows = useMemo(() => {
    const output: Array<{ node: RevenueWeeklyNode; depth: number; branchCode: string | null; branchName: string | null }> = []
    const visit = (node: RevenueWeeklyNode, depth: number, parentBranchName: string | null) => {
      const branchCode = getHierarchyBranchCode(node)
      const branchName = node.level === 'branch' ? node.hierarchy : parentBranchName
      output.push({ node, depth, branchCode, branchName })
      if (expandedKeys.has(node.key)) node.children?.forEach(child => visit(child, depth + 1, branchName))
    }
    data.forEach(node => visit(node, 0, null))
    return output
  }, [data, expandedKeys])

  const toggle = (key: string) => {
    setExpandedKeys(current => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  const renderTrend = (current: number, previous: number) => {
    const trend = getRevenueTrend(current, compare ? previous : null)
    const trendText = trend.text === 'new' ? text('新增', 'New') : trend.text
    return <span className={`${styles.trend} ${styles[trend.tone]}`}>{trendText}</span>
  }

  return (
    <div className={styles.tableScroll}>
      <table className={`${styles.dataTable} ${styles.weeklyTable}`}>
        <thead>
          <tr>
            <th scope="col">{text('周 / 分店 / 日期', 'Week / branch / date')}</th>
            <th scope="col"><span className={styles.columnPairLabel}><strong>{text('销售额', 'Revenue')}</strong><small>{text('本期 / 同期', 'Current / previous')}</small></span></th>
            <th scope="col">{text('销售同比', 'Revenue YoY')}</th>
            <th scope="col"><span className={styles.columnPairLabel}><strong>{text('订单数', 'Orders')}</strong><small>{text('本期 / 同期', 'Current / previous')}</small></span></th>
            <th scope="col">{text('订单同比', 'Orders YoY')}</th>
            <th scope="col"><span className={styles.columnPairLabel}><strong>{text('客单价', 'AOV')}</strong><small>{text('本期 / 同期', 'Current / previous')}</small></span></th>
            <th scope="col">{text('客单同比', 'AOV YoY')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ node, depth, branchCode, branchName }) => {
            const hasChildren = Boolean(node.children?.length)
            const expanded = expandedKeys.has(node.key)
            const selected = node.level === 'date'
              ? selectedDate === node.hierarchy && normalize(selectedBranchCode) === normalize(branchCode)
              : node.level === 'branch' && normalize(selectedBranchCode) === normalize(branchCode)
            const selectNode = () => {
              if (node.level === 'week') {
                const range = getHierarchyDateRange(node, queryRange)
                if (range) onSelectWeek(range)
                return
              }
              if (node.level === 'branch' && branchCode) {
                onSelectBranch(branchCode, node.hierarchy)
                return
              }
              if (node.level === 'date') onSelectDate(node.hierarchy, branchCode, branchName)
            }
            return (
              <tr key={node.key} className={selected ? styles.selectedRow : undefined}>
                <th scope="row">
                  <div className={styles.hierarchyCell} style={{ paddingInlineStart: depth * 20 }}>
                    {hasChildren ? (
                      <button
                        type="button"
                        className={styles.expandButton}
                        aria-expanded={expanded}
                        aria-label={expanded
                          ? text(`收起 ${node.hierarchy}`, `Collapse ${node.hierarchy}`)
                          : text(`展开 ${node.hierarchy}`, `Expand ${node.hierarchy}`)}
                        onClick={() => toggle(node.key)}
                        onKeyDown={event => {
                          if (event.key === 'ArrowRight' && !expanded) {
                            event.preventDefault()
                            toggle(node.key)
                          }
                          if (event.key === 'ArrowLeft' && expanded) {
                            event.preventDefault()
                            toggle(node.key)
                          }
                        }}
                      >
                        <span aria-hidden="true">{expanded ? '−' : '+'}</span>
                      </button>
                    ) : <span className={styles.hierarchyDot} aria-hidden="true" />}
                    <button
                      type="button"
                      className={styles.hierarchySelect}
                      aria-pressed={selected}
                      onClick={selectNode}
                    >
                      <strong>{node.hierarchy}</strong>
                      <small>{node.level === 'week' ? text('选择本周范围', 'Use this week') : node.level === 'branch' ? text('联动此分店', 'Filter this branch') : text('联动此日', 'Use this date')}</small>
                    </button>
                  </div>
                </th>
                <td><MetricPair current={node.revenue} previous={node.revenueLY} compare={compare} /></td>
                <td>{renderTrend(node.revenue, node.revenueLY)}</td>
                <td><MetricPair current={node.orders} previous={node.ordersLY} compare={compare} format="integer" /></td>
                <td>{renderTrend(node.orders, node.ordersLY)}</td>
                <td><MetricPair current={node.aov} previous={node.aovLY} compare={compare} /></td>
                <td>{renderTrend(node.aov, node.aovLY)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
