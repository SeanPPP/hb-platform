import { SearchOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  App as AntdApp,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Form,
  Image,
  Input,
  Modal,
  Row,
  Select,
  Space,
  Statistic,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { InputRef } from 'antd/es/input'
import type { SorterResult } from 'antd/es/table/interface'
import type { Dayjs } from 'dayjs'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import {
  getStoreOrderImportPriceVariance,
  getStoreOrderImportPriceVarianceDetails,
  updateStoreOrderImportPriceVarianceDomesticPrice,
  updateStoreOrderImportPriceVarianceWarehouseImportPrice,
} from '../../../services/storeOrderService'
import type { ChinaSupplierItem } from '../../../types/chinaSupplier'
import type {
  StoreOrderImportPriceVarianceDetailItem,
  StoreOrderImportPriceVarianceDirection,
  StoreOrderImportPriceVarianceItem,
  StoreOrderImportPriceVarianceQuery,
  StoreOrderImportPriceVarianceSummary,
  StoreOrderImportPriceVarianceSupplierSummary,
} from '../../../types/storeOrder'

const { RangePicker } = DatePicker

type RangeValue = [Dayjs | null, Dayjs | null] | null
type EditablePriceField = 'domesticPrice' | 'warehouseImportPrice'

interface FilterValues {
  keyword?: string
  storeCode?: string
  supplierCode?: string
  orderNo?: string
  orderDateRange?: RangeValue
  varianceDirection?: StoreOrderImportPriceVarianceDirection
}

interface AppliedFilters {
  keyword?: string
  storeCode?: string
  supplierCode?: string
  orderNo?: string
  startDate?: string
  endDate?: string
  varianceDirection: StoreOrderImportPriceVarianceDirection
}

interface SupplierOption {
  label: string
  value: string
}

interface DomesticSupplierFilterSelectProps {
  value?: string
  loading: boolean
  options: SupplierOption[]
  placeholder: string
  onChange?: (value?: string) => void
  onOpenChange: (open: boolean) => void
}

const DEFAULT_PAGE_SIZE = 20
const DEFAULT_SORT_BY = 'absoluteVarianceAmount'
const DEFAULT_SORT_DESCENDING = true
const DEFAULT_DETAIL_SORT_BY = 'orderDate'
const DEFAULT_DETAIL_SORT_DESCENDING = true

const emptySummary: StoreOrderImportPriceVarianceSummary = {
  totalRows: 0,
  originalImportAmountTotal: 0,
  baselineImportAmountTotal: 0,
  varianceAmountTotal: 0,
}

function trimText(value?: string) {
  const text = value?.trim()
  return text || undefined
}

function formatDate(value?: string, language?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleDateString(language?.startsWith('zh') ? 'zh-CN' : 'en-US')
}

function formatMoney(value?: number) {
  return (value ?? 0).toFixed(2)
}

function formatNumber(value?: number, fractionDigits = 2) {
  const number = value ?? 0
  return Number.isInteger(number) ? String(number) : number.toFixed(fractionDigits)
}

function parsePriceDraft(value: string) {
  const normalized = value.trim().replace(/,/g, '')
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  if (!Number.isFinite(parsed) || parsed < 0) {
    return null
  }

  return Math.round(parsed * 100) / 100
}

// 供应商统计接口一次性返回完整聚合结果，列排序在前端本地完成。
function compareSupplierText(left?: string, right?: string) {
  return (left || '').localeCompare(right || '', 'zh-Hans-CN', { numeric: true, sensitivity: 'base' })
}

function compareSupplierNumber(left?: number, right?: number) {
  return (left ?? 0) - (right ?? 0)
}

function getRowKey(row: StoreOrderImportPriceVarianceItem) {
  return row.productCode || row.itemNumber || row.productName || 'product'
}

function getEditablePriceInputKey(row: StoreOrderImportPriceVarianceItem, field: EditablePriceField) {
  return `${field}:${getRowKey(row)}`
}

function getEditablePriceValue(row: StoreOrderImportPriceVarianceItem, field: EditablePriceField) {
  return field === 'domesticPrice' ? row.domesticPrice : row.warehouseImportPrice
}

function getSupplierSummaryRowKey(row: StoreOrderImportPriceVarianceSupplierSummary) {
  return row.supplierCode || row.supplierName || 'unknown-supplier'
}

function getDetailRowKey(row: StoreOrderImportPriceVarianceDetailItem) {
  return `${row.orderGUID || 'order'}-${row.detailGUID || row.productCode || row.itemNumber || 'detail'}`
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

function normalizeFilters(values: FilterValues): AppliedFilters {
  return {
    keyword: trimText(values.keyword),
    storeCode: trimText(values.storeCode),
    supplierCode: trimText(values.supplierCode),
    orderNo: trimText(values.orderNo),
    // 日期范围统一落成后端契约字段，避免页面状态保存 Dayjs 对象。
    startDate: values.orderDateRange?.[0]?.format('YYYY-MM-DD'),
    endDate: values.orderDateRange?.[1]?.format('YYYY-MM-DD'),
    varianceDirection: values.varianceDirection ?? 'all',
  }
}

function DomesticSupplierFilterSelect({
  value,
  loading,
  options,
  placeholder,
  onChange,
  onOpenChange,
}: DomesticSupplierFilterSelectProps) {
  return (
    <Select
      allowClear
      showSearch
      value={value}
      loading={loading}
      options={options}
      placeholder={placeholder}
      onChange={onChange}
      onOpenChange={onOpenChange}
      filterOption={(input, option) =>
        String(option?.label ?? '').toLowerCase().includes(input.trim().toLowerCase()) ||
        String(option?.value ?? '').toLowerCase().includes(input.trim().toLowerCase())
      }
    />
  )
}

export default function StoreOrderImportPriceVariancePage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { message } = AntdApp.useApp()
  const [form] = Form.useForm<FilterValues>()
  const [filters, setFilters] = useState<AppliedFilters>({ varianceDirection: 'all' })
  const [items, setItems] = useState<StoreOrderImportPriceVarianceItem[]>([])
  const [summary, setSummary] = useState<StoreOrderImportPriceVarianceSummary>(emptySummary)
  const [supplierSummaries, setSupplierSummaries] = useState<StoreOrderImportPriceVarianceSupplierSummary[]>([])
  const [total, setTotal] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [sortBy, setSortBy] = useState(DEFAULT_SORT_BY)
  const [sortDescending, setSortDescending] = useState(DEFAULT_SORT_DESCENDING)
  const [loading, setLoading] = useState(false)
  const tableRegionRef = useRef<HTMLDivElement | null>(null)
  const [tableScrollY, setTableScrollY] = useState(480)
  const priceInputRefs = useRef<Record<string, InputRef | null>>({})
  const [editingPriceKey, setEditingPriceKey] = useState<string | null>(null)
  const [priceDrafts, setPriceDrafts] = useState<Record<string, string>>({})
  // 键盘保存会紧接着触发 blur，用 ref 做同步防重，避免重复提交同一格。
  const savingPriceKeyRef = useRef<string | null>(null)
  const [savingPriceKey, setSavingPriceKey] = useState<string | null>(null)
  const supplierSummaryRegionRef = useRef<HTMLDivElement | null>(null)
  const [supplierSummaryTableScrollY, setSupplierSummaryTableScrollY] = useState(360)
  const [supplierOptions, setSupplierOptions] = useState<SupplierOption[]>([])
  const [supplierLoading, setSupplierLoading] = useState(false)
  const supplierOptionsLoadedRef = useRef(false)
  const supplierRequestControllerRef = useRef<AbortController | null>(null)
  const [detailModalOpen, setDetailModalOpen] = useState(false)
  const [selectedProduct, setSelectedProduct] = useState<StoreOrderImportPriceVarianceItem | null>(null)
  const [detailItems, setDetailItems] = useState<StoreOrderImportPriceVarianceDetailItem[]>([])
  const [detailSummary, setDetailSummary] = useState<StoreOrderImportPriceVarianceSummary>(emptySummary)
  const [detailTotal, setDetailTotal] = useState(0)
  const [detailPageNumber, setDetailPageNumber] = useState(1)
  const [detailPageSize, setDetailPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [detailSortBy, setDetailSortBy] = useState(DEFAULT_DETAIL_SORT_BY)
  const [detailSortDescending, setDetailSortDescending] = useState(DEFAULT_DETAIL_SORT_DESCENDING)
  const [detailLoading, setDetailLoading] = useState(false)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const query: StoreOrderImportPriceVarianceQuery = {
        ...filters,
        pageNumber,
        pageSize,
        sortBy,
        sortDescending,
      }
      const result = await getStoreOrderImportPriceVariance(query)
      setItems(result.items)
      setTotal(result.total)
      setPageNumber(result.page)
      setPageSize(result.pageSize)
      setSummary(result.summary)
      setSupplierSummaries(result.supplierSummaries)
    } catch (error) {
      console.error(error)
      void message.error(t('storeOrders.importPriceVariance.loadFailed'))
    } finally {
      setLoading(false)
    }
  }, [filters, message, pageNumber, pageSize, sortBy, sortDescending, t])

  useEffect(() => {
    void loadData()
  }, [loadData])

  useEffect(
    () => () => {
      supplierRequestControllerRef.current?.abort()
    },
    [],
  )

  useLayoutEffect(() => {
    let frameId: number | null = null

    const readOuterHeight = (element: HTMLElement | null) => {
      if (!element) {
        return 0
      }

      const style = window.getComputedStyle(element)
      const marginTop = Number.parseFloat(style.marginTop) || 0
      const marginBottom = Number.parseFloat(style.marginBottom) || 0
      return Math.ceil(element.getBoundingClientRect().height + marginTop + marginBottom)
    }

    const measureTableBodyScrollY = (region: HTMLElement | null, minTableBodyHeight: number) => {
      if (!region) {
        return null
      }

      const tableHeader = region.querySelector('.ant-table-thead') as HTMLElement | null
      const tableBody = region.querySelector('.ant-table-body') as HTMLElement | null
      const pagination = region.querySelector('.ant-table-pagination') as HTMLElement | null
      const tableHeaderHeight = readOuterHeight(tableHeader)
      const paginationHeight = readOuterHeight(pagination)
      const horizontalScrollbarHeight = tableBody ? Math.max(0, tableBody.offsetHeight - tableBody.clientHeight) : 0
      const innerPadding = 8

      return Math.max(
        Math.floor(
          region.clientHeight -
            tableHeaderHeight -
            paginationHeight -
            horizontalScrollbarHeight -
            innerPadding,
        ),
        minTableBodyHeight,
      )
    }

    const measureTableScrollY = () => {
      // 主表和供应商统计都把滚动限制在表格 body 内，避免整页被长表格撑开。
      const nextScrollY = measureTableBodyScrollY(tableRegionRef.current, 260)
      const nextSupplierScrollY = measureTableBodyScrollY(supplierSummaryRegionRef.current, 240)

      if (nextScrollY != null) {
        setTableScrollY((current) => (Math.abs(current - nextScrollY) > 4 ? nextScrollY : current))
      }
      if (nextSupplierScrollY != null) {
        setSupplierSummaryTableScrollY((current) =>
          Math.abs(current - nextSupplierScrollY) > 4 ? nextSupplierScrollY : current,
        )
      }
    }

    const scheduleMeasure = () => {
      if (frameId != null) {
        window.cancelAnimationFrame(frameId)
      }
      frameId = window.requestAnimationFrame(measureTableScrollY)
    }

    scheduleMeasure()
    window.addEventListener('resize', scheduleMeasure)

    if (typeof ResizeObserver === 'undefined') {
      return () => {
        if (frameId != null) window.cancelAnimationFrame(frameId)
        window.removeEventListener('resize', scheduleMeasure)
      }
    }

    const observer = new ResizeObserver(scheduleMeasure)
    if (tableRegionRef.current) {
      observer.observe(tableRegionRef.current)
    }
    if (supplierSummaryRegionRef.current) {
      observer.observe(supplierSummaryRegionRef.current)
    }

    return () => {
      if (frameId != null) window.cancelAnimationFrame(frameId)
      observer.disconnect()
      window.removeEventListener('resize', scheduleMeasure)
    }
  }, [i18n.language, items.length, pageSize, supplierSummaries.length, total])

  const loadSupplierOptions = useCallback(async () => {
    if (supplierOptionsLoadedRef.current || supplierLoading) {
      return
    }

    supplierRequestControllerRef.current?.abort()
    const currentController = new AbortController()
    supplierRequestControllerRef.current = currentController
    setSupplierLoading(true)

    try {
      const suppliers: ChinaSupplierItem[] = await getActiveChinaSuppliers(currentController.signal)
      if (supplierRequestControllerRef.current !== currentController) {
        return
      }

      // 供应商筛选只使用国内供应商编码，后端会按 DomesticProduct.SupplierCode 汇总商品。
      setSupplierOptions(
        suppliers
          .filter((item) => Boolean(item.supplierCode))
          .map((item) => ({
            label: `${item.supplierCode} - ${item.supplierName || item.supplierCode}`,
            value: item.supplierCode,
          })),
      )
      supplierOptionsLoadedRef.current = true
    } catch (error) {
      if (isAbortError(error)) {
        return
      }
      console.error(error)
      void message.error(t('storeOrders.importPriceVariance.loadSuppliersFailed'))
    } finally {
      if (supplierRequestControllerRef.current === currentController) {
        supplierRequestControllerRef.current = null
        setSupplierLoading(false)
      }
    }
  }, [message, supplierLoading, t])

  const handleSupplierOpenChange = useCallback(
    (open: boolean) => {
      if (open) {
        void loadSupplierOptions()
      }
    },
    [loadSupplierOptions],
  )

  const openOrderDetail = useCallback(
    (row: StoreOrderImportPriceVarianceDetailItem) => {
      if (!row.orderGUID) {
        return
      }

      navigate(`/warehouse/store-order/detail/${row.orderGUID}`, {
        state: { orderNo: row.orderNo },
      })
    },
    [navigate],
  )

  const openContainerDetail = useCallback(
    (row: { firstContainerCode?: string }) => {
      if (!row.firstContainerCode) {
        return
      }

      navigate(`/warehouse/container/detail/${row.firstContainerCode}`)
    },
    [navigate],
  )

  const openProductDetails = useCallback((row: StoreOrderImportPriceVarianceItem) => {
    setSelectedProduct(row)
    setDetailItems([])
    setDetailSummary(emptySummary)
    setDetailTotal(0)
    setDetailPageNumber(1)
    setDetailPageSize(DEFAULT_PAGE_SIZE)
    setDetailSortBy(DEFAULT_DETAIL_SORT_BY)
    setDetailSortDescending(DEFAULT_DETAIL_SORT_DESCENDING)
    setDetailModalOpen(true)
  }, [])

  const closeProductDetails = useCallback(() => {
    setDetailModalOpen(false)
    setSelectedProduct(null)
    setDetailItems([])
    setDetailSummary(emptySummary)
    setDetailTotal(0)
  }, [])

  const loadDetailData = useCallback(async () => {
    if (!detailModalOpen || !selectedProduct?.productCode) {
      return
    }

    setDetailLoading(true)
    try {
      const result = await getStoreOrderImportPriceVarianceDetails({
        ...filters,
        productCode: selectedProduct.productCode,
        pageNumber: detailPageNumber,
        pageSize: detailPageSize,
        sortBy: detailSortBy,
        sortDescending: detailSortDescending,
      })
      setDetailItems(result.items)
      setDetailSummary(result.summary)
      setDetailTotal(result.total)
      setDetailPageNumber(result.page)
      setDetailPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      void message.error(t('storeOrders.importPriceVariance.loadDetailsFailed'))
    } finally {
      setDetailLoading(false)
    }
  }, [
    detailModalOpen,
    detailPageNumber,
    detailPageSize,
    detailSortBy,
    detailSortDescending,
    filters,
    message,
    selectedProduct,
    t,
  ])

  useEffect(() => {
    void loadDetailData()
  }, [loadDetailData])

  const registerPriceInput = useCallback((key: string, node: InputRef | null) => {
    if (node) {
      priceInputRefs.current[key] = node
      return
    }

    delete priceInputRefs.current[key]
  }, [])

  const focusPriceInput = useCallback((row: StoreOrderImportPriceVarianceItem, field: EditablePriceField) => {
    const key = getEditablePriceInputKey(row, field)
    setEditingPriceKey(key)
    setPriceDrafts((current) => ({
      ...current,
      [key]: formatMoney(getEditablePriceValue(row, field)),
    }))

    // 进入编辑后全选文本，方便仓库同事直接覆盖当前价格。
    window.setTimeout(() => priceInputRefs.current[key]?.focus?.({ cursor: 'all' }), 0)
  }, [])

  const clearPriceDraft = useCallback((key: string) => {
    setPriceDrafts((current) => {
      const next = { ...current }
      delete next[key]
      return next
    })
  }, [])

  const cancelPriceEdit = useCallback(
    (row: StoreOrderImportPriceVarianceItem, field: EditablePriceField) => {
      const key = getEditablePriceInputKey(row, field)
      setEditingPriceKey(null)
      clearPriceDraft(key)
    },
    [clearPriceDraft],
  )

  const saveEditablePrice = useCallback(
    async (
      row: StoreOrderImportPriceVarianceItem,
      field: EditablePriceField,
      nextRow?: StoreOrderImportPriceVarianceItem,
    ) => {
      const key = getEditablePriceInputKey(row, field)
      if (savingPriceKeyRef.current === key) {
        return false
      }

      const draft = priceDrafts[key] ?? formatMoney(getEditablePriceValue(row, field))
      const nextPrice = parsePriceDraft(draft)
      if (nextPrice == null) {
        const invalidKey =
          field === 'domesticPrice'
            ? 'storeOrders.importPriceVariance.invalidDomesticPrice'
            : 'storeOrders.importPriceVariance.invalidWarehouseImportPrice'
        void message.error(t(invalidKey))
        window.setTimeout(() => priceInputRefs.current[key]?.focus?.({ cursor: 'all' }), 0)
        return false
      }

      const currentPrice = Math.round((getEditablePriceValue(row, field) ?? 0) * 100) / 100
      if (nextPrice === currentPrice) {
        setEditingPriceKey(null)
        clearPriceDraft(key)
        if (nextRow) {
          focusPriceInput(nextRow, field)
        }
        return true
      }

      const failedKey =
        field === 'domesticPrice'
          ? 'storeOrders.importPriceVariance.saveDomesticPriceFailed'
          : 'storeOrders.importPriceVariance.saveWarehouseImportPriceFailed'
      if (!row.productCode) {
        void message.error(t(failedKey))
        return false
      }

      savingPriceKeyRef.current = key
      setSavingPriceKey(key)
      try {
        let resultProductCode = ''
        let resultPrice = 0
        if (field === 'domesticPrice') {
          const result = await updateStoreOrderImportPriceVarianceDomesticPrice({
            productCode: row.productCode,
            domesticPrice: nextPrice,
          })
          resultProductCode = result.productCode
          resultPrice = result.domesticPrice
        } else {
          const result = await updateStoreOrderImportPriceVarianceWarehouseImportPrice({
            productCode: row.productCode,
            warehouseImportPrice: nextPrice,
          })
          resultProductCode = result.productCode
          resultPrice = result.warehouseImportPrice
        }

        setItems((current) =>
          current.map((item) =>
            item.productCode === resultProductCode ? { ...item, [field]: resultPrice } : item,
          ),
        )
        setSelectedProduct((current) =>
          current?.productCode === resultProductCode ? { ...current, [field]: resultPrice } : current,
        )
        setEditingPriceKey(null)
        clearPriceDraft(key)
        const successKey =
          field === 'domesticPrice'
            ? 'storeOrders.importPriceVariance.saveDomesticPriceSuccess'
            : 'storeOrders.importPriceVariance.saveWarehouseImportPriceSuccess'
        void message.success(t(successKey))
        if (nextRow) {
          focusPriceInput(nextRow, field)
        }
        return true
      } catch (error) {
        const errorMessage = error instanceof Error && error.message ? error.message : t(failedKey)
        void message.error(errorMessage)
        window.setTimeout(() => priceInputRefs.current[key]?.focus?.({ cursor: 'all' }), 0)
        return false
      } finally {
        savingPriceKeyRef.current = null
        setSavingPriceKey(null)
      }
    },
    [
      clearPriceDraft,
      focusPriceInput,
      message,
      priceDrafts,
      t,
    ],
  )

  const handlePriceKeyDown = useCallback(
    (event: KeyboardEvent<HTMLInputElement>, row: StoreOrderImportPriceVarianceItem, field: EditablePriceField) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        event.stopPropagation()
        void saveEditablePrice(row, field)
        return
      }

      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        cancelPriceEdit(row, field)
        return
      }

      const isArrowUp = event.key === 'ArrowUp'
      const isArrowDown = event.key === 'ArrowDown'
      if (!isArrowUp && !isArrowDown) {
        return
      }

      event.preventDefault()
      event.stopPropagation()
      const currentIndex = items.findIndex(
        (item) => getEditablePriceInputKey(item, field) === getEditablePriceInputKey(row, field),
      )
      const nextIndex = isArrowUp ? currentIndex - 1 : currentIndex + 1
      const nextRow = nextIndex >= 0 && nextIndex < items.length ? items[nextIndex] : undefined
      void saveEditablePrice(row, field, nextRow)
    },
    [cancelPriceEdit, items, saveEditablePrice],
  )

  const renderEditablePriceCell = useCallback(
    (value: number | undefined, row: StoreOrderImportPriceVarianceItem, field: EditablePriceField) => {
      const rowKey = getEditablePriceInputKey(row, field)
      if (editingPriceKey !== rowKey) {
        return (
          <Typography.Text
            style={{ cursor: 'text' }}
            onClick={() => focusPriceInput(row, field)}
          >
            {formatMoney(value)}
          </Typography.Text>
        )
      }

      return (
        <Input
          ref={(node) => registerPriceInput(rowKey, node)}
          size="small"
          inputMode="decimal"
          autoComplete="off"
          value={priceDrafts[rowKey] ?? formatMoney(value)}
          disabled={savingPriceKey === rowKey}
          style={{ textAlign: 'right', width: '100%' }}
          onChange={(event) =>
            setPriceDrafts((current) => ({
              ...current,
              [rowKey]: event.target.value,
            }))
          }
          onFocus={(event) => event.currentTarget.select()}
          onClick={(event) => event.currentTarget.select()}
          onBlur={() => {
            if (editingPriceKey === rowKey) {
              void saveEditablePrice(row, field)
            }
          }}
          onKeyDown={(event) => handlePriceKeyDown(event, row, field)}
        />
      )
    },
    [
      editingPriceKey,
      focusPriceInput,
      handlePriceKeyDown,
      priceDrafts,
      registerPriceInput,
      saveEditablePrice,
      savingPriceKey,
    ],
  )

  const productColumns = useMemo<ColumnsType<StoreOrderImportPriceVarianceItem>>(
    () => [
      {
        title: t('storeOrders.importPriceVariance.productImage'),
        dataIndex: 'productImage',
        key: 'productImage',
        width: 92,
        render: (value?: string) =>
          value ? (
            <Image
              src={value}
              width={48}
              height={48}
              style={{ objectFit: 'cover', borderRadius: 4, border: '1px solid #f0f0f0' }}
            />
          ) : (
            <Typography.Text type="secondary">--</Typography.Text>
          ),
      },
      {
        title: t('storeOrders.importPriceVariance.itemAndProduct'),
        dataIndex: 'productName',
        key: 'itemNumber',
        width: 260,
        sorter: true,
        render: (_value, row) => (
          <Space direction="vertical" size={0}>
            <Typography.Text strong>{row.itemNumber || row.productCode || '--'}</Typography.Text>
            <Typography.Text type="secondary">{row.productName || '--'}</Typography.Text>
          </Space>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.domesticSupplier'),
        dataIndex: 'supplierCode',
        key: 'supplierCode',
        width: 180,
        sorter: true,
        render: (_value, row) => (
          <Space direction="vertical" size={0}>
            <Typography.Text>{row.supplierName || '--'}</Typography.Text>
            <Typography.Text type="secondary">{row.supplierCode || '--'}</Typography.Text>
          </Space>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.domesticPrice'),
        dataIndex: 'domesticPrice',
        key: 'domesticPrice',
        align: 'right',
        width: 120,
        sorter: true,
        render: (value: number | undefined, row) => renderEditablePriceCell(value, row, 'domesticPrice'),
      },
      {
        title: t('storeOrders.importPriceVariance.unitVolume'),
        dataIndex: 'unitVolume',
        key: 'unitVolume',
        align: 'right',
        width: 110,
        sorter: true,
        render: (value?: number) => formatNumber(value, 4),
      },
      {
        title: t('storeOrders.importPriceVariance.packingQuantity'),
        dataIndex: 'packingQuantity',
        key: 'packingQuantity',
        align: 'right',
        width: 110,
        sorter: true,
        render: (value?: number) => formatNumber(value, 0),
      },
      {
        title: t('storeOrders.importPriceVariance.warehouseImportPrice'),
        dataIndex: 'warehouseImportPrice',
        key: 'warehouseImportPrice',
        align: 'right',
        width: 150,
        sorter: true,
        render: (value: number | undefined, row) => renderEditablePriceCell(value, row, 'warehouseImportPrice'),
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerImportPrice'),
        dataIndex: 'firstContainerImportPrice',
        key: 'firstContainerImportPrice',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.originalImportAmountTotal'),
        dataIndex: 'originalImportAmountTotal',
        key: 'originalImportAmountTotal',
        align: 'right',
        width: 140,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.baselineImportAmountTotal'),
        dataIndex: 'baselineImportAmountTotal',
        key: 'baselineImportAmountTotal',
        align: 'right',
        width: 140,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.varianceAmountTotal'),
        dataIndex: 'varianceAmountTotal',
        key: 'varianceAmountTotal',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => {
          const amount = value ?? 0
          const color = amount > 0 ? 'red' : amount < 0 ? 'green' : 'default'
          return <Tag color={color}>{formatMoney(amount)}</Tag>
        },
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerNumber'),
        dataIndex: 'firstContainerNumber',
        key: 'firstContainerNumber',
        width: 150,
        render: (_value, row) => {
          const text = row.firstContainerNumber || row.firstContainerCode || '--'
          if (!row.firstContainerCode) {
            return text
          }

          return (
            <Button type="link" size="small" onClick={() => openContainerDetail(row)}>
              {text}
            </Button>
          )
        },
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerDate'),
        dataIndex: 'firstContainerDate',
        key: 'firstContainerDate',
        width: 130,
        sorter: true,
        render: (value?: string) => formatDate(value, i18n.language),
      },
      {
        title: t('storeOrders.importPriceVariance.details'),
        dataIndex: 'detailCount',
        key: 'detailCount',
        align: 'right',
        fixed: 'right',
        width: 130,
        sorter: true,
        render: (value: number | undefined, row) => (
          <Button type="link" size="small" onClick={() => openProductDetails(row)}>
            {t('storeOrders.importPriceVariance.detailEntry', { count: value ?? 0 })}
          </Button>
        ),
      },
    ],
    [
      i18n.language,
      openContainerDetail,
      openProductDetails,
      renderEditablePriceCell,
      t,
    ],
  )

  const detailColumns = useMemo<ColumnsType<StoreOrderImportPriceVarianceDetailItem>>(
    () => [
      {
        title: t('storeOrders.importPriceVariance.orderNo'),
        dataIndex: 'orderNo',
        key: 'orderNo',
        width: 150,
        sorter: true,
        render: (value: string | undefined, row) => {
          const text = value || '--'
          if (!row.orderGUID || !value) {
            return text
          }

          return (
            <Button type="link" size="small" onClick={() => openOrderDetail(row)}>
              {text}
            </Button>
          )
        },
      },
      {
        title: t('storeOrders.importPriceVariance.orderDate'),
        dataIndex: 'orderDate',
        key: 'orderDate',
        width: 130,
        sorter: true,
        render: (value?: string) => formatDate(value, i18n.language),
      },
      {
        title: t('storeOrders.importPriceVariance.store'),
        dataIndex: 'storeName',
        key: 'storeCode',
        width: 180,
        render: (_value, row) => (
          <Space direction="vertical" size={0}>
            <Typography.Text>{row.storeName || '--'}</Typography.Text>
            <Typography.Text type="secondary">{row.storeCode || '--'}</Typography.Text>
          </Space>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.orderImportPrice'),
        dataIndex: 'orderImportPrice',
        key: 'orderImportPrice',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerImportPrice'),
        dataIndex: 'firstContainerImportPrice',
        key: 'firstContainerImportPrice',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.allocQuantity'),
        dataIndex: 'allocQuantity',
        key: 'allocQuantity',
        align: 'right',
        width: 100,
        sorter: true,
        render: (value?: number) => formatNumber(value, 2),
      },
      {
        title: t('storeOrders.importPriceVariance.originalImportAmount'),
        dataIndex: 'originalImportAmount',
        key: 'originalImportAmount',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.baselineImportAmount'),
        dataIndex: 'baselineImportAmount',
        key: 'baselineImportAmount',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.varianceAmount'),
        dataIndex: 'varianceAmount',
        key: 'varianceAmount',
        align: 'right',
        width: 130,
        sorter: true,
        render: (value?: number) => {
          const amount = value ?? 0
          const color = amount > 0 ? 'red' : amount < 0 ? 'green' : 'default'
          return <Tag color={color}>{formatMoney(amount)}</Tag>
        },
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerNumber'),
        dataIndex: 'firstContainerNumber',
        key: 'firstContainerNumber',
        width: 150,
        render: (_value, row) => {
          const text = row.firstContainerNumber || row.firstContainerCode || '--'
          if (!row.firstContainerCode) {
            return text
          }

          return (
            <Button type="link" size="small" onClick={() => openContainerDetail(row)}>
              {text}
            </Button>
          )
        },
      },
      {
        title: t('storeOrders.importPriceVariance.firstContainerDate'),
        dataIndex: 'firstContainerDate',
        key: 'firstContainerDate',
        width: 130,
        sorter: true,
        render: (value?: string) => formatDate(value, i18n.language),
      },
    ],
    [i18n.language, openContainerDetail, openOrderDetail, t],
  )

  const supplierSummaryColumns = useMemo<ColumnsType<StoreOrderImportPriceVarianceSupplierSummary>>(
    () => [
      {
        title: t('storeOrders.importPriceVariance.domesticSupplier'),
        dataIndex: 'supplierName',
        key: 'supplierName',
        width: 220,
        sorter: (left, right) =>
          compareSupplierText(left.supplierName || left.supplierCode, right.supplierName || right.supplierCode) ||
          compareSupplierText(left.supplierCode, right.supplierCode),
        render: (_value, row) => {
          const supplierName =
            row.supplierName ||
            row.supplierCode ||
            t('storeOrders.importPriceVariance.unknownSupplier')

          return (
            <Space direction="vertical" size={0}>
              <Typography.Text strong>{supplierName}</Typography.Text>
              <Typography.Text type="secondary">{row.supplierCode || '--'}</Typography.Text>
            </Space>
          )
        },
      },
      {
        title: t('storeOrders.importPriceVariance.originalImportAmountTotal'),
        dataIndex: 'originalImportAmountTotal',
        key: 'originalImportAmountTotal',
        align: 'right',
        width: 140,
        sorter: (left, right) =>
          compareSupplierNumber(left.originalImportAmountTotal, right.originalImportAmountTotal),
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.baselineImportAmountTotal'),
        dataIndex: 'baselineImportAmountTotal',
        key: 'baselineImportAmountTotal',
        align: 'right',
        width: 140,
        sorter: (left, right) =>
          compareSupplierNumber(left.baselineImportAmountTotal, right.baselineImportAmountTotal),
        render: (value?: number) => formatMoney(value),
      },
      {
        title: t('storeOrders.importPriceVariance.increaseVarianceAmountTotal'),
        dataIndex: 'increaseVarianceAmountTotal',
        key: 'increaseVarianceAmountTotal',
        align: 'right',
        width: 130,
        sorter: (left, right) =>
          compareSupplierNumber(left.increaseVarianceAmountTotal, right.increaseVarianceAmountTotal),
        render: (value?: number) => (
          <Typography.Text style={{ color: '#cf1322' }}>{formatMoney(value)}</Typography.Text>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.decreaseVarianceAmountTotal'),
        dataIndex: 'decreaseVarianceAmountTotal',
        key: 'decreaseVarianceAmountTotal',
        align: 'right',
        width: 130,
        sorter: (left, right) =>
          compareSupplierNumber(left.decreaseVarianceAmountTotal, right.decreaseVarianceAmountTotal),
        render: (value?: number) => (
          <Typography.Text style={{ color: '#389e0d' }}>{formatMoney(value)}</Typography.Text>
        ),
      },
      {
        title: t('storeOrders.importPriceVariance.varianceAmountTotal'),
        dataIndex: 'varianceAmountTotal',
        key: 'varianceAmountTotal',
        align: 'right',
        width: 130,
        sorter: (left, right) => compareSupplierNumber(left.varianceAmountTotal, right.varianceAmountTotal),
        render: (value?: number) => {
          const amount = value ?? 0
          return <Tag color={amount > 0 ? 'red' : amount < 0 ? 'green' : 'default'}>{formatMoney(amount)}</Tag>
        },
      },
      {
        title: t('storeOrders.importPriceVariance.productCount'),
        dataIndex: 'productCount',
        key: 'productCount',
        align: 'right',
        width: 100,
        sorter: (left, right) => compareSupplierNumber(left.productCount, right.productCount),
        render: (value?: number) => formatNumber(value, 0),
      },
      {
        title: t('storeOrders.importPriceVariance.detailCount'),
        dataIndex: 'detailCount',
        key: 'detailCount',
        align: 'right',
        width: 100,
        sorter: (left, right) => compareSupplierNumber(left.detailCount, right.detailCount),
        render: (value?: number) => formatNumber(value, 0),
      },
    ],
    [t],
  )

  const handleSearch = (values: FilterValues) => {
    setFilters(normalizeFilters(values))
    setPageNumber(1)
  }

  const handleReset = () => {
    form.resetFields()
    setFilters({ varianceDirection: 'all' })
    setPageNumber(1)
    setPageSize(DEFAULT_PAGE_SIZE)
    setSortBy(DEFAULT_SORT_BY)
    setSortDescending(DEFAULT_SORT_DESCENDING)
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    _filters: Record<string, unknown>,
    sorter: SorterResult<StoreOrderImportPriceVarianceItem> | SorterResult<StoreOrderImportPriceVarianceItem>[],
  ) => {
    const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
    setPageNumber(pagination.current ?? 1)
    setPageSize(pagination.pageSize ?? DEFAULT_PAGE_SIZE)

    // Antd 清空排序时回到“绝对差额倒序”，保持商品汇总首屏最关注差异最大的商品。
    if (nextSorter?.order) {
      setSortBy(String(nextSorter.columnKey ?? nextSorter.field ?? DEFAULT_SORT_BY))
      setSortDescending(nextSorter.order === 'descend')
    } else {
      setSortBy(DEFAULT_SORT_BY)
      setSortDescending(DEFAULT_SORT_DESCENDING)
    }
  }

  const handleDetailTableChange = (
    pagination: TablePaginationConfig,
    _filters: Record<string, unknown>,
    sorter:
      | SorterResult<StoreOrderImportPriceVarianceDetailItem>
      | SorterResult<StoreOrderImportPriceVarianceDetailItem>[],
  ) => {
    const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
    setDetailPageNumber(pagination.current ?? 1)
    setDetailPageSize(pagination.pageSize ?? DEFAULT_PAGE_SIZE)

    if (nextSorter?.order) {
      setDetailSortBy(String(nextSorter.columnKey ?? nextSorter.field ?? DEFAULT_DETAIL_SORT_BY))
      setDetailSortDescending(nextSorter.order === 'descend')
    } else {
      setDetailSortBy(DEFAULT_DETAIL_SORT_BY)
      setDetailSortDescending(DEFAULT_DETAIL_SORT_DESCENDING)
    }
  }

  return (
    <PageContainer title={t('storeOrders.importPriceVariance.title')}>
      <Card>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ varianceDirection: 'all' }}
          onFinish={handleSearch}
        >
          <Row gutter={16}>
            <Col xs={24} md={8} xl={5}>
              <Form.Item name="keyword" label={t('storeOrders.importPriceVariance.keyword')}>
                <Input allowClear placeholder={t('storeOrders.importPriceVariance.keywordPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} xl={4}>
              <Form.Item name="storeCode" label={t('storeOrders.importPriceVariance.storeCode')}>
                <Input allowClear placeholder={t('storeOrders.importPriceVariance.storeCodePlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} xl={5}>
              <Form.Item name="supplierCode" label={t('storeOrders.importPriceVariance.domesticSupplier')}>
                <DomesticSupplierFilterSelect
                  loading={supplierLoading}
                  options={supplierOptions}
                  placeholder={t('storeOrders.importPriceVariance.supplierPlaceholder')}
                  onOpenChange={handleSupplierOpenChange}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} xl={4}>
              <Form.Item name="orderNo" label={t('storeOrders.importPriceVariance.orderNo')}>
                <Input allowClear placeholder={t('storeOrders.importPriceVariance.orderNoPlaceholder')} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12} xl={6}>
              <Form.Item name="orderDateRange" label={t('storeOrders.importPriceVariance.orderDateRange')}>
                <RangePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12} xl={4}>
              <Form.Item name="varianceDirection" label={t('storeOrders.importPriceVariance.varianceDirection')}>
                <Select
                  options={[
                    { value: 'all', label: t('storeOrders.importPriceVariance.directionAll') },
                    { value: 'increase', label: t('storeOrders.importPriceVariance.directionIncrease') },
                    { value: 'decrease', label: t('storeOrders.importPriceVariance.directionDecrease') },
                  ]}
                />
              </Form.Item>
            </Col>
          </Row>
          <Space>
            <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
              {t('common.search')}
            </Button>
            <Button onClick={handleReset} icon={<ReloadOutlined />}>
              {t('common.reset')}
            </Button>
          </Space>
        </Form>
      </Card>

      <Row gutter={16} style={{ marginTop: 16 }}>
        <Col xs={24} md={8}>
          <Card>
            <Statistic
              title={t('storeOrders.importPriceVariance.originalImportAmountTotal')}
              value={formatMoney(summary.originalImportAmountTotal)}
            />
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card>
            <Statistic
              title={t('storeOrders.importPriceVariance.baselineImportAmountTotal')}
              value={formatMoney(summary.baselineImportAmountTotal)}
            />
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card>
            <Statistic
              title={t('storeOrders.importPriceVariance.varianceAmountTotal')}
              value={formatMoney(summary.varianceAmountTotal)}
              valueStyle={{
                color:
                  summary.varianceAmountTotal > 0
                    ? '#cf1322'
                    : summary.varianceAmountTotal < 0
                      ? '#389e0d'
                      : undefined,
              }}
            />
          </Card>
        </Col>
      </Row>

      <Card
        title={t('storeOrders.importPriceVariance.supplierVarianceRankingTitle')}
        style={{
          marginTop: 16,
          maxHeight: 'calc(100vh - 32px)',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
        styles={{
          body: {
            flex: 1,
            minHeight: 0,
            overflow: 'hidden',
          },
        }}
      >
        <div ref={supplierSummaryRegionRef} style={{ height: '100%', minHeight: 0, overflow: 'hidden' }}>
          <Table<StoreOrderImportPriceVarianceSupplierSummary>
            rowKey={getSupplierSummaryRowKey}
            loading={loading}
            columns={supplierSummaryColumns}
            dataSource={supplierSummaries}
            size="small"
            scroll={{ x: 1120, y: supplierSummaryTableScrollY }}
            locale={{
              emptyText: <Empty description={t('storeOrders.importPriceVariance.noSupplierVarianceData')} />,
            }}
            pagination={{
              defaultPageSize: 50,
              pageSizeOptions: [20, 50, 100],
              showSizeChanger: true,
              showTotal: (value) => t('storeOrders.importPriceVariance.totalSuppliers', { total: value }),
            }}
          />
        </div>
      </Card>

      <Card
        style={{
          marginTop: 16,
          height: 'calc(100vh - 32px)',
          minHeight: 0,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
        styles={{
          body: {
            flex: 1,
            minHeight: 0,
            overflow: 'hidden',
            display: 'flex',
            flexDirection: 'column',
          },
        }}
      >
        <div ref={tableRegionRef} style={{ flex: 1, minHeight: 0, overflow: 'hidden' }}>
          <Table<StoreOrderImportPriceVarianceItem>
            rowKey={getRowKey}
            loading={loading}
            columns={productColumns}
            dataSource={items}
            scroll={{ x: 2000, y: tableScrollY }}
            onChange={handleTableChange}
            pagination={{
              current: pageNumber,
              pageSize,
              total,
              showSizeChanger: true,
              showTotal: (value) => t('storeOrders.importPriceVariance.totalRows', { total: value }),
            }}
          />
        </div>
      </Card>

      <Modal
        open={detailModalOpen}
        title={t('storeOrders.importPriceVariance.detailModalTitle', {
          item: selectedProduct?.itemNumber || selectedProduct?.productCode || '--',
        })}
        width={1280}
        footer={null}
        destroyOnClose
        onCancel={closeProductDetails}
      >
        <Row gutter={16} style={{ marginBottom: 16 }}>
          <Col xs={24} md={8}>
            <Statistic
              title={t('storeOrders.importPriceVariance.originalImportAmountTotal')}
              value={formatMoney(detailSummary.originalImportAmountTotal)}
            />
          </Col>
          <Col xs={24} md={8}>
            <Statistic
              title={t('storeOrders.importPriceVariance.baselineImportAmountTotal')}
              value={formatMoney(detailSummary.baselineImportAmountTotal)}
            />
          </Col>
          <Col xs={24} md={8}>
            <Statistic
              title={t('storeOrders.importPriceVariance.varianceAmountTotal')}
              value={formatMoney(detailSummary.varianceAmountTotal)}
              valueStyle={{
                color:
                  detailSummary.varianceAmountTotal > 0
                    ? '#cf1322'
                    : detailSummary.varianceAmountTotal < 0
                      ? '#389e0d'
                      : undefined,
              }}
            />
          </Col>
        </Row>
        <Table<StoreOrderImportPriceVarianceDetailItem>
          rowKey={getDetailRowKey}
          loading={detailLoading}
          columns={detailColumns}
          dataSource={detailItems}
          scroll={{ x: 1450 }}
          onChange={handleDetailTableChange}
          pagination={{
            current: detailPageNumber,
            pageSize: detailPageSize,
            total: detailTotal,
            showSizeChanger: true,
            showTotal: (value) => t('storeOrders.importPriceVariance.totalRows', { total: value }),
          }}
        />
      </Modal>
    </PageContainer>
  )
}
