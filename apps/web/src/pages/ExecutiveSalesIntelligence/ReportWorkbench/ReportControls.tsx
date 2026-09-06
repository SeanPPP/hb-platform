import { Button, DatePicker, Segmented, Select, Switch } from 'antd'
import { ReloadOutlined } from '@ant-design/icons'
import dayjs from 'dayjs'
import { useTranslation } from 'react-i18next'
import { quickDateSelection, reportPeriod, validPeriod, type DateSelection, type QuickRange } from './logic'
import styles from './report.module.css'

export function useReportText() {
  const { i18n } = useTranslation()
  const english = i18n.language.toLowerCase().startsWith('en')
  return (zh: string, en: string) => english ? en : zh
}

export function ReportControls({ value, onChange, onRefresh, loading }: {
  value: DateSelection; onChange: (value: DateSelection) => void; onRefresh: () => void; loading: boolean
}) {
  const text = useReportText()
  const period = reportPeriod(value)
  const ranges = [
    ['today', text('今天', 'Today')], ['yesterday', text('昨天', 'Yesterday')],
    ['thisWeek', text('本周', 'This week')], ['lastWeek', text('上周', 'Last week')],
    ['thisMonth', text('本月', 'This month')], ['lastMonth', text('上月', 'Last month')],
  ]
  return <div className={styles.controls}>
    <DatePicker.RangePicker allowClear={false} value={[dayjs(value.startDate), dayjs(value.endDate)]}
      disabledDate={(date, info) => date.isAfter(dayjs(), 'day') || Boolean(info.from && Math.abs(date.diff(info.from, 'day')) >= 366)}
      onChange={range => {
        if (!range?.[0] || !range[1]) return
        const startDate = range[0].format('YYYY-MM-DD'), endDate = range[1].format('YYYY-MM-DD')
        if (validPeriod(startDate, endDate)) onChange({ ...value, startDate, endDate, quick: 'custom' })
      }}
      aria-label={text('日期范围，最多366天', 'Date range, up to 366 days')} />
    <Segmented value={value.quick} options={ranges.map(([key, label]) => ({ value: key, label }))}
      onChange={key => onChange({ ...quickDateSelection(key as Exclude<QuickRange, 'custom'>), compare: value.compare, compareMode: value.compareMode })} />
    <div className={styles.compareControls}>
      <Select aria-label={text('同比方式', 'Comparison mode')} value={value.compareMode} disabled={!value.compare}
        options={[{ value: 'ByWeek', label: text('按周同比', 'Same ISO week') }, { value: 'ByDate', label: text('按日期同比', 'Same date') }]}
        onChange={compareMode => onChange({ ...value, compareMode })} />
      <label><Switch size="small" checked={value.compare} onChange={compare => onChange({ ...value, compare })} /> {text('自动对比', 'Compare')}</label>
      <Button icon={<ReloadOutlined />} loading={loading} onClick={onRefresh}>{text('刷新数据', 'Refresh')}</Button>
    </div>
    {value.compare && <small className={styles.compareRange}>{text('同期', 'Previous')} {period.compareStartDate} — {period.compareEndDate}</small>}
  </div>
}

export function MetricPair({ current, previous, format = 'money', compare = true, revenue, compareRevenue, costMetric = false }: {
  current: number | null | undefined; previous?: number | null; format?: 'money' | 'integer' | 'rate'; compare?: boolean; revenue?: number; compareRevenue?: number | null; costMetric?: boolean
}) {
  const text = useReportText()
  const display = (value: number | null | undefined) => {
    if (value == null) return '—'
    if (format === 'rate') return new Intl.NumberFormat('en-AU', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value)
    return new Intl.NumberFormat('en-AU', format === 'integer' ? { maximumFractionDigits: 0 } : { style: 'currency', currency: 'AUD', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value)
  }
  const pending = (value: number | null | undefined, amount: number | null | undefined) => costMetric && value == null && amount != null && amount !== 0
  return <span className={styles.pair} title={pending(current, revenue) ? text('成本待补全', 'Cost pending') : undefined}>
    <strong>{pending(current, revenue) && format === 'rate' ? <small className={styles.costPending}>{text('成本待补全', 'Cost pending')}</small> : display(current)}</strong>
    <small title={pending(previous, compareRevenue) ? text('同期成本待补全', 'Previous cost pending') : undefined}>{compare ? pending(previous, compareRevenue) && format === 'rate' ? text('成本待补全', 'Cost pending') : display(previous) : '—'}</small>
  </span>
}
