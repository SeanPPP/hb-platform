import {
  Button,
  Card,
  Checkbox,
  DatePicker,
  Form,
  Image,
  Input,
  InputNumber,
  message,
  Modal,
  Progress,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tooltip,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import {
  batchUpdateStoreRetailPrices,
  getStorePriceTransferJob,
  getStoreProductPriceGrid,
  startStorePriceTransferJob,
  syncFromHq,
  syncToOtherStores,
} from '../../../services/storeProductPriceService'
import {
  createHqSyncJobPoller,
  HqProductSyncPollingCancelledError,
  HqProductSyncPollingTimeoutError,
} from '../../../services/productHqSyncPolling'
import { getActiveStores } from '../../../services/storeService'
import type {
  BatchUpdateStoreRetailPriceDto,
  CopyProgressDto,
  StoreProductPriceListDto,
  StoreProductPriceQueryDto,
  StorePriceTransferJobDto,
  StorePriceTransferRequest,
  StorePriceTransferResult,
  SyncFromHqRequest,
  SyncToOtherStoresDto,
} from '../../../types/storeProductPrice'
import { CheckSquareOutlined, CopyOutlined, SwapOutlined } from '@ant-design/icons'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { discountRateToDecimal, formatDiscountRate } from '../../../utils/discountRate'
import { useAuthStore } from '../../../store/auth'
import { formatPaginationTotalText } from './pagination'

type DataType = StoreProductPriceListDto & { key: string }
const PRICE_TRANSFER_POLL_TIMEOUT_MS = 45 * 60 * 1000

const productTypeMap: Record<number, { labelKey: string; color: string }> = {
  0: { labelKey: 'posAdmin.productPrice.normalProduct', color: 'default' },
  1: { labelKey: 'posAdmin.productPrice.weighProduct', color: 'blue' },
  2: { labelKey: 'posAdmin.productPrice.multiCodeProductType', color: 'purple' },
}

function isFormValidationError(error: unknown): error is { errorFields: unknown[] } {
  return (
    typeof error === 'object' &&
    error !== null &&
    Array.isArray((error as { errorFields?: unknown }).errorFields)
  )
}

function getPriceTransferErrors(job: StorePriceTransferJobDto) {
  return Array.from(new Set([...(job.errors ?? []), ...(job.result?.errors ?? [])]))
}

function getPriceTransferHandledCount(result?: StorePriceTransferResult) {
  return result ? result.totalProcessed + result.skippedCount : 0
}

function getPriceTransferProgressPercent(job: StorePriceTransferJobDto) {
  if (job.status === 'Succeeded' || job.status === 'Failed') return 100
  const totalCount = job.result?.totalCount ?? 0
  if (totalCount <= 0) return 0
  const percent = Math.floor((getPriceTransferHandledCount(job.result) / totalCount) * 100)
  return Math.max(0, Math.min(99, percent))
}

export default function StoreProductPricePage() {
  const { t } = useTranslation()
  const currentUser = useAuthStore((s) => s.currentUser)
  const access = useAuthStore((s) => s.access)
  const [searchForm] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<DataType[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [sortField, setSortField] = useState<string | undefined>()
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend' | undefined>()
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])
  const [selectedStoreCode, setSelectedStoreCode] = useState<string | undefined>()

  const [batchModalOpen, setBatchModalOpen] = useState(false)
  const [batchForm] = Form.useForm()

  const [syncModalOpen, setSyncModalOpen] = useState(false)
  const [syncForm] = Form.useForm()

  const [copyModalOpen, setCopyModalOpen] = useState(false)
  const [copyForm] = Form.useForm()
  const [copyProgress, setCopyProgress] = useState<CopyProgressDto | null>(null)
  const [copying, setCopying] = useState(false)
  const eventSourceRef = useRef<EventSource | null>(null)

  const [hqSyncModalOpen, setHqSyncModalOpen] = useState(false)
  const [hqSyncForm] = Form.useForm()
  const [hqSyncing, setHqSyncing] = useState(false)

  const [priceTransferModalOpen, setPriceTransferModalOpen] = useState(false)
  const [priceTransferForm] = Form.useForm()
  const [priceTransferSubmitting, setPriceTransferSubmitting] = useState(false)
  const [priceTransferJob, setPriceTransferJob] = useState<StorePriceTransferJobDto | null>(null)
  const priceTransferPollerRef = useRef<{ stop: () => void } | null>(null)

  const inFlightRef = useRef(false)

  const stopPriceTransferPolling = useCallback(() => {
    priceTransferPollerRef.current?.stop()
    priceTransferPollerRef.current = null
  }, [])

  const loadData = useCallback(async () => {
    if (!selectedStoreCode) {
      setData([])
      setTotal(0)
      return
    }
    if (inFlightRef.current) return
    inFlightRef.current = true
    try {
      setLoading(true)
      const values = searchForm.getFieldsValue()
      const query: StoreProductPriceQueryDto = {
        storeCode: selectedStoreCode,
        search: values.search || undefined,
        localSupplierCode: values.localSupplierCode || undefined,
        pageNumber: page,
        pageSize,
        sortBy: sortField,
        sortOrder: sortOrder === 'ascend' ? 'asc' : sortOrder === 'descend' ? 'desc' : undefined,
      }
      const result = await getStoreProductPriceGrid(query)
      const items = result?.items ?? []
      setTotal(result?.total ?? 0)
      setData(items.map((item, idx) => ({ ...item, key: item.productCode ?? String(idx) })))
      setSelectedRowKeys([])
    } catch {
      message.error(t('posAdmin.productPrice.loadFailed', '加载商品价格列表失败'))
    } finally {
      setLoading(false)
      inFlightRef.current = false
    }
  }, [selectedStoreCode, page, pageSize, sortField, sortOrder, searchForm])

  useEffect(() => {
    ;(async () => {
      try {
        const managedCodes = access.managedStoreCodes()
        if (managedCodes === null) {
          const stores = await getActiveStores()
          setStoreOptions(stores)
        } else if (currentUser?.stores?.length) {
          const visible = currentUser.stores
            .filter((s) => managedCodes.includes(s.storeCode))
            .map((s) => ({ label: s.storeName || s.storeCode, value: s.storeCode }))
          setStoreOptions(visible)
        }
      } catch { /* ignore */ }
      try {
        const suppliers = await getActiveLocalSuppliers()
        setSupplierOptions(
          suppliers.map((s) => ({ label: s.name || s.localSupplierCode, value: s.localSupplierCode })),
        )
      } catch { /* ignore */ }
    })()
  }, [currentUser, access])

  useEffect(() => {
    if (storeOptions.length === 1 && selectedStoreCode !== storeOptions[0].value) {
      setSelectedStoreCode(storeOptions[0].value)
      searchForm.setFieldsValue({ storeCode: storeOptions[0].value })
      setPage(1)
      setSelectedRowKeys([])
    } else if (selectedStoreCode && !storeOptions.some((s) => s.value === selectedStoreCode)) {
      setSelectedStoreCode(undefined)
      searchForm.setFieldsValue({ storeCode: undefined })
      setData([])
      setTotal(0)
    }
  }, [storeOptions, selectedStoreCode, searchForm])

  useEffect(() => {
    loadData()
  }, [loadData])

  useEffect(() => () => {
    stopPriceTransferPolling()
  }, [stopPriceTransferPolling])

  const onTableChange = (pagination: any, _filters: any, sorter: any) => {
    if (sorter?.field) {
      setSortField(String(sorter.field))
      setSortOrder(sorter.order)
    } else {
      setSortField(undefined)
      setSortOrder(undefined)
    }
    setPage(pagination.current)
    setPageSize(pagination.pageSize)
  }

  const handleStoreChange = (val: string | undefined) => {
    setSelectedStoreCode(val)
    setPage(1)
    setSelectedRowKeys([])
  }

  const handleSearch = () => {
    setPage(1)
    loadData()
  }

  const handleReset = () => {
    searchForm.resetFields()
    setPage(1)
    loadData()
  }

  const openBatchModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('posAdmin.productPrice.selectProducts', '请先选择商品'))
      return
    }
    batchForm.resetFields()
    batchForm.setFieldsValue({
      updatePurchasePrice: false,
      updateRetailPrice: false,
      updateAutoPricing: false,
      updateSpecialProduct: false,
      updateDiscountRate: false,
    })
    setBatchModalOpen(true)
  }

  const handleBatchUpdate = async () => {
    try {
      const values = await batchForm.validateFields()
      if (!values.updatePurchasePrice && !values.updateRetailPrice && !values.updateAutoPricing && !values.updateSpecialProduct && !values.updateDiscountRate) {
        message.warning(t('posAdmin.productPrice.selectUpdateField', '请至少选择一项要更新的字段'))
        return
      }
      const dto: BatchUpdateStoreRetailPriceDto = {
        productCodes: selectedRowKeys as string[],
        storeCode: selectedStoreCode,
      }
      if (values.updatePurchasePrice && values.purchasePrice != null) {
        dto.purchasePrice = Number(values.purchasePrice)
      }
      if (values.updateRetailPrice && values.storeRetailPriceValue != null) {
        dto.storeRetailPriceValue = Number(values.storeRetailPriceValue)
      }
      if (values.updateAutoPricing) {
        dto.isAutoPricing = !!values.isAutoPricing
      }
      if (values.updateSpecialProduct) {
        dto.isSpecialProduct = !!values.isSpecialProduct
      }
      if (values.updateDiscountRate && values.discountRate != null) {
        dto.discountRate = discountRateToDecimal(Number(values.discountRate))
      }
      await batchUpdateStoreRetailPrices(dto)
      message.success(t('posAdmin.productPrice.batchUpdateSuccess', '批量更新成功'))
      setBatchModalOpen(false)
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error(t('posAdmin.productPrice.batchUpdateFailed', '批量更新失败'))
    }
  }

  const openSyncModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning(t('posAdmin.productPrice.selectProducts', '请先选择商品'))
      return
    }
    syncForm.resetFields()
    syncForm.setFieldsValue({
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false,
      syncDiscountRate: false,
      syncMode: 'Overwrite',
    })
    setSyncModalOpen(true)
  }

  const handleSyncToOtherStores = async () => {
    try {
      const values = await syncForm.validateFields()
      if (!values.targetStoreCodes || values.targetStoreCodes.length === 0) {
        message.warning(t('posAdmin.productPrice.selectTargetStore', '请选择目标分店'))
        return
      }
      const dto: SyncToOtherStoresDto = {
        productCodes: selectedRowKeys as string[],
        sourceStoreCode: selectedStoreCode!,
        targetStoreCodes: values.targetStoreCodes,
        syncPurchasePrice: !!values.syncPurchasePrice,
        syncRetailPrice: !!values.syncRetailPrice,
        syncIsAutoPricing: !!values.syncIsAutoPricing,
        syncIsSpecialProduct: !!values.syncIsSpecialProduct,
        syncDiscountRate: !!values.syncDiscountRate,
        syncMode: values.syncMode || 'Overwrite',
      }
      const count = await syncToOtherStores(dto)
      message.success(t('posAdmin.productPrice.syncComplete', '同步完成，影响 {{count}} 条记录', { count }))
      setSyncModalOpen(false)
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error(t('posAdmin.productPrice.syncFailed', '同步失败'))
    }
  }

  const openCopyModal = () => {
    copyForm.resetFields()
    copyForm.setFieldsValue({
      mode: 'Overwrite',
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false,
      syncDiscountRate: false,
      syncMultiCode: false,
      syncMultiCodeRetailPrice: false,
    })
    setCopyProgress(null)
    setCopyModalOpen(true)
  }

  const openHqSyncModal = () => {
    hqSyncForm.resetFields()
    setHqSyncModalOpen(true)
  }

  const selectAllHqSyncStores = () => {
    hqSyncForm.setFieldValue('selectedStoreCodes', storeOptions.map((option) => option.value))
  }

  const handleSyncFromHq = async () => {
    try {
      const values = await hqSyncForm.validateFields()
      setHqSyncing(true)
      const dto: SyncFromHqRequest = {
        selectedStoreCodes: values.selectedStoreCodes,
        startDate: values.dateRange[0].format('YYYY-MM-DD'),
        endDate: values.dateRange[1].format('YYYY-MM-DD'),
      }
      const result = await syncFromHq(dto)
      setHqSyncModalOpen(false)
      Modal.info({
        title: t('posAdmin.productPrice.hqSyncResult', 'HQ同步结果'),
        width: 600,
        content: (
          <div>
            <p>{t('posAdmin.productPrice.added', '新增')}：{result.addedCount} {t('posAdmin.productPrice.recordsUnit', '条')}</p>
            <p>{t('posAdmin.productPrice.updated', '更新')}：{result.updatedCount} {t('posAdmin.productPrice.recordsUnit', '条')}</p>
            <p>{t('posAdmin.productPrice.totalProcessed', '总处理')}：{result.totalProcessed} {t('posAdmin.productPrice.recordsUnit', '条')}</p>
            <p>{t('posAdmin.productPrice.duration', '耗时')}：{(result.durationMs / 1000).toFixed(2)} {t('posAdmin.productPrice.seconds', '秒')}</p>
            {result.errors && result.errors.length > 0 && (
              <div>
                <p style={{ color: 'red' }}>{t('posAdmin.productPrice.errorInfo', '错误信息')}：</p>
                <ul>
                  {result.errors.map((err, idx) => (
                    <li key={idx} style={{ color: 'red' }}>{err}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        ),
      })
      await loadData()
    } catch (error) {
      // 表单校验错误由字段自身展示，避免误提示为后端同步失败。
      if (isFormValidationError(error)) return
      message.error(error instanceof Error ? error.message : t('posAdmin.productPrice.hqSyncFailed', '从HQ同步失败'))
    } finally {
      setHqSyncing(false)
    }
  }

  const openPriceTransferModal = () => {
    stopPriceTransferPolling()
    priceTransferForm.resetFields()
    priceTransferForm.setFieldsValue({
      direction: 'HqToLocal',
      sourceStoreCode: undefined,
      targetStoreCode: selectedStoreCode,
      syncRetailPrices: true,
      syncMultiCodePrices: true,
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncDiscountRate: false,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false,
    })
    setPriceTransferJob(null)
    setPriceTransferSubmitting(false)
    setPriceTransferModalOpen(true)
  }

  const handlePriceTransferCancel = () => {
    stopPriceTransferPolling()
    setPriceTransferSubmitting(false)
    setPriceTransferModalOpen(false)
  }

  const handleStorePriceTransfer = async () => {
    if (priceTransferSubmitting) return
    let activePoller: { stop: () => void } | null = null

    try {
      const values = await priceTransferForm.validateFields()
      const hasSelectedTable = !!values.syncRetailPrices || !!values.syncMultiCodePrices
      const hasSelectedField = !!values.syncPurchasePrice || !!values.syncRetailPrice || !!values.syncDiscountRate || !!values.syncIsAutoPricing || !!values.syncIsSpecialProduct

      if (!hasSelectedTable) {
        message.warning(t('posAdmin.productPrice.selectSyncTable', '请至少选择一个同步表'))
        return
      }

      if (!hasSelectedField) {
        message.warning(t('posAdmin.productPrice.selectSyncField', '请至少选择一个同步字段'))
        return
      }

      const dto: StorePriceTransferRequest = {
        direction: values.direction,
        sourceStoreCode: values.sourceStoreCode,
        targetStoreCode: values.targetStoreCode,
        syncRetailPrices: !!values.syncRetailPrices,
        syncMultiCodePrices: !!values.syncMultiCodePrices,
        syncPurchasePrice: !!values.syncPurchasePrice,
        syncRetailPrice: !!values.syncRetailPrice,
        syncDiscountRate: !!values.syncDiscountRate,
        syncIsAutoPricing: !!values.syncIsAutoPricing,
        syncIsSpecialProduct: !!values.syncIsSpecialProduct,
      }

      setPriceTransferSubmitting(true)
      const job = await startStorePriceTransferJob(dto)
      setPriceTransferJob(job)
      if (job.isDuplicateRequest) {
        message.info(t('posAdmin.productPrice.priceTransferDuplicate', '目标分店同步任务正在执行，已切换到已有任务'))
      }

      const poller = createHqSyncJobPoller<StorePriceTransferJobDto>({
        jobId: job.jobId,
        getJob: async (jobId) => {
          const nextJob = await getStorePriceTransferJob(jobId)
          setPriceTransferJob(nextJob)
          return nextJob
        },
        timeoutMs: PRICE_TRANSFER_POLL_TIMEOUT_MS,
      })
      activePoller = poller
      priceTransferPollerRef.current = poller
      const completedJob = await poller.promise
      setPriceTransferJob(completedJob)

      if (completedJob.status === 'Failed') {
        const errorMessage = completedJob.message || completedJob.errors?.[0] || t('posAdmin.productPrice.priceTransferFailed', '分店价格同步失败')
        message.error(errorMessage)
        return
      }

      const totalProcessed = getPriceTransferHandledCount(completedJob.result)
      message.success(t('posAdmin.productPrice.priceTransferComplete', '分店价格同步完成，处理 {{count}} 条', { count: totalProcessed }))
      if (dto.targetStoreCode === selectedStoreCode) {
        await loadData()
      }
    } catch (error) {
      if (isFormValidationError(error)) return
      if (error instanceof HqProductSyncPollingCancelledError) return
      if (error instanceof HqProductSyncPollingTimeoutError) {
        message.error(t('posAdmin.productPrice.priceTransferTimeout', '分店价格同步任务轮询超时，任务可能仍在后台执行，请稍后刷新或重新查询'))
        return
      }
      message.error(error instanceof Error ? error.message : t('posAdmin.productPrice.priceTransferFailed', '分店价格同步失败'))
    } finally {
      if (priceTransferPollerRef.current === activePoller) {
        priceTransferPollerRef.current = null
      }
      setPriceTransferSubmitting(false)
    }
  }

  const columns: ColumnsType<DataType> = useMemo(() => [
    {
      title: t('posAdmin.productPrice.rowIndex', '序号'),
      key: 'rowIndex',
      width: 42,
      align: 'right',
      render: (_: unknown, __: DataType, index: number) => index + 1,
    },
    {
      title: t('posAdmin.productPrice.productImage', '商品图片'),
      dataIndex: 'productImage',
      key: 'productImage',
      width: 50,
      align: 'center',
      render: (url: string) =>
        url ? (
          <Image className="store-product-price-image-cell" src={url} width={34} height={34} style={{ objectFit: 'cover', borderRadius: 4 }} />
        ) : (
          <div className="store-product-price-image-cell">
            {t('posAdmin.productPrice.noImage', '无图')}
          </div>
        ),
    },
    {
      title: t('posAdmin.productPrice.itemNumber', '货号'),
      dataIndex: 'itemNumber',
      key: 'itemNumber',
      width: 96,
      sorter: true,
      render: (v: string) => <span className="store-product-price-code-cell">{v || '-'}</span>,
    },
    {
      title: t('posAdmin.productPrice.productCode', '商品代码'),
      dataIndex: 'productCode',
      key: 'productCode',
      width: 48,
      fixed: 'left',
      sorter: true,
      render: (v: string) => (
        <Tooltip title={v}>
          <Button type="text" size="small" icon={<CopyOutlined />} onClick={() => void copyTextToClipboard(v)} />
        </Tooltip>
      ),
    },
    {
      title: t('posAdmin.productPrice.productName', '商品名称'),
      dataIndex: 'productName',
      key: 'productName',
      width: 180,
      sorter: true,
      render: (v: string) => (
        <div
          className="store-product-price-name-cell"
          title={v}
        >
          {v}
        </div>
      ),
    },
    
    {
      title: t('posAdmin.productPrice.barcode', '条码'),
      dataIndex: 'barcode',
      key: 'barcode',
      width: 132,
      render: (v: string) => (
        <div className="store-product-price-barcode-cell">
          <BarcodePreview
            value={v}
            compactCopy
            align="left"
            className="store-product-price-barcode-preview"
            gap={2}
            options={{ height: 22, width: 1, margin: 0 }}
            textMaxWidth={104}
            textNoWrap
          />
        </div>
      ),
    },
    {
      title: t('posAdmin.productPrice.supplier', '供应商'),
      dataIndex: 'localSupplierName',
      key: 'localSupplierName',
      width: 110,
      render: (v: string) => <div className="store-product-price-supplier-cell" title={v}>{v || '-'}</div>,
    },
    {
      title: t('posAdmin.productPrice.productType', '商品类型'),
      dataIndex: 'productType',
      key: 'productType',
      width: 76,
      align: 'center',
      render: (v: number) => {
        const info = productTypeMap[v] || { labelKey: String(v), color: 'default' }
        return <Tag color={info.color}>{t(info.labelKey)}</Tag>
      },
    },
   
    {
      title: t('posAdmin.productPrice.purchasePrice', '采购价'),
      dataIndex: 'storePurchasePrice',
      key: 'storePurchasePrice',
      width: 72,
      align: 'right',
      sorter: true,
      render: (v: number) => <span className="store-product-price-numeric-cell">{v != null ? v.toFixed(2) : '-'}</span>,
    },
    {
      title: t('posAdmin.productPrice.retailPrice', '零售价'),
      dataIndex: 'storeRetailPrice',
      key: 'storeRetailPrice',
      width: 72,
      align: 'right',
      sorter: true,
      render: (v: number) => <span className="store-product-price-numeric-cell">{v != null ? v.toFixed(2) : '-'}</span>,
    },
    {
      title: t('posAdmin.productPrice.autoPricing', '自动定价'),
      dataIndex: 'isStoreAutoPricing',
      key: 'isStoreAutoPricing',
      width: 70,
      align: 'center',
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? t('posAdmin.invoiceDetail.yes', '是') : t('posAdmin.invoiceDetail.no', '否')}</Tag>,
    },
    {
      title: t('posAdmin.productPrice.specialProduct', '特殊商品'),
      dataIndex: 'isStoreSpecialProduct',
      key: 'isStoreSpecialProduct',
      width: 70,
      align: 'center',
      render: (v: boolean) => <Tag color={v ? 'orange' : 'default'}>{v ? t('posAdmin.invoiceDetail.yes', '是') : t('posAdmin.invoiceDetail.no', '否')}</Tag>,
    },
    {
      title: t('posAdmin.productPrice.discountRate', '折扣率'),
      dataIndex: 'discountRate',
      key: 'discountRate',
      width: 72,
      align: 'right',
      sorter: true,
      render: (v: number) => <span className="store-product-price-numeric-cell">{formatDiscountRate(v)}</span>,
    },
    {
      title: t('posAdmin.productPrice.enabled', '启用'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 56,
      align: 'center',
      render: (v: boolean) => <Tag color={v ? 'success' : 'error'}>{v ? t('posAdmin.invoiceDetail.yes', '是') : t('posAdmin.invoiceDetail.no', '否')}</Tag>,
    },
    {
      title: t('posAdmin.productPrice.updatedAt', '更新时间'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 92,
      sorter: true,
      render: (v: string) => v ? (
        <span className="store-product-price-date-cell">
          <span>{dayjs(v).format('YYYY-MM-DD')}</span>
          <span>{dayjs(v).format('HH:mm')}</span>
        </span>
      ) : '-',
    },
    {
      title: t('posAdmin.productPrice.updatedBy', '更新人'),
      dataIndex: 'updatedBy',
      key: 'updatedBy',
      width: 80,
      render: (v: string) => <span className="store-product-price-code-cell">{v || '-'}</span>,
    },
  ], [t])

  const selectedCount = selectedRowKeys.length
  const priceTransferErrors = priceTransferJob ? getPriceTransferErrors(priceTransferJob) : []

  return (
    <PageContainer
      title={t('posAdmin.productPrice.title', '分店商品价格管理')}
      subtitle={t('posAdmin.productPrice.subtitle', '管理各分店的商品采购价、零售价、自动定价及折扣率')}
    >
      <Card>
        <Form form={searchForm} layout="inline" style={{ marginBottom: 16 }} onFinish={handleSearch}>
          <Form.Item name="storeCode" label={t('posAdmin.productPrice.store', '分店')}>
            <Select
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder={t('posAdmin.productPrice.selectStoreFirst', '请选择分店')}
              style={{ width: 260 }}
              allowClear
              onChange={handleStoreChange}
            />
          </Form.Item>
          <Form.Item name="localSupplierCode" label={t('posAdmin.productPrice.supplier', '供应商')}>
            <Select
              showSearch
              optionFilterProp="label"
              options={supplierOptions}
              placeholder={t('posAdmin.productPrice.allSuppliers', '全部供应商')}
              style={{ width: 220 }}
              allowClear
            />
          </Form.Item>
          <Form.Item name="search" label={t('common.query', '查询')}>
            <Input allowClear placeholder={t('posAdmin.productPrice.searchPlaceholder', '商品代码/名称/货号/条码')} style={{ width: 240 }} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" disabled={!selectedStoreCode}>
                {t('common.query', '查询')}
              </Button>
              <Button onClick={handleReset}>{t('common.reset', '重置')}</Button>
            </Space>
          </Form.Item>
        </Form>

        <div style={{ marginBottom: 12 }}>
          <Space wrap>
            {access.canEditStoreProducts && (
              <>
                <Button type="primary" disabled={selectedCount === 0} onClick={openBatchModal}>
                  {t('posAdmin.productPrice.batchUpdate', '批量更新')} {selectedCount > 0 ? `(${selectedCount})` : ''}
                </Button>
                <Button disabled={selectedCount === 0} onClick={openSyncModal}>
                  {t('posAdmin.productPrice.syncToStores', '同步到其他分店')} {selectedCount > 0 ? `(${selectedCount})` : ''}
                </Button>
              </>
            )}
            {access.isAdmin && (
              <Button onClick={openCopyModal}>{t('posAdmin.productPrice.copyStoreData', '复制分店数据')}</Button>
            )}
            {access.isAdmin && (
              <Button icon={<SwapOutlined />} onClick={openPriceTransferModal}>
                {t('posAdmin.productPrice.priceTransfer', 'HQ/本地价格同步')}
              </Button>
            )}
            {access.isAdmin && (
              <Button onClick={openHqSyncModal}>{t('posAdmin.productPrice.updateFromHQ', '从HQ更新零售价')}</Button>
            )}
          </Space>
        </div>

        <Table
          className="store-product-price-compact-table"
          rowKey="key"
          loading={loading}
          dataSource={data}
          columns={columns}
          scroll={{ x: 1520, y: 600 }}
          virtual
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys),
            columnWidth: 40,
          }}
          pagination={{
            total,
            current: page,
            pageSize,
            showSizeChanger: true,
            pageSizeOptions: ['10', '20', '50', '100', '200'],
            showTotal: (value) => formatPaginationTotalText(value, pageSize, t),
          }}
          onChange={onTableChange}
          size="small"
        />
      </Card>

      <Modal
        open={batchModalOpen}
        title={t('posAdmin.productPrice.batchUpdateTitle', '批量更新价格')}
        onCancel={() => setBatchModalOpen(false)}
        onOk={handleBatchUpdate}
        width={600}
        forceRender
      >
        <p style={{ marginBottom: 16, color: '#666' }}>
          {t('posAdmin.productPrice.selectedProductsStore', '已选择 {{count}} 个商品，分店：{{storeCode}}', { count: selectedCount, storeCode: selectedStoreCode })}
        </p>
        <Form form={batchForm} layout="vertical">
          <Form.Item name="updatePurchasePrice" valuePropName="checked" label={t('posAdmin.productPrice.updatePurchasePriceLabel', '更新采购价')}>
            <Checkbox>{t('posAdmin.productPrice.enableUpdate', '启用')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updatePurchasePrice !== cur.updatePurchasePrice}>
            {({ getFieldValue }) =>
              getFieldValue('updatePurchasePrice') ? (
                <Form.Item name="purchasePrice" label={t('posAdmin.productPrice.purchasePrice', '采购价')} rules={[{ required: true, type: 'number', min: 0 }]}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateRetailPrice" valuePropName="checked" label={t('posAdmin.productPrice.updateRetailPriceLabel', '更新零售价')}>
            <Checkbox>{t('posAdmin.productPrice.enableUpdate', '启用')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateRetailPrice !== cur.updateRetailPrice}>
            {({ getFieldValue }) =>
              getFieldValue('updateRetailPrice') ? (
                <Form.Item name="storeRetailPriceValue" label={t('posAdmin.productPrice.retailPrice', '零售价')} rules={[{ required: true, type: 'number', min: 0 }]}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateAutoPricing" valuePropName="checked" label={t('posAdmin.productPrice.updateAutoPricingLabel', '更新自动定价')}>
            <Checkbox>{t('posAdmin.productPrice.enableUpdate', '启用')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateAutoPricing !== cur.updateAutoPricing}>
            {({ getFieldValue }) =>
              getFieldValue('updateAutoPricing') ? (
                <Form.Item name="isAutoPricing" label={t('posAdmin.productPrice.autoPricing', '自动定价')} valuePropName="checked">
                  <Switch checkedChildren={t('posAdmin.invoiceDetail.yes', '是')} unCheckedChildren={t('posAdmin.invoiceDetail.no', '否')} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateSpecialProduct" valuePropName="checked" label={t('posAdmin.productPrice.updateSpecialLabel', '更新特殊商品')}>
            <Checkbox>{t('posAdmin.productPrice.enableUpdate', '启用')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateSpecialProduct !== cur.updateSpecialProduct}>
            {({ getFieldValue }) =>
              getFieldValue('updateSpecialProduct') ? (
                <Form.Item name="isSpecialProduct" label={t('posAdmin.productPrice.specialProduct', '特殊商品')} valuePropName="checked">
                  <Switch checkedChildren={t('posAdmin.invoiceDetail.yes', '是')} unCheckedChildren={t('posAdmin.invoiceDetail.no', '否')} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateDiscountRate" valuePropName="checked" label={t('posAdmin.productPrice.updateDiscountLabel', '更新折扣率')}>
            <Checkbox>{t('posAdmin.productPrice.enableUpdate', '启用')}</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateDiscountRate !== cur.updateDiscountRate}>
            {({ getFieldValue }) =>
              getFieldValue('updateDiscountRate') ? (
                <Form.Item name="discountRate" label={t('posAdmin.productPrice.discountRate', '折扣率')} rules={[{ required: true, type: 'number', min: 0, max: 100 }]}>
                  <InputNumber min={0} max={100} step={1} precision={1} addonAfter="%" style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={syncModalOpen}
        title={t('posAdmin.productPrice.syncToStoresTitle', '同步到其他分店')}
        onCancel={() => setSyncModalOpen(false)}
        onOk={handleSyncToOtherStores}
        width={650}
        forceRender
      >
        <p style={{ marginBottom: 16, color: '#666' }}>
          {t('posAdmin.productPrice.selectedSourceStore', '已选择 {{count}} 个商品，源分店：{{storeCode}}', { count: selectedCount, storeCode: selectedStoreCode })}
        </p>
        <Form form={syncForm} layout="vertical">
          <Form.Item name="targetStoreCodes" label={t('posAdmin.productPrice.targetStore', '目标分店')} rules={[{ required: true }]}>
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              options={storeOptions.filter((s) => s.value !== selectedStoreCode)}
              placeholder={t('posAdmin.productPrice.selectTargetStore', '请选择目标分店')}
            />
          </Form.Item>
          <Form.Item name="syncMode" label={t('posAdmin.productPrice.syncMode', '同步模式')} rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'Overwrite', label: t('posAdmin.productPrice.overwrite', '覆盖') },
                { value: 'OnlyUpdateNull', label: t('posAdmin.productPrice.onlyUpdateNull', '仅更新空值') },
              ]}
            />
          </Form.Item>
          <Form.Item label={t('posAdmin.productPrice.syncFields', '同步字段')}>
            <Space wrap>
              <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.purchasePrice', '采购价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.retailPrice', '零售价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.autoPricing', '自动定价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.specialProduct', '特殊商品')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncDiscountRate" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.discountRate', '折扣率')}</Checkbox>
              </Form.Item>
            </Space>
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={copyModalOpen}
        title={t('posAdmin.productPrice.copyTitle', '复制分店数据')}
        onCancel={() => {
          if (eventSourceRef.current) {
            eventSourceRef.current.close()
            eventSourceRef.current = null
          }
          setCopying(false)
          setCopyModalOpen(false)
        }}
        footer={copying ? null : undefined}
        width={650}
        forceRender
      >
        <Form form={copyForm} layout="vertical">
          <Form.Item name="sourceStoreCode" label={t('posAdmin.productPrice.sourceStore', '源分店')} rules={[{ required: true }]}>
            <Select showSearch optionFilterProp="label" options={storeOptions} placeholder={t('posAdmin.productPrice.selectSourceStore', '请选择源分店')} />
          </Form.Item>
          <Form.Item name="targetStoreCodes" label={t('posAdmin.productPrice.targetStore', '目标分店')} rules={[{ required: true }]}>
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder={t('posAdmin.productPrice.selectTargetStore', '请选择目标分店')}
            />
          </Form.Item>
          <Form.Item name="mode" label={t('posAdmin.productPrice.copyMode', '复制模式')} rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'Overwrite', label: t('posAdmin.productPrice.overwrite', '覆盖') },
                { value: 'OnlyUpdateNull', label: t('posAdmin.productPrice.onlyUpdateNull', '仅更新空值') },
              ]}
            />
          </Form.Item>
          <Form.Item label={t('posAdmin.productPrice.syncFields', '同步字段')}>
            <Space wrap>
              <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.purchasePrice', '采购价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.retailPrice', '零售价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.autoPricing', '自动定价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.specialProduct', '特殊商品')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncDiscountRate" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.discountRate', '折扣率')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncMultiCode" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.multiCodeProduct', '多码商品')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncMultiCodeRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.multiCodeRetailPrice', '多码零售价')}</Checkbox>
              </Form.Item>
            </Space>
          </Form.Item>
        </Form>

        {copying && copyProgress && (
          <Card size="small" style={{ marginTop: 16 }}>
            <Space direction="vertical" style={{ width: '100%' }}>
              <div>
                <Spin size="small" /> {copyProgress.message}
              </div>
              {copyProgress.totalStores > 0 && (
                <Progress
                  percent={Math.round((copyProgress.storeIndex / copyProgress.totalStores) * 100)}
                  status="active"
                />
              )}
              <div style={{ color: '#666', fontSize: 12 }}>
                {t('posAdmin.productPrice.copyProgress', '零售价已复制：{{retailCount}} | 多码已复制：{{multiCodeCount}}', { retailCount: copyProgress.retailPriceCopied, multiCodeCount: copyProgress.multiCodeCopied })}
              </div>
            </Space>
          </Card>
        )}
      </Modal>

      <Modal
        open={priceTransferModalOpen}
        title={t('posAdmin.productPrice.priceTransferTitle', 'HQ/本地价格同步')}
        onCancel={handlePriceTransferCancel}
        onOk={handleStorePriceTransfer}
        width={650}
        confirmLoading={priceTransferSubmitting}
        forceRender
      >
        <Form form={priceTransferForm} layout="vertical" disabled={priceTransferSubmitting}>
          <Form.Item name="direction" label={t('posAdmin.productPrice.transferDirection', '同步方向')} rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'HqToLocal', label: t('posAdmin.productPrice.hqToLocal', 'HQ -> 本地') },
                { value: 'LocalToHq', label: t('posAdmin.productPrice.localToHq', '本地 -> HQ') },
              ]}
            />
          </Form.Item>
          <Form.Item name="sourceStoreCode" label={t('posAdmin.productPrice.sourceStore', '源分店')} rules={[{ required: true, message: t('posAdmin.productPrice.selectSourceStore', '请选择源分店') }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder={t('posAdmin.productPrice.selectSourceStore', '请选择源分店')}
            />
          </Form.Item>
          <Form.Item name="targetStoreCode" label={t('posAdmin.productPrice.targetStore', '目标分店')} rules={[{ required: true, message: t('posAdmin.productPrice.selectTargetStore', '请选择目标分店') }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder={t('posAdmin.productPrice.selectTargetStore', '请选择目标分店')}
            />
          </Form.Item>
          <Form.Item label={t('posAdmin.productPrice.syncTables', '同步表')}>
            <Space wrap>
              <Form.Item name="syncRetailPrices" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.storeRetailPriceTable', '分店零售价表')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncMultiCodePrices" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.storeMultiCodePriceTable', '分店多码表')}</Checkbox>
              </Form.Item>
            </Space>
          </Form.Item>
          <Form.Item label={t('posAdmin.productPrice.syncFields', '同步字段')}>
            <Space wrap>
              <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.purchasePrice', '采购价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.retailPrice', '零售价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncDiscountRate" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.discountRate', '折扣率')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.autoPricing', '自动定价')}</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
                <Checkbox>{t('posAdmin.productPrice.specialProduct', '特殊商品')}</Checkbox>
              </Form.Item>
            </Space>
          </Form.Item>
        </Form>

        {priceTransferJob && (
          <Card size="small" style={{ marginTop: 16 }}>
            <Space direction="vertical" style={{ width: '100%' }}>
              <Space>
                <Tag color={priceTransferJob.status === 'Succeeded' ? 'success' : priceTransferJob.status === 'Failed' ? 'error' : 'processing'}>
                  {priceTransferJob.status}
                </Tag>
                <span>{priceTransferJob.message || t('posAdmin.productPrice.priceTransferRunning', '分店价格同步任务处理中')}</span>
              </Space>
              <Progress
                percent={getPriceTransferProgressPercent(priceTransferJob)}
                status={priceTransferJob.status === 'Failed' ? 'exception' : priceTransferJob.status === 'Succeeded' ? 'success' : 'active'}
              />
              {priceTransferJob.result && (
                <div style={{ color: '#666', fontSize: 12 }}>
                  <div>
                    {t('posAdmin.productPrice.processedProgress', '已处理')}：
                    {getPriceTransferHandledCount(priceTransferJob.result)}
                    {priceTransferJob.result.totalCount > 0 ? ` / ${priceTransferJob.result.totalCount}` : ''}，
                    {t('posAdmin.productPrice.added', '新增')}：{priceTransferJob.result.insertedCount}，
                    {t('posAdmin.productPrice.updated', '更新')}：{priceTransferJob.result.updatedCount}，
                    {t('posAdmin.productPrice.skipped', '跳过')}：{priceTransferJob.result.skippedCount}
                  </div>
                  <div>
                    {t('posAdmin.productPrice.storeRetailPriceTable', '分店零售价表')}：
                    {priceTransferJob.result.retailPriceInserted}/{priceTransferJob.result.retailPriceUpdated}/{priceTransferJob.result.retailPriceSkipped}
                  </div>
                  <div>
                    {t('posAdmin.productPrice.storeMultiCodePriceTable', '分店多码表')}：
                    {priceTransferJob.result.multiCodeInserted}/{priceTransferJob.result.multiCodeUpdated}/{priceTransferJob.result.multiCodeSkipped}
                  </div>
                </div>
              )}
              {priceTransferErrors.length > 0 ? (
                <div style={{ color: '#cf1322', fontSize: 12 }}>
                  {priceTransferErrors.map((error, index) => (
                    <div key={`${error}-${index}`}>{error}</div>
                  ))}
                </div>
              ) : null}
            </Space>
          </Card>
        )}
      </Modal>

      <Modal
        open={hqSyncModalOpen}
        title={t('posAdmin.productPrice.hqSyncTitle', '从HQ更新零售价')}
        onCancel={() => setHqSyncModalOpen(false)}
        onOk={handleSyncFromHq}
        width={550}
        confirmLoading={hqSyncing}
        forceRender
      >
        <Form form={hqSyncForm} layout="vertical">
          <Form.Item label={t('posAdmin.productPrice.storeOptional', '分店')} required>
            <Space.Compact style={{ width: '100%' }}>
              {/* selectedStoreCodes 必须直接绑定 Select，否则全选写入表单后多选框不会回显。 */}
              <Form.Item name="selectedStoreCodes" noStyle rules={[{ required: true, message: t('posAdmin.productPrice.selectStoreRequired', '请选择分店') }]}>
                <Select
                  mode="multiple"
                  showSearch
                  optionFilterProp="label"
                  options={storeOptions}
                  placeholder={t('posAdmin.productPrice.syncAllStores', '请选择分店')}
                  allowClear
                  style={{ flex: 1 }}
                />
              </Form.Item>
              <Button htmlType="button" icon={<CheckSquareOutlined />} onClick={selectAllHqSyncStores}>
                {t('posAdmin.productPrice.selectAllStores', '全选分店')}
              </Button>
            </Space.Compact>
          </Form.Item>
          <Form.Item name="dateRange" label={t('posAdmin.productPrice.syncDateRange', '同步日期范围')} rules={[{ required: true, message: t('posAdmin.productPrice.selectDateRangeRequired', '请选择日期范围') }]}>
            <DatePicker.RangePicker style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
