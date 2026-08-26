import { FilePdfOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, DatePicker, Empty, Image, Input, Select, Space, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import request from '../../../utils/request'
import {
  RETAIL_PRICE_CHANGES_COLUMN_KEYS,
  buildRetailPriceChangesQuery,
  createRetailPriceChangesRequestCoordinator,
  getBrisbaneMonthRange,
  getRetailPriceChangesViewState,
  normalizeRetailPriceChangesResponse,
  type RetailPriceChangeItem,
  type RetailPriceChangesFilters,
} from './logic'
import {
  buildRetailPriceChangesPdfFileName,
  collectRetailPriceChangesForPdf,
  mapRetailPriceChangesPdfRows,
} from './pdfExport'
import { MeasuredTable } from '../../../components/MeasuredTable'

const { RangePicker } = DatePicker
const API_PATH = '/api/react/v1/warehouse-retail-price-changes'
const RETAIL_PRICE_BARCODE_OPTIONS = { width: 1, height: 22, displayValue: false, margin: 0 }
const PRODUCT_IMAGE_FALLBACK = `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40" viewBox="0 0 40 40"><rect width="40" height="40" rx="4" fill="#f5f5f5"/><circle cx="15" cy="15" r="3" fill="#c7c7c7"/><path d="M9 30l8-9 5 5 4-4 6 8H9z" fill="#d9d9d9"/></svg>',
)}`

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

function formatRetailPrice(value: number | null) {
  return value === null ? '--' : `$${value.toFixed(2)}`
}

function formatBrisbaneDateTime(value: string | undefined, locale: string) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '--'
  return new Intl.DateTimeFormat(locale.startsWith('en') ? 'en-AU' : 'zh-CN', {
    timeZone: 'Australia/Brisbane',
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  }).format(date)
}

export default function RetailPriceChangesPage() {
  const { t, i18n } = useTranslation()
  const initialFilters = useMemo<RetailPriceChangesFilters>(() => ({
    ...getBrisbaneMonthRange(), keyword: '', onlyWithLocation: true,
  }), [])
  const coordinatorRef = useRef(createRetailPriceChangesRequestCoordinator())
  const exportAbortControllerRef = useRef<AbortController | null>(null)
  const [draftFilters, setDraftFilters] = useState(initialFilters)
  const [appliedFilters, setAppliedFilters] = useState(initialFilters)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [items, setItems] = useState<RetailPriceChangeItem[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [exporting, setExporting] = useState(false)

  const load = useCallback(async () => {
    const { requestId, signal } = coordinatorRef.current.start()
    setLoading(true)
    setError(null)
    try {
      const response = await request.get<unknown>(API_PATH, {
        params: buildRetailPriceChangesQuery(appliedFilters, pageNumber, pageSize),
        signal,
      })
      if (!coordinatorRef.current.isLatest(requestId)) return
      const normalized = normalizeRetailPriceChangesResponse(response)
      setItems(normalized.items)
      setTotal(normalized.total)
    } catch (loadError) {
      if (isAbortError(loadError) || !coordinatorRef.current.isLatest(requestId)) return
      setItems([])
      setTotal(0)
      setError(loadError)
    } finally {
      if (coordinatorRef.current.isLatest(requestId)) setLoading(false)
    }
  }, [appliedFilters, pageNumber, pageSize])

  useEffect(() => { void load() }, [load])
  useEffect(() => () => {
    coordinatorRef.current.dispose()
    exportAbortControllerRef.current?.abort()
  }, [])

  const columns = useMemo<ColumnsType<RetailPriceChangeItem>>(() => [
    {
      title: t('warehouse.retailPriceChanges.columns.image'), key: RETAIL_PRICE_CHANGES_COLUMN_KEYS[0], width: 72,
      render: (_, record) => <Image preview={false} src={record.productImage || PRODUCT_IMAGE_FALLBACK} fallback={PRODUCT_IMAGE_FALLBACK} width={40} height={40} style={{ objectFit: 'contain' }} />,
    },
    {
      title: t('warehouse.retailPriceChanges.columns.itemNumber'), dataIndex: 'itemNumber', key: RETAIL_PRICE_CHANGES_COLUMN_KEYS[1], width: 170,
      render: (value: string | undefined) => value || '--',
    },
    {
      title: t('warehouse.retailPriceChanges.columns.barcode'), dataIndex: 'barcode', key: RETAIL_PRICE_CHANGES_COLUMN_KEYS[2], width: 190,
      render: (value: string | undefined) => value ? (
        <BarcodePreview
          value={value}
          align="left"
          showCopy={false}
          textNoWrap
          gap={2}
          options={RETAIL_PRICE_BARCODE_OPTIONS}
        />
      ) : '--',
    },
    {
      title: t('warehouse.retailPriceChanges.columns.latestRetailPrice'), dataIndex: 'latestRetailPrice', key: RETAIL_PRICE_CHANGES_COLUMN_KEYS[3], align: 'right', width: 160,
      render: formatRetailPrice,
    },
    {
      title: t('warehouse.retailPriceChanges.columns.lastChangedAt'), dataIndex: 'lastPriceChangedAtUtc', key: RETAIL_PRICE_CHANGES_COLUMN_KEYS[4], width: 210,
      render: (value: string | undefined) => formatBrisbaneDateTime(value, i18n.language),
    },
  ], [i18n.language, t])

  const viewState = getRetailPriceChangesViewState(loading, error, items.length)
  const emptyText = viewState === 'error'
    ? <Empty description={t('warehouse.retailPriceChanges.loadFailed')}><Button size="small" icon={<ReloadOutlined />} onClick={() => void load()}>{t('common.retry', '重试')}</Button></Empty>
    : <Empty description={t('warehouse.retailPriceChanges.empty')} />

  const handleSearch = () => {
    setAppliedFilters({ ...draftFilters, keyword: draftFilters.keyword.trim() })
    setPageNumber(1)
  }

  const handleResetMonth = () => {
    const nextFilters: RetailPriceChangesFilters = { ...getBrisbaneMonthRange(), keyword: '', onlyWithLocation: true }
    setDraftFilters(nextFilters)
    setAppliedFilters(nextFilters)
    setPageNumber(1)
  }

  const handleExportPdf = async () => {
    const exportFilters = { ...appliedFilters }
    exportAbortControllerRef.current?.abort()
    const controller = new AbortController()
    exportAbortControllerRef.current = controller
    setExporting(true)

    try {
      const exportItems = await collectRetailPriceChangesForPdf(async (nextPageNumber, nextPageSize) => {
        const response = await request.get<unknown>(API_PATH, {
          params: buildRetailPriceChangesQuery(exportFilters, nextPageNumber, nextPageSize),
          signal: controller.signal,
        })
        return normalizeRetailPriceChangesResponse(response)
      })

      if (!exportItems.length) {
        message.warning(t('warehouse.retailPriceChanges.pdf.empty'))
        return
      }

      const { exportContainerDetailsToPdf } = await import('../../../services/exportService')
      const pdfRows = mapRetailPriceChangesPdfRows(
        exportItems,
        (value) => formatBrisbaneDateTime(value, i18n.language),
      )
      await exportContainerDetailsToPdf(pdfRows, {
        columns: [
          { header: t('warehouse.retailPriceChanges.columns.image'), key: 'productImage', width: 10 },
          { header: t('warehouse.retailPriceChanges.columns.itemNumber'), key: 'itemNumber', width: 18 },
          { header: t('warehouse.retailPriceChanges.columns.barcode'), key: 'barcodeImage', width: 28 },
          { header: t('warehouse.retailPriceChanges.columns.latestRetailPrice'), key: 'latestRetailPrice', width: 18, valueType: 'money' },
          { header: t('warehouse.retailPriceChanges.columns.lastChangedAt'), key: 'lastPriceChangedAt', width: 24 },
        ],
        summary: {
          title: t('warehouse.retailPriceChanges.title'),
          rows: [
            [
              { label: t('warehouse.retailPriceChanges.pdf.dateRange'), value: `${exportFilters.startDate} - ${exportFilters.endDate}` },
              {
                label: t('warehouse.retailPriceChanges.pdf.productScope'),
                value: exportFilters.onlyWithLocation
                  ? t('warehouse.retailPriceChanges.onlyWithLocation')
                  : t('warehouse.retailPriceChanges.allProducts'),
              },
            ],
            [
              { label: t('warehouse.retailPriceChanges.pdf.keyword'), value: exportFilters.keyword || '--' },
              { label: t('warehouse.retailPriceChanges.pdf.recordCount'), value: exportItems.length, valueType: 'integer' },
            ],
          ],
        },
        fileName: buildRetailPriceChangesPdfFileName(
          t('warehouse.retailPriceChanges.pdf.fileName'),
          exportFilters,
        ),
        pdfRenderScale: 2,
        pdfImageFormat: 'JPEG',
        pdfImageQuality: 0.95,
      })
      message.success(t('warehouse.retailPriceChanges.pdf.success', { count: exportItems.length }))
    } catch (exportError) {
      if (!isAbortError(exportError)) {
        message.error(getErrorMessage(exportError, t('warehouse.retailPriceChanges.pdf.failed')))
      }
    } finally {
      if (exportAbortControllerRef.current === controller) {
        exportAbortControllerRef.current = null
        setExporting(false)
      }
    }
  }

  const rangeValue: [Dayjs, Dayjs] = [dayjs(draftFilters.startDate), dayjs(draftFilters.endDate)]

  return <PageContainer title={t('warehouse.retailPriceChanges.title')} subtitle={t('warehouse.retailPriceChanges.subtitle')}>
    <Card size="small">
      <Space wrap size={[8, 8]} style={{ marginBottom: 12 }}>
        <RangePicker allowClear={false} value={rangeValue} onChange={(range) => {
          const [start, end] = range ?? []
          if (!start || !end) return
          setDraftFilters((current) => ({ ...current, startDate: start.format('YYYY-MM-DD'), endDate: end.format('YYYY-MM-DD') }))
        }} />
        <Input allowClear value={draftFilters.keyword} placeholder={t('warehouse.retailPriceChanges.keywordPlaceholder')} style={{ width: 220 }} onChange={(event) => setDraftFilters((current) => ({ ...current, keyword: event.target.value }))} onPressEnter={handleSearch} />
        <Select value={draftFilters.onlyWithLocation} style={{ width: 128 }} options={[
          { value: true, label: t('warehouse.retailPriceChanges.onlyWithLocation') },
          { value: false, label: t('warehouse.retailPriceChanges.allProducts') },
        ]} onChange={(onlyWithLocation) => setDraftFilters((current) => ({ ...current, onlyWithLocation }))} />
        <Button type="primary" icon={<SearchOutlined />} onClick={handleSearch}>{t('common.search')}</Button>
        <Button onClick={handleResetMonth}>{t('warehouse.retailPriceChanges.resetMonth')}</Button>
        <Button icon={<FilePdfOutlined />} loading={exporting} onClick={() => void handleExportPdf()}>
          {t('warehouse.retailPriceChanges.pdf.export')}
        </Button>
      </Space>
      <Alert showIcon type="info" style={{ marginBottom: 12 }} message={t('warehouse.retailPriceChanges.auditNotice')} />
      {error ? <Typography.Text type="danger" style={{ display: 'block', marginBottom: 8 }}>{getErrorMessage(error, t('warehouse.retailPriceChanges.loadFailed'))}</Typography.Text> : null}
      <MeasuredTable metricId="warehouse.retail-price-changes.table-1"
        size="small"
        loading={loading}
        rowKey="productCode"
        columns={columns}
        dataSource={items}
        locale={{ emptyText }}
        scroll={{ x: 820 }}
        pagination={{
          current: pageNumber, pageSize, total, showSizeChanger: true,
          showTotal: (count) => t('warehouse.retailPriceChanges.total', { count }),
          onChange: (nextPage, nextPageSize) => { setPageNumber(nextPage); setPageSize(nextPageSize) },
        }}
      />
    </Card>
  </PageContainer>
}
