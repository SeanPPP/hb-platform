import {
  DeleteOutlined,
  DownloadOutlined,
  DollarOutlined,
  FileExcelOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Checkbox,
  Image,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Progress,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd'
import type { FilterDropdownProps, FilterValue, SorterResult, TablePaginationConfig } from 'antd/es/table/interface'
import type { ColumnsType } from 'antd/es/table'
import type { Key } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { exportProductGradesToExcel } from '../../../services/exportService'
import {
  batchUpdateGrades,
  createOrUpdateProductGrade,
  deleteProductGrade,
  getGradesByProductCodes,
  getProductGradeList,
} from '../../../services/productGradeService'
import { PRODUCT_GRADE_CONFIG, type ProductGradeListItem } from '../../../types/productGrade'
import BatchPriceModal from './BatchPriceModal'
import PasteImportModal from './PasteImportModal'

const GRADE_TAG_COLOR: Record<string, string> = {
  A: 'purple',
  B: 'blue',
  C: 'orange',
  D: 'red',
}

interface SupplierOption {
  label: string
  value: string
}

type ProductGradeSortOrder = 'ascend' | 'descend' | null

interface ProductGradeColumnFilters {
  supplierCode?: string
  grade?: string
  hbProductNo?: string
  domesticPriceMin?: number
  domesticPriceMax?: number
  importPriceMin?: number
  importPriceMax?: number
  oemPriceMin?: number
  oemPriceMax?: number
}

interface LoadListOptions {
  filters?: ProductGradeColumnFilters
  sortField?: string
  sortOrder?: ProductGradeSortOrder
}

function formatPrice(value?: number, prefix = '¥') {
  if (value === undefined || value === null) return '--'
  return `${prefix}${value.toFixed(2)}`
}

function encodePriceRange(min?: number, max?: number) {
  if (min === undefined && max === undefined) return undefined
  return `${min ?? ''}|${max ?? ''}`
}

function parsePriceRange(value?: Key | boolean): { min?: number; max?: number } {
  if (typeof value !== 'string') return {}
  const [minText, maxText] = value.split('|')
  const min = minText === '' ? undefined : Number(minText)
  const max = maxText === '' ? undefined : Number(maxText)
  return {
    min: Number.isFinite(min) ? min : undefined,
    max: Number.isFinite(max) ? max : undefined,
  }
}

function getSingleFilterValue(value?: FilterValue | null) {
  const first = value?.[0]
  return first == null || first === '' ? undefined : String(first)
}

function normalizeFilterNumber(value: string | number | null) {
  if (value === null || value === '') return undefined
  const numberValue = Number(value)
  return Number.isFinite(numberValue) ? numberValue : undefined
}

export default function ProductGradeManagementPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<ProductGradeListItem[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [total, setTotal] = useState(0)
  const [search, setSearch] = useState('')
  const [columnFilters, setColumnFilters] = useState<ProductGradeColumnFilters>({})
  const [sortField, setSortField] = useState<string | undefined>(undefined)
  const [sortOrder, setSortOrder] = useState<ProductGradeSortOrder>(null)
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([])
  const [supplierLoading, setSupplierLoading] = useState(false)
  const [suppliersLoaded, setSuppliersLoaded] = useState(false)
  const [selectedRowKeys, setSelectedRowKeys] = useState<string[]>([])
  const [batchGrade, setBatchGrade] = useState<string | undefined>(undefined)
  const [pasteImportOpen, setPasteImportOpen] = useState(false)
  const [batchPriceOpen, setBatchPriceOpen] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const [exportIncludeImage, setExportIncludeImage] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [exportMessage, setExportMessage] = useState('')
  const listAbortRef = useRef<AbortController | null>(null)
  const listRequestSeqRef = useRef(0)
  const supplierAbortRef = useRef<AbortController | null>(null)
  const gradeFilterOptions = [
    { label: t('productGrade.allGrades'), value: '' },
    ...Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => ({
      label: t(`productGrade.${cfg.i18nKey}`),
      value: key,
    })),
  ]

  const loadSuppliers = useCallback(async () => {
    if (suppliersLoaded || supplierLoading) {
      return
    }

    supplierAbortRef.current?.abort()
    const controller = new AbortController()
    supplierAbortRef.current = controller
    setSupplierLoading(true)
    try {
      const result = await getActiveChinaSuppliers(controller.signal)
      setSuppliers(
        result.map((item) => ({
          label: `${item.supplierCode} - ${item.supplierName}`,
          value: item.supplierCode,
        })),
      )
      setSuppliersLoaded(true)
    } catch (error) {
      if (controller.signal.aborted) return
      console.error(error)
      message.error(t('productGrade.loadSuppliersFailed'))
    } finally {
      if (supplierAbortRef.current === controller) {
        supplierAbortRef.current = null
        setSupplierLoading(false)
      }
    }
  }, [supplierLoading, suppliersLoaded, t])

  const loadList = useCallback(async (
    nextPage = page,
    nextPageSize = pageSize,
    options: LoadListOptions = {},
  ) => {
    const activeFilters = options.filters ?? columnFilters
    const activeSortField = options.sortField ?? sortField
    const activeSortOrder = options.sortOrder ?? sortOrder
    const requestSeq = listRequestSeqRef.current + 1
    listRequestSeqRef.current = requestSeq
    listAbortRef.current?.abort()
    const controller = new AbortController()
    listAbortRef.current = controller
    setLoading(true)
    try {
      const result = await getProductGradeList({
        page: nextPage,
        pageSize: nextPageSize,
        search: search || undefined,
        grade: activeFilters.grade || undefined,
        supplierCode: activeFilters.supplierCode,
        hbProductNo: activeFilters.hbProductNo,
        domesticPriceMin: activeFilters.domesticPriceMin,
        domesticPriceMax: activeFilters.domesticPriceMax,
        importPriceMin: activeFilters.importPriceMin,
        importPriceMax: activeFilters.importPriceMax,
        oemPriceMin: activeFilters.oemPriceMin,
        oemPriceMax: activeFilters.oemPriceMax,
        sortField: activeSortField,
        sortDirection: activeSortOrder === 'ascend' ? 'asc' : activeSortOrder === 'descend' ? 'desc' : undefined,
        signal: controller.signal,
      })
      if (requestSeq !== listRequestSeqRef.current) {
        return
      }
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setColumnFilters(activeFilters)
      setSortField(activeSortField)
      setSortOrder(activeSortOrder)
    } catch (error) {
      if (controller.signal.aborted) return
      console.error(error)
      message.error(t('productGrade.loadListFailed'))
    } finally {
      if (requestSeq === listRequestSeqRef.current) {
        setLoading(false)
      }
      if (listAbortRef.current === controller) {
        listAbortRef.current = null
      }
    }
  }, [columnFilters, page, pageSize, search, sortField, sortOrder, t])

  useEffect(() => {
    void loadList(1, pageSize)
    return () => {
      listAbortRef.current?.abort()
      supplierAbortRef.current?.abort()
    }
  }, [])

  const handleDelete = useCallback(async (id: string) => {
    try {
      await deleteProductGrade(id)
      message.success(t('common.deleteSuccess'))
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('common.deleteFailed'))
    }
  }, [loadList, page, pageSize, t])

  const handleBatchUpdate = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    if (!batchGrade) {
      message.warning(t('productGrade.selectTargetGrade'))
      return
    }
    try {
      await batchUpdateGrades({
        items: selectedRowKeys.map((productCode) => ({
          productCode,
          grade: batchGrade,
        })),
      })
      message.success(t('productGrade.batchUpdateSuccess', { count: selectedRowKeys.length }))
      setSelectedRowKeys([])
      setBatchGrade(undefined)
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.batchUpdateFailed'))
    }
  }

  const handleInlineGradeChange = useCallback(async (productCode: string, newGrade: string) => {
    try {
      await createOrUpdateProductGrade({ productCode, grade: newGrade })
      message.success(t('productGrade.updateSuccess'))
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('productGrade.updateFailed'))
    }
  }, [loadList, page, pageSize, t])

  const openExportModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }
    setExportIncludeImage(true)
    setExportProgress(0)
    setExportMessage('')
    setExportOpen(true)
  }

  const handleExportExcel = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('productGrade.selectProductsFirst'))
      return
    }

    try {
      setExporting(true)
      setExportProgress(0)
      setExportMessage(t('productGrade.exportPreparing'))

      const selectedProductCodes = selectedRowKeys.map(String)
      // 选中项可能来自跨页选择，导出前按商品编码重新拉完整字段，避免只导出当前页残缺数据。
      const exportRows = await getGradesByProductCodes(selectedProductCodes)
      const rowOrder = new Map(selectedProductCodes.map((code, index) => [code, index]))
      const orderedRows = exportRows
        .filter((item) => rowOrder.has(item.productCode))
        .sort((a, b) => (rowOrder.get(a.productCode) ?? 0) - (rowOrder.get(b.productCode) ?? 0))

      if (!orderedRows.length) {
        message.warning(t('productGrade.noDataToExport'))
        return
      }

      const result = await exportProductGradesToExcel(orderedRows, {
        includeProductImage: exportIncludeImage,
        fileName: t('productGrade.exportFileName'),
        onProgress: (progress, nextMessage) => {
          setExportProgress(progress)
          setExportMessage(nextMessage)
        },
      })

      if (result.failedProductImages.length) {
        message.warning(t('productGrade.exportImageFailed', { count: result.failedProductImages.length }))
      } else {
        message.success(t('productGrade.exportSuccess'))
      }
      setExportOpen(false)
      setExportProgress(0)
      setExportMessage('')
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productGrade.exportFailed'))
    } finally {
      setExporting(false)
    }
  }

  const getFiltersFromTable = (filters: Record<string, FilterValue | null>): ProductGradeColumnFilters => {
    const domesticRange = parsePriceRange(filters.domesticPrice?.[0])
    const importRange = parsePriceRange(filters.importPrice?.[0])
    const oemRange = parsePriceRange(filters.oemPrice?.[0])

    return {
      supplierCode: getSingleFilterValue(filters.supplierCode),
      grade: getSingleFilterValue(filters.grade),
      hbProductNo: getSingleFilterValue(filters.hbProductNo),
      domesticPriceMin: domesticRange.min,
      domesticPriceMax: domesticRange.max,
      importPriceMin: importRange.min,
      importPriceMax: importRange.max,
      oemPriceMin: oemRange.min,
      oemPriceMax: oemRange.max,
    }
  }

  const handleTableChange = (
    pagination: TablePaginationConfig,
    filters: Record<string, FilterValue | null>,
    sorter: SorterResult<ProductGradeListItem> | SorterResult<ProductGradeListItem>[],
    extra: { action: 'paginate' | 'sort' | 'filter' },
  ) => {
    const currentSorter = Array.isArray(sorter) ? sorter[0] : sorter
    const rawField = currentSorter?.field || currentSorter?.column?.dataIndex
    const field = Array.isArray(rawField) ? rawField.join('.') : rawField ? String(rawField) : undefined
    const order = currentSorter?.order as ProductGradeSortOrder | undefined
    const nextSortField = field && order ? field : undefined
    const nextSortOrder = field && order ? order : null
    const nextFilters = getFiltersFromTable(filters)
    // 列头排序/过滤都走服务端，变化时回到第一页，避免只处理当前页数据。
    const nextPage = extra.action === 'paginate' ? pagination.current ?? 1 : 1

    void loadList(nextPage, pagination.pageSize ?? pageSize, {
      filters: nextFilters,
      sortField: nextSortField,
      sortOrder: nextSortOrder,
    })
  }

  const renderSupplierFilterDropdown = ({
    selectedKeys,
    setSelectedKeys,
    confirm,
    clearFilters,
  }: FilterDropdownProps) => (
    <Space direction="vertical" style={{ padding: 8, width: 240 }}>
      <Select
        showSearch
        allowClear
        placeholder={t('productGrade.filterSupplier')}
        value={selectedKeys[0] as string | undefined}
        options={suppliers}
        loading={supplierLoading}
        optionFilterProp="label"
        style={{ width: '100%' }}
        onDropdownVisibleChange={(open) => {
          if (open) void loadSuppliers()
        }}
        onChange={(value) => setSelectedKeys(value ? [value] : [])}
      />
      <Space>
        <Button type="primary" size="small" onClick={() => confirm()}>
          {t('common.query')}
        </Button>
        <Button
          size="small"
          onClick={() => {
            clearFilters?.()
            confirm()
          }}
        >
          {t('common.reset', '重置')}
        </Button>
      </Space>
    </Space>
  )

  const renderTextFilterDropdown = ({
    selectedKeys,
    setSelectedKeys,
    confirm,
    clearFilters,
  }: FilterDropdownProps) => (
    <Space direction="vertical" style={{ padding: 8 }}>
      <Input
        autoFocus
        allowClear
        placeholder={t('productGrade.itemNumberFilterPlaceholder', '输入货号')}
        value={selectedKeys[0] as string | undefined}
        onChange={(event) => setSelectedKeys(event.target.value ? [event.target.value] : [])}
        onPressEnter={() => confirm()}
      />
      <Space>
        <Button type="primary" size="small" onClick={() => confirm()}>
          {t('common.query')}
        </Button>
        <Button
          size="small"
          onClick={() => {
            clearFilters?.()
            confirm()
          }}
        >
          {t('common.reset', '重置')}
        </Button>
      </Space>
    </Space>
  )

  const renderPriceFilterDropdown = (
    minValue: number | undefined,
    maxValue: number | undefined,
  ) => ({
    selectedKeys,
    setSelectedKeys,
    confirm,
    clearFilters,
  }: FilterDropdownProps) => {
    const selectedRangeValue = selectedKeys[0]
    const hasSelectedRange = typeof selectedRangeValue === 'string'
    const range = parsePriceRange(selectedRangeValue)
    const min = hasSelectedRange ? range.min : minValue
    const max = hasSelectedRange ? range.max : maxValue

    const updateRange = (nextMin?: number, nextMax?: number) => {
      const encoded = encodePriceRange(nextMin, nextMax)
      setSelectedKeys(encoded ? [encoded] : [])
    }

    return (
      <Space direction="vertical" style={{ padding: 8, width: 220 }}>
        <InputNumber
          placeholder={t('common.min', '最小值')}
          value={min}
          min={0}
          precision={2}
          style={{ width: '100%' }}
          onChange={(value) => updateRange(normalizeFilterNumber(value), max)}
        />
        <InputNumber
          placeholder={t('common.max', '最大值')}
          value={max}
          min={0}
          precision={2}
          style={{ width: '100%' }}
          onChange={(value) => updateRange(min, normalizeFilterNumber(value))}
        />
        <Space>
          <Button type="primary" size="small" onClick={() => confirm()}>
            {t('common.query')}
          </Button>
          <Button
            size="small"
            onClick={() => {
              clearFilters?.()
              confirm()
            }}
          >
            {t('common.reset', '重置')}
          </Button>
        </Space>
      </Space>
    )
  }

  const columns = useMemo<ColumnsType<ProductGradeListItem>>(
    () => [
      {
        title: t('column.index'),
        width: 50,
        render: (_v, _r, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: t('column.supplier'),
        dataIndex: 'supplierName',
        width: 150,
        sorter: true,
        sortOrder: sortField === 'supplierName' ? sortOrder : null,
        render: (_, record) => record.supplierName || record.supplierCode || '--',
      },
      {
        title: t('column.supplierCode'),
        dataIndex: 'supplierCode',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'supplierCode' ? sortOrder : null,
        filterDropdown: renderSupplierFilterDropdown,
        filteredValue: columnFilters.supplierCode ? [columnFilters.supplierCode] : null,
        render: (value?: string) => value || '--',
      },
      {
        title: t('column.itemNumber'),
        dataIndex: 'hbProductNo',
        width: 140,
        sorter: true,
        sortOrder: sortField === 'hbProductNo' ? sortOrder : null,
        filterDropdown: renderTextFilterDropdown,
        filteredValue: columnFilters.hbProductNo ? [columnFilters.hbProductNo] : null,
        render: (value?: string) => value || '--',
      },
      {
        title: t('column.image'),
        dataIndex: 'productImage',
        width: 80,
        render: (value: string | undefined, record) =>
          value ? (
            <Image
              src={value}
              alt={record.productName || record.hbProductNo || record.productCode}
              width={48}
              height={48}
              loading="lazy"
              decoding="async"
              style={{ objectFit: 'contain' }}
              preview={{ mask: '' }}
              fallback="data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNDgiIGhlaWdodD0iNDgiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjQ4IiBoZWlnaHQ9IjQ4IiBmaWxsPSIjZjBmMGYwIi8+PHRleHQgeD0iMjQiIHk9IjI4IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmb250LXNpemU9IjEyIiBmaWxsPSIjY2NjIj7ml6DnvKnnlaXimLQ8L3RleHQ+PC9zdmc+"
            />
          ) : (
            '--'
          ),
      },
      {
        title: t('column.levelLabel'),
        dataIndex: 'grade',
        width: 100,
        sorter: true,
        sortOrder: sortField === 'grade' ? sortOrder : null,
        filters: Object.keys(PRODUCT_GRADE_CONFIG).map((key) => ({ text: key, value: key })),
        filteredValue: columnFilters.grade ? [columnFilters.grade] : null,
        render: (grade: string, record) => (
          <Select
            value={grade}
            size="small"
            style={{ width: 80 }}
            onChange={(value) => void handleInlineGradeChange(record.productCode, value)}
          >
            {Object.keys(PRODUCT_GRADE_CONFIG).map((key) => (
              <Select.Option key={key} value={key}>
                <Tag color={GRADE_TAG_COLOR[key]} style={{ marginRight: 0 }}>
                  {key}
                </Tag>
              </Select.Option>
            ))}
          </Select>
        ),
      },
      {
        title: t('productGrade.domesticPriceRmb'),
        dataIndex: 'domesticPrice',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'domesticPrice' ? sortOrder : null,
        filterDropdown: renderPriceFilterDropdown(
          columnFilters.domesticPriceMin,
          columnFilters.domesticPriceMax,
        ),
        filteredValue: encodePriceRange(columnFilters.domesticPriceMin, columnFilters.domesticPriceMax)
          ? [encodePriceRange(columnFilters.domesticPriceMin, columnFilters.domesticPriceMax)!]
          : null,
        render: (value?: number) => formatPrice(value),
      },
      {
        title: t('productGrade.importPriceAud'),
        dataIndex: 'importPrice',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'importPrice' ? sortOrder : null,
        filterDropdown: renderPriceFilterDropdown(
          columnFilters.importPriceMin,
          columnFilters.importPriceMax,
        ),
        filteredValue: encodePriceRange(columnFilters.importPriceMin, columnFilters.importPriceMax)
          ? [encodePriceRange(columnFilters.importPriceMin, columnFilters.importPriceMax)!]
          : null,
        render: (value?: number) => formatPrice(value, 'A$'),
      },
      {
        title: t('productGrade.retailPriceAud'),
        dataIndex: 'oemPrice',
        width: 110,
        sorter: true,
        sortOrder: sortField === 'oemPrice' ? sortOrder : null,
        filterDropdown: renderPriceFilterDropdown(
          columnFilters.oemPriceMin,
          columnFilters.oemPriceMax,
        ),
        filteredValue: encodePriceRange(columnFilters.oemPriceMin, columnFilters.oemPriceMax)
          ? [encodePriceRange(columnFilters.oemPriceMin, columnFilters.oemPriceMax)!]
          : null,
        render: (value?: number) => formatPrice(value, 'A$'),
      },
      {
        title: t('column.action'),
        key: 'action',
        width: 80,
        fixed: 'right',
        render: (_, record) => (
          <Popconfirm
            title={t('productGrade.confirmDelete')}
            description={t('productGrade.deleteGradeHint')}
            onConfirm={() => void handleDelete(record.id)}
          >
            <Tooltip title={t('productGrade.deleteGrade')}>
              <Button type="link" danger icon={<DeleteOutlined />} size="small" />
            </Tooltip>
          </Popconfirm>
        ),
      },
    ],
    [columnFilters, handleDelete, handleInlineGradeChange, page, pageSize, sortField, sortOrder, supplierLoading, suppliers, t],
  )

  return (
    <PageContainer title={t('productGrade.title')} subtitle={t('productGrade.subtitle')}>
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            showSearch
            allowClear
            placeholder={t('productGrade.filterSupplier')}
            value={columnFilters.supplierCode}
            onDropdownVisibleChange={(open) => {
              if (open) void loadSuppliers()
            }}
            onChange={(value) => {
              setColumnFilters((current) => ({ ...current, supplierCode: value }))
            }}
            options={suppliers}
            loading={supplierLoading}
            style={{ width: 220 }}
            optionFilterProp="label"
          />
          <Select
            value={columnFilters.grade ?? ''}
            onChange={(value) => {
              setColumnFilters((current) => ({ ...current, grade: value || undefined }))
            }}
            options={gradeFilterOptions}
            style={{ width: 180 }}
          />
          <Input
            placeholder={t('productGrade.searchPlaceholder')}
            prefix={<SearchOutlined />}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            allowClear
            style={{ width: 260 }}
          />
          <Button type="primary" onClick={() => void loadList(1, pageSize)}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadList(page, pageSize)}>
            {t('common.refresh')}
          </Button>
          <Button
            type="dashed"
            icon={<FileExcelOutlined />}
            onClick={() => setPasteImportOpen(true)}
          >
            {t('productGrade.pasteImport')}
          </Button>
          <Button
            icon={<DollarOutlined />}
            disabled={selectedRowKeys.length === 0}
            onClick={() => setBatchPriceOpen(true)}
          >
            {t('productGrade.batchPrice')}
          </Button>
          <Button
            icon={<DownloadOutlined />}
            disabled={selectedRowKeys.length === 0}
            onClick={openExportModal}
          >
            {t('productGrade.exportExcel')}
          </Button>
        </Space>

        {selectedRowKeys.length > 0 && (
          <Card size="small" style={{ marginBottom: 12, background: '#fafafa' }}>
            <Space>
              <span>{t('productGrade.selectedProducts', { count: selectedRowKeys.length })}</span>
              <Select
                placeholder={t('productGrade.selectTargetGrade')}
                value={batchGrade}
                onChange={setBatchGrade}
                style={{ width: 160 }}
                allowClear
              >
                {Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => (
                  <Select.Option key={key} value={key}>
                    <Tag color={GRADE_TAG_COLOR[key]} style={{ marginRight: 4 }}>
                      {key}
                    </Tag>
                    {t(`productGrade.${cfg.i18nKey}`)}
                  </Select.Option>
                ))}
              </Select>
              <Button type="primary" size="small" onClick={() => void handleBatchUpdate()}>
                {t('productGrade.batchModify')}
              </Button>
              <Button size="small" onClick={() => setSelectedRowKeys([])}>
                {t('productGrade.cancelSelection')}
              </Button>
            </Space>
          </Card>
        )}

        <Table<ProductGradeListItem>
          rowKey="productCode"
          virtual
          loading={loading}
          columns={columns}
          dataSource={data}
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys as string[]),
            columnWidth: 48,
            fixed: true,
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            pageSizeOptions: [20, 50, 100, 200, 500, 1000],
            showQuickJumper: true,
            showTotal: (total) => t('common.total', { count: total }),
          }}
          scroll={{ x: 800, y: 600 }}
          onChange={handleTableChange}
        />
      </Card>

      <PasteImportModal
        open={pasteImportOpen}
        onClose={() => setPasteImportOpen(false)}
        onSuccess={() => void loadList(page, pageSize)}
      />

      <BatchPriceModal
        open={batchPriceOpen}
        selectedCount={selectedRowKeys.length}
        productCodes={selectedRowKeys}
        onClose={() => setBatchPriceOpen(false)}
        onSuccess={() => {
          setSelectedRowKeys([])
          setBatchGrade(undefined)
        }}
      />

      <Modal
        title={t('productGrade.exportExcelTitle')}
        open={exportOpen}
        onCancel={() => {
          if (!exporting) setExportOpen(false)
        }}
        onOk={() => void handleExportExcel()}
        okText={t('productGrade.startExport')}
        cancelText={t('common.cancel')}
        confirmLoading={exporting}
        maskClosable={!exporting}
      >
        <Space direction="vertical" style={{ width: '100%' }} size={12}>
          <span>{t('productGrade.exportSelectedProducts', { count: selectedRowKeys.length })}</span>
          <Checkbox
            checked={exportIncludeImage}
            disabled={exporting}
            onChange={(event) => setExportIncludeImage(event.target.checked)}
          >
            {t('productGrade.includeProductImage')}
          </Checkbox>
          {exporting && (
            <div>
              <Progress percent={exportProgress} size="small" />
              <div style={{ color: '#666', marginTop: 4 }}>{exportMessage}</div>
            </div>
          )}
        </Space>
      </Modal>
    </PageContainer>
  )
}
