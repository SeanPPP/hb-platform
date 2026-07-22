import { CloudSyncOutlined, CopyOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  Modal,
  Pagination,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  message,
} from 'antd'
import type { ColumnsType, TableRef } from 'antd/es/table'
import dayjs from 'dayjs'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../../store/auth'
import { RequestError } from '../../../utils/request'
import { createLatestRequestGuard, runLatestGuardedRequest } from '../../../utils/latestRequestGuard'
import { getStableTagColor } from '../../../utils/tagColors'
import {
  checkInvoiceNoExists,
  createInvoice,
  deleteInvoice,
  getInvoiceGrid,
  syncInvoicesFromHq,
} from '../../../services/localSupplierInvoiceService'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import { getActiveStores } from '../../../services/storeService'
import type {
  LocalSupplierInvoiceHqSyncRequest,
  LocalSupplierInvoiceHqSyncResult,
  LocalSupplierInvoiceListDto,
} from '../../../types/localSupplierInvoice'
import { copyTextToClipboard } from '../../../utils/clipboard'
import {
  buildStoreOptionsFromUserStores,
  buildScopedStoreCodeFilter,
  filterStoreOptionsByManagedCodes,
  isStoreCodeInManagedScope,
  shouldSkipScopedStoreQuery,
} from '../../../utils/managedStoreScope'
import ImportInvoiceModal from './ImportInvoiceModal'
import {
  getNextInvoiceTableScrollTop,
  scheduleInvoiceTableScrollRestore,
  shouldRestoreInvoiceTableScroll,
} from './invoiceTableScroll'

const SORT_FIELD_MAP: Record<string, string> = {
  storeName: 'storeName',
  supplierName: 'supplierName',
  invoiceNo: 'invoiceNo',
  orderDate: 'orderDate',
  inboundDate: 'inboundDate',
  totalAmount: 'totalAmount',
  receivedTotalAmount: 'receivedTotalAmount',
  flowStatus: 'flowStatus',
  inboundStatus: 'inboundStatus',
  createdAt: 'createdAt',
  createdBy: 'createdBy',
  updatedAt: 'updatedAt',
  updatedBy: 'updatedBy',
}

const FLOW_STATUS_MAP: Record<number, { labelKey: string; color: string }> = {
  0: { labelKey: 'posAdmin.invoices.draft', color: 'default' },
  1: { labelKey: 'posAdmin.invoices.submitted', color: 'blue' },
  2: { labelKey: 'posAdmin.invoices.approved', color: 'green' },
  3: { labelKey: 'posAdmin.invoices.pushed', color: 'purple' },
}

const INBOUND_STATUS_MAP: Record<number, { labelKey: string; color: string }> = {
  0: { labelKey: 'posAdmin.invoices.notInbound', color: 'default' },
  1: { labelKey: 'posAdmin.invoices.partialInbound', color: 'orange' },
  2: { labelKey: 'posAdmin.invoices.inbounded', color: 'green' },
}

function formatDateTime(value?: string) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', { hour12: false })
}

function formatDate(value?: string) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleDateString('zh-CN')
}

function formatAmount(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
}

function getHqSyncResultFromError(error: unknown) {
  if (!(error instanceof RequestError)) return undefined
  const payload = error.payload as { data?: unknown; details?: unknown } | undefined
  return (payload?.data ?? payload?.details) as LocalSupplierInvoiceHqSyncResult | undefined
}

export default function LocalSupplierInvoicesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { active } = useKeepAliveContext()
  const { access, currentUser } = useAuthStore()
  const isAdmin = access.isAdmin
  const managedStoreCodes = access.managedStoreCodes()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'

  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<LocalSupplierInvoiceListDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [sortBy, setSortBy] = useState('createdAt')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend')

  // 筛选条件
  const [storeCode, setStoreCode] = useState<string | undefined>(undefined)
  const [supplierCode, setSupplierCode] = useState<string | undefined>(undefined)
  const [invoiceNo, setInvoiceNo] = useState('')
  const [keyword, setKeyword] = useState('')

  // 下拉选项
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])

  // 行选择
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  // 创建 Modal
  const [createVisible, setCreateVisible] = useState(false)
  const [importVisible, setImportVisible] = useState(false)
  const [createForm] = Form.useForm()
  const [creating, setCreating] = useState(false)
  const [_invoiceNoChecking, setInvoiceNoChecking] = useState(false)

  // 从 HQ 增量同步
  const [hqSyncModalOpen, setHqSyncModalOpen] = useState(false)
  const [hqSyncing, setHqSyncing] = useState(false)
  const [hqSyncForm] = Form.useForm()

  // 动态高度
  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const tableRegionRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)
  // KeepAlive 切换业务 Tab 时保留表体纵向位置，避免返回列表后跳回第一行。
  const invoiceTableRef = useRef<TableRef | null>(null)
  const lastInvoiceTableScrollTopRef = useRef(0)
  const wasInvoiceListTabActiveRef = useRef(active)
  const listRequestGuardRef = useRef(createLatestRequestGuard())
  const mountedRef = useRef(false)
  const latestLoadDataRef = useRef<() => Promise<void>>(async () => undefined)

  const loadData = async () => {
    if (!mountedRef.current) return

    if (shouldSkipScopedStoreQuery(managedStoreCodes)) {
      listRequestGuardRef.current.invalidate()
      setData([])
      setTotal(0)
      setSelectedRowKeys([])
      setLoading(false)
      return
    }

    const startRow = (page - 1) * pageSize
    const filterModel: Record<string, unknown> = {}
    const scopedStoreFilter = buildScopedStoreCodeFilter(storeCode, managedStoreCodes)
    if (scopedStoreFilter) {
      filterModel.storeCode = scopedStoreFilter
    }
    if (supplierCode) {
      filterModel.supplierCode = { filterType: 'text', type: 'equals', filter: supplierCode }
    }
    if (invoiceNo) {
      filterModel.invoiceNo = { filterType: 'text', type: 'contains', filter: invoiceNo }
    }
    if (keyword) {
      filterModel.productKeyword = { filterType: 'text', filter: keyword }
    }
    const sortField = SORT_FIELD_MAP[sortBy] || sortBy
    const sortModel = [{ colId: sortField, sort: sortOrder === 'ascend' ? 'asc' : 'desc' }]

    await runLatestGuardedRequest(
      listRequestGuardRef.current,
      () =>
        getInvoiceGrid({
          startRow,
          endRow: startRow + pageSize,
          pageSize,
          filterModel: Object.keys(filterModel).length ? filterModel : undefined,
          sortModel,
        } as Record<string, unknown>),
      {
        onStart: () => setLoading(true),
        onSuccess: (result) => {
          setData(result?.items ?? [])
          setTotal(result?.total ?? 0)
        },
        onError: () => message.error(t('posAdmin.invoices.loadFailed', '加载进货单列表失败')),
        onSettled: () => setLoading(false),
      },
    )
  }

  useLayoutEffect(() => {
    mountedRef.current = true

    return () => {
      mountedRef.current = false
      listRequestGuardRef.current.invalidate()
    }
  }, [])

  useLayoutEffect(() => {
    latestLoadDataRef.current = loadData
  })

  useEffect(() => {
    void latestLoadDataRef.current()
  }, [page, pageSize, sortBy, sortOrder, managedStoreCodeKey])

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

    const calc = () => {
      const region = tableRegionRef.current
      if (!region) {
        return
      }

      const tableHeader = region.querySelector('.ant-table-thead') as HTMLElement | null
      const tableBody = region.querySelector('.ant-table-body') as HTMLElement | null
      const tableHeaderHeight = readOuterHeight(tableHeader)
      const horizontalScrollbarHeight = tableBody ? Math.max(0, tableBody.offsetHeight - tableBody.clientHeight) : 0
      // 分页在表格外层，scroll.y 只给表体，避免固定列继续画到分页区域。
      const available = Math.floor(region.clientHeight - tableHeaderHeight - horizontalScrollbarHeight - 8)
      const nextScrollY = available > 200 ? available : 200
      setTableScrollY((current) => (
        current === undefined || Math.abs(current - nextScrollY) > 4 ? nextScrollY : current
      ))
    }

    const scheduleCalc = () => {
      if (frameId != null) {
        window.cancelAnimationFrame(frameId)
      }
      frameId = window.requestAnimationFrame(() => {
        frameId = null
        calc()
      })
    }

    scheduleCalc()
    window.addEventListener('resize', scheduleCalc)

    if (typeof ResizeObserver === 'undefined') {
      return () => {
        if (frameId != null) window.cancelAnimationFrame(frameId)
        window.removeEventListener('resize', scheduleCalc)
      }
    }

    const observer = new ResizeObserver(scheduleCalc)
    for (const element of [wrapRef.current, toolbarRef.current, tableRegionRef.current, pagerRef.current]) {
      if (element) {
        observer.observe(element)
      }
    }

    return () => {
      if (frameId != null) window.cancelAnimationFrame(frameId)
      window.removeEventListener('resize', scheduleCalc)
      observer.disconnect()
    }
  }, [data.length, pageSize, total])

  useEffect(() => {
    const wasActive = wasInvoiceListTabActiveRef.current
    wasInvoiceListTabActiveRef.current = active

    if (!shouldRestoreInvoiceTableScroll(wasActive, active)) {
      return
    }

    const scrollTop = lastInvoiceTableScrollTopRef.current
    return scheduleInvoiceTableScrollRestore({
      requestFrame: (callback) => window.requestAnimationFrame(callback),
      cancelFrame: (frameId) => window.cancelAnimationFrame(frameId),
      restore: () => invoiceTableRef.current?.scrollTo?.({ top: scrollTop }),
    })
  }, [active])

  const handleInvoiceTableScroll = (event: React.UIEvent<HTMLDivElement>) => {
    lastInvoiceTableScrollTopRef.current = getNextInvoiceTableScrollTop(
      active,
      lastInvoiceTableScrollTopRef.current,
      event.currentTarget.scrollTop,
    )
  }

  useEffect(() => {
    const loadOptions = async () => {
      const suppliersResult = await getActiveLocalSuppliers()
        .then((value) => ({ status: 'fulfilled' as const, value }))
        .catch(() => ({ status: 'rejected' as const }))

      try {
        const stores = managedStoreCodes === null
          ? filterStoreOptionsByManagedCodes(await getActiveStores(), managedStoreCodes)
          : buildStoreOptionsFromUserStores(currentUser?.stores, { manageableOnly: true })
        setStoreOptions(stores)
        if (storeCode && !stores.some((store) => store.value === storeCode)) {
          setStoreCode(undefined)
        }
      } catch {
        setStoreOptions([])
      }

      if (suppliersResult.status === 'fulfilled') {
        setSupplierOptions(
          suppliersResult.value.map((s) => ({
            label: s.name || s.localSupplierCode,
            value: s.localSupplierCode,
          })),
        )
      } else {
        setSupplierOptions([])
      }
    }
    loadOptions()
  }, [currentUser?.stores, managedStoreCodeKey, storeCode])

  const requestFirstPage = (deferUntilCommitted = false, reloadFromDependencies = page !== 1) => {
    if (!mountedRef.current) return

    if (reloadFromDependencies) {
      listRequestGuardRef.current.invalidate()
      if (page !== 1) setPage(1)
      return
    }

    if (deferUntilCommitted) {
      setTimeout(() => void latestLoadDataRef.current(), 0)
      return
    }

    void latestLoadDataRef.current()
  }

  const handleSearch = () => {
    requestFirstPage()
  }

  const handleReset = () => {
    const reloadFromDependencies = page !== 1 || sortBy !== 'createdAt' || sortOrder !== 'descend'
    setStoreCode(undefined)
    setSupplierCode(undefined)
    setInvoiceNo('')
    setKeyword('')
    setSortBy('createdAt')
    setSortOrder('descend')
    requestFirstPage(true, reloadFromDependencies)
  }

  const handleDelete = async (invoiceGuid: string) => {
    try {
      await deleteInvoice(invoiceGuid)
      message.success(t('message.deleteSuccess'))
      void latestLoadDataRef.current()
    } catch {
      message.error(t('message.deleteFailed'))
    }
  }

  const handleCreate = async () => {
    const values = await createForm.validateFields()
    if (!isStoreCodeInManagedScope(values.storeCode, managedStoreCodes)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    // 随货单号重复检测
    const invoiceNoValue = values.invoiceNo?.trim()
    if (invoiceNoValue) {
      setInvoiceNoChecking(true)
      try {
        const checkResult = await checkInvoiceNoExists({
          storeCode: values.storeCode,
          supplierCode: values.supplierCode,
          invoiceNo: invoiceNoValue,
        })
        if (checkResult.exists) {
          message.error(t('posAdmin.invoices.invoiceNoDuplicate'))
          setInvoiceNoChecking(false)
          return
        }
      } catch {
        // 检测失败不阻止创建
      }
      setInvoiceNoChecking(false)
    }

    setCreating(true)
    try {
      const newGuid = await createInvoice({
        storeCode: values.storeCode,
        supplierCode: values.supplierCode,
        invoiceNo: invoiceNoValue,
        orderDate: values.orderDate?.format('YYYY-MM-DD'),
        inboundDate: values.inboundDate?.format('YYYY-MM-DD'),
        remarks: values.remarks?.trim() || undefined,
      })
      message.success(t('message.createSuccess'))
      setCreateVisible(false)
      createForm.resetFields()
      navigate(`/pos-admin/local-supplier-invoices/${newGuid}`)
    } catch {
      message.error(t('message.createFailed'))
    } finally {
      setCreating(false)
    }
  }

  const handleImportedInvoiceCreated = async (invoiceGuid: string) => {
    setImportVisible(false)
    await latestLoadDataRef.current()
    navigate(`/pos-admin/local-supplier-invoices/${invoiceGuid}`)
  }

  const openHqSyncModal = () => {
    hqSyncForm.resetFields()
    hqSyncForm.setFieldsValue({
      dateRange: [dayjs().subtract(30, 'day'), dayjs()],
    })
    setHqSyncModalOpen(true)
  }

  const showHqSyncResult = (result: LocalSupplierInvoiceHqSyncResult, failed = false) => {
    const content = (
      <div>
        <p>{t('posAdmin.invoices.invoiceAdded', '主表新增')}：{result.invoiceAddedCount} {t('posAdmin.invoices.recordsUnit', '条')}</p>
        <p>{t('posAdmin.invoices.invoiceUpdated', '主表更新')}：{result.invoiceUpdatedCount} {t('posAdmin.invoices.recordsUnit', '条')}</p>
        <p>{t('posAdmin.invoices.detailAdded', '明细新增')}：{result.detailAddedCount} {t('posAdmin.invoices.recordsUnit', '条')}</p>
        <p>{t('posAdmin.invoices.detailUpdated', '明细更新')}：{result.detailUpdatedCount} {t('posAdmin.invoices.recordsUnit', '条')}</p>
        <p>{t('posAdmin.invoices.totalProcessed', '总处理')}：{result.totalProcessed} {t('posAdmin.invoices.recordsUnit', '条')}</p>
        <p>{t('posAdmin.invoices.duration', '耗时')}：{(result.durationMs / 1000).toFixed(2)} {t('posAdmin.invoices.seconds', '秒')}</p>
        {result.errors && result.errors.length > 0 && (
          <div>
            <p style={{ color: 'red' }}>{t('posAdmin.invoices.errorInfo', '错误信息')}：</p>
            <ul>
              {result.errors.map((err, idx) => (
                <li key={idx}>{err}</li>
              ))}
            </ul>
          </div>
        )}
      </div>
    )

    if (failed) {
      Modal.warning({
        title: t('posAdmin.invoices.hqSyncFailed', '从HQ同步失败'),
        width: 600,
        content,
      })
      return
    }

    Modal.info({
      title: t('posAdmin.invoices.hqSyncResult', 'HQ同步结果'),
      width: 600,
      content,
    })
  }

  const handleSyncFromHq = async () => {
    try {
      const values = await hqSyncForm.validateFields()
      const dto: LocalSupplierInvoiceHqSyncRequest = {}
      if (values.selectedStoreCodes && values.selectedStoreCodes.length > 0) {
        dto.selectedStoreCodes = values.selectedStoreCodes
      }
      if (values.dateRange && values.dateRange.length === 2) {
        dto.startDate = values.dateRange[0].format('YYYY-MM-DD')
        dto.endDate = values.dateRange[1].format('YYYY-MM-DD')
      }

      setHqSyncing(true)
      const result = await syncInvoicesFromHq(dto)
      setHqSyncModalOpen(false)
      showHqSyncResult(result)
      setSelectedRowKeys([])
      await latestLoadDataRef.current()
    } catch (error) {
      const result = getHqSyncResultFromError(error)
      if (result) {
        showHqSyncResult(result, true)
        return
      }
      message.error(error instanceof Error ? error.message : t('posAdmin.invoices.hqSyncFailed', '从HQ同步失败'))
    } finally {
      setHqSyncing(false)
    }
  }

  const columns: ColumnsType<LocalSupplierInvoiceListDto> = [
    {
      title: t('column.index'),
      width: 60,
      align: 'right',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('column.store'),
      dataIndex: 'storeCode',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'storeCode' ? sortOrder : undefined,
      render: (_: string, record) => {
        const storeText = record.storeName ? `${record.storeCode} - ${record.storeName}` : record.storeCode || '--'

        return (
          <Tag
            color={getStableTagColor(record.storeCode || '')}
            style={{
              maxWidth: '100%',
              whiteSpace: 'normal',
            }}
            title={storeText}
          >
            {/* 分店名称较长时允许自动换行，最多展示两行，避免挤压后续列。 */}
            <span
              style={{
                display: '-webkit-box',
                overflow: 'hidden',
                lineHeight: '18px',
                overflowWrap: 'anywhere',
                WebkitBoxOrient: 'vertical',
                WebkitLineClamp: 2,
              }}
            >
              {storeText}
            </span>
          </Tag>
        )
      },
    },
    {
      title: t('column.supplier'),
      dataIndex: 'supplierCode',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'supplierCode' ? sortOrder : undefined,
      render: (_: string, record) => {
        const supplierText = record.supplierName ? `${record.supplierCode} - ${record.supplierName}` : record.supplierCode || '--'

        return (
          <Tag
            color={getStableTagColor(record.supplierCode || '')}
            style={{
              maxWidth: '100%',
              whiteSpace: 'normal',
            }}
            title={supplierText}
          >
            {/* 供应商内容较长时允许自动换行，最多展示两行，避免挤压后续列。 */}
            <span
              style={{
                display: '-webkit-box',
                overflow: 'hidden',
                lineHeight: '18px',
                overflowWrap: 'anywhere',
                WebkitBoxOrient: 'vertical',
                WebkitLineClamp: 2,
              }}
            >
              {supplierText}
            </span>
          </Tag>
        )
      },
    },
    {
      title: t('posAdmin.invoices.invoiceNo'),
      dataIndex: 'invoiceNo',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'invoiceNo' ? sortOrder : undefined,
      render: (value: string) => (
        <Space size={4}>
          <span>{value || '--'}</span>
          {value && (
            <Button
              type="text"
              size="small"
              icon={<CopyOutlined />}
              onClick={() => void copyTextToClipboard(value)}
            />
          )}
        </Space>
      ),
    },
    {
      title: t('posAdmin.invoices.orderDate'),
      dataIndex: 'orderDate',
      width: 120,
      sorter: true,
      sortOrder: sortBy === 'orderDate' ? sortOrder : undefined,
      render: (v: string) => formatDate(v),
    },
    {
      title: t('posAdmin.invoices.inboundDate'),
      dataIndex: 'inboundDate',
      width: 120,
      sorter: true,
      sortOrder: sortBy === 'inboundDate' ? sortOrder : undefined,
      render: (v: string) => formatDate(v),
    },
    {
      title: t('column.totalAmount'),
      dataIndex: 'totalAmount',
      width: 120,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'totalAmount' ? sortOrder : undefined,
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoices.receivedTotal', '已收总金额'),
      dataIndex: 'receivedTotalAmount',
      width: 120,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'receivedTotalAmount' ? sortOrder : undefined,
      render: (v: number) => formatAmount(v),
    },
    {
      title: t('posAdmin.invoices.flowStatus', '流程状态'),
      dataIndex: 'flowStatus',
      width: 100,
      sorter: true,
      sortOrder: sortBy === 'flowStatus' ? sortOrder : undefined,
      render: (v: number) => {
        const info = FLOW_STATUS_MAP[v] || { labelKey: String(v), color: 'default' }
        return <Tag color={info.color}>{t(info.labelKey)}</Tag>
      },
    },
    {
      title: t('posAdmin.invoices.inboundStatus', '入库状态'),
      dataIndex: 'inboundStatus',
      width: 100,
      sorter: true,
      sortOrder: sortBy === 'inboundStatus' ? sortOrder : undefined,
      render: (v: number) => {
        const info = INBOUND_STATUS_MAP[v] || { labelKey: String(v), color: 'default' }
        return <Tag color={info.color}>{t(info.labelKey)}</Tag>
      },
    },
    {
      title: t('column.remarks'),
      dataIndex: 'remarks',
      width: 180,
      ellipsis: true,
      render: (v: string) => v || '--',
    },
    {
      title: t('column.createTime'),
      dataIndex: 'createdAt',
      width: 170,
      sorter: true,
      sortOrder: sortBy === 'createdAt' ? sortOrder : undefined,
      render: (v: string) => formatDateTime(v),
    },
    {
      title: t('column.creator'),
      dataIndex: 'createdBy',
      width: 120,
      render: (v: string) => v || '--',
    },
    {
      title: t('column.updateTime'),
      dataIndex: 'updatedAt',
      width: 170,
      sorter: true,
      sortOrder: sortBy === 'updatedAt' ? sortOrder : undefined,
      render: (v: string) => formatDateTime(v),
    },
    {
      title: t('column.updater'),
      dataIndex: 'updatedBy',
      width: 120,
      render: (v: string) => v || '--',
    },
    {
      title: t('column.action'),
      key: 'action',
      fixed: 'right',
      width: 180,
      render: (_, record) => (
        <Space size={0}>
          <Button
            type="link"
            onClick={() => navigate(`/pos-admin/invoice-detail/${record.invoiceGUID}`)}
          >
            {t('common.view')}
          </Button>
          {isAdmin && (
            <Button
              type="link"
              onClick={() => navigate(`/pos-admin/local-supplier-invoices/${record.invoiceGUID}`)}
            >
              {t('common.edit')}
            </Button>
          )}
          {isAdmin && (
            <Popconfirm
              title={t('posAdmin.invoices.confirmDeleteInvoice')}
              description={t('posAdmin.invoices.deleteIrreversible')}
              okText={t('common.delete')}
              cancelText={t('common.cancel')}
              okButtonProps={{ danger: true }}
              onConfirm={() => void handleDelete(record.invoiceGUID)}
            >
              <Button type="link" danger>
                {t('common.delete')}
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ]

  return (
    <Card
      title={t('posAdmin.invoices.title')}
      styles={{ body: { padding: 0 } }}
      extra={
        <Space>
          {isAdmin && (
            <Button
              icon={<CloudSyncOutlined />}
              loading={hqSyncing}
              disabled={storeOptions.length === 0}
              onClick={openHqSyncModal}
            >
              {t('posAdmin.invoices.syncFromHQ', '从HQ同步')}
            </Button>
          )}
          {isAdmin && (
            <Button
              icon={<UploadOutlined />}
              disabled={storeOptions.length === 0}
              onClick={() => setImportVisible(true)}
            >
              {t('posAdmin.invoices.import.uploadButton')}
            </Button>
          )}
          {isAdmin && (
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={storeOptions.length === 0}
              onClick={() => setCreateVisible(true)}
            >
              {t('posAdmin.invoices.createInvoice')}
            </Button>
          )}
        </Space>
      }
    >
      <div
        ref={wrapRef}
        style={{
          height: 'calc(100vh - 160px)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <div ref={toolbarRef} style={{ padding: 16 }}>
          <Space wrap>
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder={t('form.pleaseSelectStore')}
              style={{ width: 200 }}
              value={storeCode}
              onChange={(v) => {
                setStoreCode(v)
              }}
              options={storeOptions}
            />
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder={t('form.pleaseSelectSupplier')}
              style={{ width: 200 }}
              value={supplierCode}
              onChange={(v) => {
                setSupplierCode(v)
              }}
              options={supplierOptions}
            />
            <Input
              allowClear
              placeholder={t('posAdmin.invoices.invoiceNo')}
              style={{ width: 180 }}
              value={invoiceNo}
              onChange={(e) => setInvoiceNo(e.target.value)}
            />
            <Input
              allowClear
              placeholder={t('posAdmin.invoices.productKeyword', '商品关键词')}
              style={{ width: 180 }}
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
            />
            <Button type="primary" onClick={handleSearch}>
              {t('common.query')}
            </Button>
            <Button onClick={handleReset}>{t('common.reset')}</Button>
          </Space>
        </div>

        <div ref={tableRegionRef} style={{ flex: 1, minHeight: 0, overflow: 'hidden' }}>
          <Table
            ref={invoiceTableRef}
            rowKey="invoiceGUID"
            loading={loading}
            dataSource={data}
            columns={columns}
            pagination={false}
            scroll={{ x: 2200, y: tableScrollY }}
            rowSelection={
              isAdmin
                ? {
                    selectedRowKeys,
                    onChange: (keys) => setSelectedRowKeys(keys),
                  }
                : undefined
            }
            rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
            onScroll={handleInvoiceTableScroll}
            onChange={(_pagination, _filters, sorter) => {
              const s = Array.isArray(sorter) ? sorter[0] : sorter
              const field = s?.field || s?.column?.dataIndex
              const order = s?.order as 'ascend' | 'descend' | undefined
              if (field && order) {
                setSortBy(String(field))
                setSortOrder(order)
              } else {
                setSortBy('createdAt')
                setSortOrder('descend')
              }
            }}
          />
        </div>

        <div
          ref={pagerRef}
          style={{
            padding: '8px 16px',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            width: '100%',
            background: '#fff',
            position: 'relative',
            zIndex: 3,
            flexShrink: 0,
          }}
        >
          <div />
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            onChange={(p, ps) => {
              setPage(p)
              setPageSize(ps)
            }}
            showSizeChanger
            responsive={false}
            pageSizeOptions={[10, 20, 50, 100, 200]}
          />
        </div>
      </div>

      <Modal
        open={hqSyncModalOpen}
        title={t('posAdmin.invoices.hqSyncTitle', '从HQ同步分店进货单')}
        confirmLoading={hqSyncing}
        onCancel={() => setHqSyncModalOpen(false)}
        onOk={() => void handleSyncFromHq()}
        width={550}
        forceRender
      >
        <Form form={hqSyncForm} layout="vertical">
          <Form.Item name="selectedStoreCodes" label={t('posAdmin.invoices.storeOptional', '分店（不选则全部）')}>
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder={t('posAdmin.invoices.syncAllStores', '不选则同步所有分店')}
              allowClear
            />
          </Form.Item>
          <Form.Item name="dateRange" label={t('posAdmin.invoices.syncDateRange', '同步日期范围')}>
            <DatePicker.RangePicker style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={createVisible}
        title={t('posAdmin.invoices.createTitle')}
        confirmLoading={creating}
        onCancel={() => {
          setCreateVisible(false)
          createForm.resetFields()
        }}
        onOk={() => void handleCreate()}
      >
        <Form form={createForm} layout="vertical">
          <Form.Item
            name="storeCode"
            label={t('column.store')}
            rules={[{ required: true, message: t('form.pleaseSelectStore') }]}
          >
            <Select
              showSearch
              optionFilterProp="label"
              placeholder={t('form.pleaseSelectStore')}
              options={storeOptions}
            />
          </Form.Item>
          <Form.Item
            name="supplierCode"
            label={t('column.supplier')}
            rules={[{ required: true, message: t('form.pleaseSelectSupplier') }]}
          >
            <Select
              showSearch
              optionFilterProp="label"
              placeholder={t('form.pleaseSelectSupplier')}
              options={supplierOptions}
            />
          </Form.Item>
          <Form.Item
            name="invoiceNo"
            label={t('posAdmin.invoices.invoiceNo')}
            validateTrigger={['onBlur']}
            rules={[
              { required: true, message: t('posAdmin.invoices.invoiceNoRequired') },
              {
                validator: async (_, value) => {
                  if (!value?.trim()) return
                  const storeCodeValue = createForm.getFieldValue('storeCode')
                  const supplierCodeValue = createForm.getFieldValue('supplierCode')
                  if (!storeCodeValue || !supplierCodeValue) return
                  try {
                    const result = await checkInvoiceNoExists({
                      storeCode: storeCodeValue,
                      supplierCode: supplierCodeValue,
                      invoiceNo: value.trim(),
                    })
                    if (result.exists) {
                      throw new Error(t('posAdmin.invoices.invoiceNoDuplicate'))
                    }
                  } catch (err) {
                    if (err instanceof Error && err.message === t('posAdmin.invoices.invoiceNoDuplicate')) {
                      throw err
                    }
                  }
                },
              },
            ]}
          >
            <Input placeholder={t('posAdmin.invoices.invoiceNoRequired')} />
          </Form.Item>
          <Form.Item name="orderDate" label={t('posAdmin.invoices.orderDate')}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="inboundDate" label={t('posAdmin.invoices.inboundDate')}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="remarks" label={t('column.remarks')}>
            <Input.TextArea rows={3} placeholder={t('form.pleaseInput')} />
          </Form.Item>
        </Form>
      </Modal>

      <ImportInvoiceModal
        open={importVisible}
        storeOptions={storeOptions}
        supplierOptions={supplierOptions}
        onCancel={() => setImportVisible(false)}
        onCreated={handleImportedInvoiceCreated}
      />
    </Card>
  )
}
