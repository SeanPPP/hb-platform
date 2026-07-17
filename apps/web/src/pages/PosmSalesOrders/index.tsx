import {
  DownloadOutlined,
  FilePdfOutlined,
  ReloadOutlined,
  SearchOutlined,
  ShopOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Image,
  Input,
  InputNumber,
  Modal,
  Pagination,
  Select,
  Space,
  Table,
  Tag,
  TimePicker,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { FilterDropdownProps } from 'antd/es/table/interface'
import dayjs from 'dayjs'
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../../store/auth'
import { getActiveStores } from '../../services/storeService'
import {
  applyPosmSalesOrderColumnFilterDraft,
  applyPosmSalesOrderQueryChange,
  applyPosmSalesOrderTopFilterDraft,
  buildPosmSalesOrderListQuery,
  createPosmSalesOrderColumnFilterDraft,
  createResetPosmSalesOrderState,
  mapPosmSalesOrderSortState,
  normalizePosmSalesOrderFilterNumber,
  isLatestPosmSalesOrderRequest,
  syncColumnFiltersToTopFilters,
  validatePosmSalesOrderNumberRanges,
} from './posmSalesOrdersLogic'
import {
  fetchTaxInvoicePdf,
  getSalesOrderDetail,
  getSalesOrderList,
  getTaxInvoicePdfUrl,
} from '../../services/posmSalesOrderService'
import type {
  PosmSalesOrder,
  PosmSalesOrderColumnFilters,
  PosmSalesOrderDetailResponse,
  PosmSalesOrderSortField,
  PosmSalesOrderSortState,
} from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'
import { formatPosmSalesOrderLocalTime } from './time'

const { Text } = Typography

const BRANCH_COLORS = [
  'blue',
  'green',
  'orange',
  'red',
  'cyan',
  'purple',
  'magenta',
  'lime',
  'gold',
  'volcano',
  'geekblue',
]

function getBranchColor(branchCode?: string): string {
  if (!branchCode) return 'default'
  let hash = 0
  for (let i = 0; i < branchCode.length; i++) {
    const char = branchCode.charCodeAt(i)
    hash = (hash << 5) - hash + char
    hash &= hash
  }
  return BRANCH_COLORS[Math.abs(hash) % BRANCH_COLORS.length]
}

export default function PosmSalesOrdersPage() {
  const { t } = useTranslation()
  const access = useAuthStore((s) => s.access)
  const currentUser = useAuthStore((s) => s.currentUser)
  const managedStoreCodes = access.managedStoreCodes?.()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<PosmSalesOrder[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [stores, setStores] = useState<{ label: string; value: string }[]>([])

  const [filterBranchCode, setFilterBranchCode] = useState('')
  const [filterOrderType, setFilterOrderType] = useState<OrderType>(OrderType.All)
  const [filterKeyword, setFilterKeyword] = useState('')
  const [appliedOrderType, setAppliedOrderType] = useState<OrderType>(OrderType.All)
  const [appliedKeyword, setAppliedKeyword] = useState('')
  const [filterDateRange, setFilterDateRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>([
    dayjs(),
    dayjs(),
  ])
  const [columnFilters, setColumnFilters] = useState<PosmSalesOrderColumnFilters>({
    startDate: dayjs().format('YYYY-MM-DD'),
    endDate: dayjs().format('YYYY-MM-DD'),
    branchCode: '',
  })
  const [columnFilterDraft, setColumnFilterDraft] = useState<PosmSalesOrderColumnFilters>({
    startDate: dayjs().format('YYYY-MM-DD'),
    endDate: dayjs().format('YYYY-MM-DD'),
    branchCode: '',
  })
  const [sort, setSort] = useState<PosmSalesOrderSortState>({
    field: 'orderTime',
    direction: 'asc',
  })
  const [sortColumnKey, setSortColumnKey] = useState('date')

  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([])
  const [detailData, setDetailData] = useState<Record<string, PosmSalesOrderDetailResponse>>({})

  const [pdfModalVisible, setPdfModalVisible] = useState(false)
  const [pdfBlobUrl, setPdfBlobUrl] = useState('')
  const [pdfLoading, setPdfLoading] = useState(false)
  const [pdfOrderGuid, setPdfOrderGuid] = useState('')

  const wrapRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const skipNextPageLoadRef = useRef(false)
  const latestRequestIdRef = useRef(0)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)

  const [storesLoaded, setStoresLoaded] = useState(false)

  const orderTypeOptions = useMemo(
    () => [
      { label: t('posmOrders.all'), value: OrderType.All },
      { label: t('posmOrders.pending'), value: OrderType.Pending },
      { label: t('posmOrders.paid'), value: OrderType.Paid },
      { label: t('posmOrders.cancelled'), value: OrderType.Cancelled },
      { label: t('posmOrders.refunded'), value: OrderType.Refunded },
      { label: t('posmOrders.installment'), value: OrderType.Installment },
    ],
    [t],
  )

  const getCurrentQueryState = () => ({
    startDate: columnFilters.startDate || '',
    endDate: columnFilters.endDate || '',
    branchCode: columnFilters.branchCode || '',
    orderType: appliedOrderType,
    keyword: appliedKeyword,
    page,
    pageSize,
    columnFilters,
    sort,
  })

  const loadData = async (overrides: Partial<ReturnType<typeof getCurrentQueryState>> = {}) => {
    const nextColumnFilters = { ...columnFilters, ...overrides.columnFilters }
    if (!validatePosmSalesOrderNumberRanges(nextColumnFilters).isValid) {
      message.error(t('posmOrders.invalidRange'))
      return
    }
    const requestId = ++latestRequestIdRef.current
    setLoading(true)
    try {
      const result = await getSalesOrderList(buildPosmSalesOrderListQuery(getCurrentQueryState(), overrides))
      // 只允许最后一次请求更新列表，避免慢响应覆盖用户刚应用的新条件。
      if (isLatestPosmSalesOrderRequest(requestId, latestRequestIdRef.current)) {
        setData(result?.items ?? [])
        setTotal(result?.total ?? 0)
      }
    } catch {
      if (isLatestPosmSalesOrderRequest(requestId, latestRequestIdRef.current)) {
        message.error(t('posmOrders.loadFailed'))
      }
    } finally {
      if (isLatestPosmSalesOrderRequest(requestId, latestRequestIdRef.current)) {
        setLoading(false)
      }
    }
  }

  const loadStores = async (codes?: string[] | null) => {
    try {
      if (codes === null || codes === undefined) {
        const storeOptions = await getActiveStores()
        setStores(storeOptions)
      } else if (codes.length && currentUser?.stores?.length) {
        const visible = currentUser.stores
          .filter((s) => codes.includes(s.storeCode))
          .map((s) => ({ label: s.storeName || s.storeCode, value: s.storeCode }))
        setStores(visible)
        if (visible.length >= 1 && !filterBranchCode) {
          setFilterBranchCode(visible[0].value)
          setColumnFilters((current) => ({ ...current, branchCode: visible[0].value }))
          setColumnFilterDraft((current) => ({ ...current, branchCode: visible[0].value }))
        }
      } else {
        setStores([])
      }
    } catch {
      // ignore
    } finally {
      setStoresLoaded(true)
    }
  }

  const loadDetail = async (record: PosmSalesOrder) => {
    if (!record.orderGuid || detailData[record.orderGuid]) return
    try {
      const result = await getSalesOrderDetail(record.orderGuid)
      if (result) {
        setDetailData((prev) => ({ ...prev, [record.orderGuid!]: result }))
      }
    } catch {
      // ignore
    }
  }

  useEffect(() => {
    void loadStores(managedStoreCodes)
  }, [managedStoreCodes?.join(',')])

  useEffect(() => {
    if (storesLoaded) {
      if (skipNextPageLoadRef.current) {
        // 搜索/重置已用显式参数请求，跳过 setPage(1) 触发的重复加载。
        skipNextPageLoadRef.current = false
        return
      }
      void loadData()
    }
  }, [page, pageSize, storesLoaded])

  useLayoutEffect(() => {
    const calc = () => {
      const containerH = wrapRef.current?.clientHeight || window.innerHeight
      const pagerH = pagerRef.current?.getBoundingClientRect().height || 0
      const available = containerH - pagerH - 8
      setTableScrollY(available > 200 ? available : 200)
    }
    calc()
    window.addEventListener('resize', calc)
    return () => window.removeEventListener('resize', calc)
  }, [pageSize, total])

  const handleSearch = () => {
    const topFilters = {
      startDate: filterDateRange?.[0]?.format('YYYY-MM-DD') || '',
      endDate: filterDateRange?.[1]?.format('YYYY-MM-DD') || '',
      branchCode: filterBranchCode,
      orderType: filterOrderType,
      keyword: filterKeyword,
    }
    const nextState = applyPosmSalesOrderTopFilterDraft(getCurrentQueryState(), topFilters)
    if (!validatePosmSalesOrderNumberRanges(nextState.columnFilters).isValid) {
      message.error(t('posmOrders.invalidRange'))
      return
    }
    // 顶部查询是同步点：日期、分店同时写入列头受控状态。
    setColumnFilters(nextState.columnFilters)
    setColumnFilterDraft(nextState.columnFilters)
    setAppliedOrderType(nextState.orderType)
    setAppliedKeyword(nextState.keyword)
    if (page !== 1) {
      skipNextPageLoadRef.current = true
    }
    setPage(1)
    void loadData(nextState)
  }

  const handleRefresh = () => {
    void loadData()
  }

  const handleReset = () => {
    const today = dayjs()
    const resetState = createResetPosmSalesOrderState(today.format('YYYY-MM-DD'), pageSize)
    if (page !== 1) {
      skipNextPageLoadRef.current = true
    }
    setFilterBranchCode(resetState.branchCode)
    setFilterOrderType(resetState.orderType)
    setFilterKeyword(resetState.keyword)
    setAppliedOrderType(resetState.orderType)
    setAppliedKeyword(resetState.keyword)
    setFilterDateRange([today, today])
    setColumnFilters(resetState.columnFilters)
    setColumnFilterDraft(resetState.columnFilters)
    setSort(resetState.sort)
    setSortColumnKey('date')
    setPage(resetState.page)
    void loadData(resetState)
  }

  const handlePreviewPdf = async (orderGuid: string) => {
    setPdfOrderGuid(orderGuid)
    setPdfLoading(true)
    setPdfModalVisible(true)
    try {
      const blobUrl = await fetchTaxInvoicePdf(orderGuid)
      if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
      setPdfBlobUrl(blobUrl)
    } catch {
      message.error(t('posmOrders.getInvoiceFailed'))
    } finally {
      setPdfLoading(false)
    }
  }

  const handleDownloadPdf = (orderGuid: string) => {
    const url = getTaxInvoicePdfUrl(orderGuid)
    const a = document.createElement('a')
    a.href = url
    a.download = `TaxInvoice_${orderGuid}.pdf`
    a.target = '_blank'
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
  }

  const updateColumnFilterDraft = (changes: Partial<PosmSalesOrderColumnFilters>) => {
    setColumnFilterDraft((current) =>
      createPosmSalesOrderColumnFilterDraft(current, changes),
    )
  }

  const applyColumnFilters = (
    confirm: FilterDropdownProps['confirm'],
    nextFilters: PosmSalesOrderColumnFilters = columnFilterDraft,
  ) => {
    if (!validatePosmSalesOrderNumberRanges(nextFilters).isValid) {
      message.error(t('posmOrders.invalidRange'))
      return
    }

    const nextState = applyPosmSalesOrderColumnFilterDraft(
      getCurrentQueryState(),
      nextFilters,
    )
    const topFilters = syncColumnFiltersToTopFilters(nextState.columnFilters)
    // 列头日期和分店与顶部控件共用查询语义，应用时双向对齐。
    setColumnFilters(nextFilters)
    setColumnFilterDraft(nextFilters)
    setFilterBranchCode(topFilters.branchCode)
    setFilterDateRange(
      topFilters.startDate && topFilters.endDate
        ? [dayjs(topFilters.startDate), dayjs(topFilters.endDate)]
        : null,
    )
    if (page !== 1) {
      skipNextPageLoadRef.current = true
    }
    setPage(1)
    confirm({ closeDropdown: true })
    void loadData(nextState)
  }

  const clearColumnFilters = (
    keys: Array<keyof PosmSalesOrderColumnFilters>,
    confirm: FilterDropdownProps['confirm'],
  ) => {
    const nextFilters = createPosmSalesOrderColumnFilterDraft(columnFilters)
    keys.forEach((key) => {
      nextFilters[key] = undefined
    })
    applyColumnFilters(confirm, nextFilters)
  }

  const initializeColumnFilterDraft = (open: boolean) => {
    if (open) {
      // 每次打开都从已应用条件创建草稿，取消关闭不会污染后续请求。
      setColumnFilterDraft(createPosmSalesOrderColumnFilterDraft(columnFilters))
    }
  }

  const filterIcon = (active?: boolean) => (
    <SearchOutlined style={{ color: active ? '#1677ff' : undefined }} />
  )
  const filterDropdownStyle = { padding: 8, width: 250 }

  const makeTextFilterDropdown = (
    key: 'orderGuidKeyword' | 'deviceCodeKeyword',
    placeholder: string,
    ariaLabel: string,
  ) =>
    ({ confirm }: FilterDropdownProps) => (
      <div
        style={filterDropdownStyle}
        onKeyDown={(event) => event.stopPropagation()}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <Space direction="vertical" style={{ width: '100%' }}>
          <Input
            autoFocus
            allowClear
            aria-label={ariaLabel}
            value={columnFilterDraft[key] ?? ''}
            placeholder={placeholder}
            onChange={(event) => updateColumnFilterDraft({ [key]: event.target.value })}
            onPressEnter={() => applyColumnFilters(confirm)}
          />
          <Space>
            <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>
              {t('posmOrders.applyFilter')}
            </Button>
            <Button size="small" onClick={() => clearColumnFilters([key], confirm)}>
              {t('posmOrders.resetFilter')}
            </Button>
          </Space>
        </Space>
      </div>
    )

  const makeBranchFilterDropdown = ({ confirm }: FilterDropdownProps) => (
    <div
      style={filterDropdownStyle}
      onKeyDown={(event) => event.stopPropagation()}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <Space direction="vertical" style={{ width: '100%' }}>
        <Select
          allowClear
          aria-label={t('posmOrders.filterBranchAria')}
          showSearch
          value={columnFilterDraft.branchCode || undefined}
          placeholder={t('posmOrders.filterBranch')}
          style={{ width: '100%' }}
          options={stores}
          filterOption={(input, option) =>
            String(option?.label ?? '').toLowerCase().includes(input.toLowerCase())
          }
          onChange={(value) => {
            updateColumnFilterDraft({ branchCode: value || '' })
          }}
        />
        <Space>
          <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>
            {t('posmOrders.applyFilter')}
          </Button>
          <Button size="small" onClick={() => clearColumnFilters(['branchCode'], confirm)}>
            {t('posmOrders.resetFilter')}
          </Button>
        </Space>
      </Space>
    </div>
  )

  const makeDateFilterDropdown = ({ confirm }: FilterDropdownProps) => (
    <div
      style={{ ...filterDropdownStyle, width: 290 }}
      onKeyDown={(event) => event.stopPropagation()}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <Space direction="vertical" style={{ width: '100%' }}>
        <DatePicker.RangePicker
          aria-label={t('posmOrders.filterDateRangeAria')}
          value={
            columnFilterDraft.startDate && columnFilterDraft.endDate
              ? [dayjs(columnFilterDraft.startDate), dayjs(columnFilterDraft.endDate)]
              : null
          }
          placeholder={[t('posmOrders.filterDateStart'), t('posmOrders.filterDateEnd')]}
          onChange={(dates) => {
            const nextRange = dates?.[0] && dates?.[1] ? [dates[0], dates[1]] as [dayjs.Dayjs, dayjs.Dayjs] : null
            updateColumnFilterDraft({
              startDate: nextRange?.[0].format('YYYY-MM-DD'),
              endDate: nextRange?.[1].format('YYYY-MM-DD'),
            })
          }}
        />
        <Space>
          <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>
            {t('posmOrders.applyFilter')}
          </Button>
          <Button
            size="small"
            onClick={() => clearColumnFilters(['startDate', 'endDate'], confirm)}
          >
            {t('posmOrders.resetFilter')}
          </Button>
        </Space>
      </Space>
    </div>
  )

  const makeTimeFilterDropdown = ({ confirm }: FilterDropdownProps) => (
    <div
      style={{ ...filterDropdownStyle, width: 260 }}
      onKeyDown={(event) => event.stopPropagation()}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <Space direction="vertical" style={{ width: '100%' }}>
        <TimePicker.RangePicker
          aria-label={t('posmOrders.filterTimeRangeAria')}
          value={
            columnFilterDraft.timeStart && columnFilterDraft.timeEnd
              ? [
                  dayjs(`2000-01-01T${columnFilterDraft.timeStart}`),
                  dayjs(`2000-01-01T${columnFilterDraft.timeEnd}`),
                ]
              : null
          }
          format="HH:mm:ss"
          placeholder={[t('posmOrders.filterTimeStart'), t('posmOrders.filterTimeEnd')]}
          onChange={(times) =>
            updateColumnFilterDraft({
              timeStart: times?.[0]?.format('HH:mm:ss'),
              timeEnd: times?.[1]?.format('HH:mm:ss'),
            })
          }
        />
        <Space>
          <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>
            {t('posmOrders.applyFilter')}
          </Button>
          <Button
            size="small"
            onClick={() => clearColumnFilters(['timeStart', 'timeEnd'], confirm)}
          >
            {t('posmOrders.resetFilter')}
          </Button>
        </Space>
      </Space>
    </div>
  )

  const makeNumberFilterDropdown = (
    minKey: keyof PosmSalesOrderColumnFilters,
    maxKey: keyof PosmSalesOrderColumnFilters,
    columnLabel: string,
    integer = false,
  ) =>
    ({ confirm }: FilterDropdownProps) => (
      <div
        style={filterDropdownStyle}
        onKeyDown={(event) => event.stopPropagation()}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <Space direction="vertical" style={{ width: '100%' }}>
          <Space.Compact style={{ width: '100%' }}>
            <InputNumber
              aria-label={t('posmOrders.filterMinAria', { column: columnLabel })}
              min={0}
              precision={integer ? 0 : undefined}
              step={integer ? 1 : undefined}
              controls={false}
              value={columnFilterDraft[minKey] as number | undefined}
              placeholder={t('posmOrders.minValue')}
              style={{ width: '50%' }}
              onChange={(value) =>
                updateColumnFilterDraft({
                  [minKey]: normalizePosmSalesOrderFilterNumber(value, integer),
                })
              }
            />
            <InputNumber
              aria-label={t('posmOrders.filterMaxAria', { column: columnLabel })}
              min={0}
              precision={integer ? 0 : undefined}
              step={integer ? 1 : undefined}
              controls={false}
              value={columnFilterDraft[maxKey] as number | undefined}
              placeholder={t('posmOrders.maxValue')}
              style={{ width: '50%' }}
              onChange={(value) =>
                updateColumnFilterDraft({
                  [maxKey]: normalizePosmSalesOrderFilterNumber(value, integer),
                })
              }
            />
          </Space.Compact>
          <Space>
            <Button size="small" type="primary" onClick={() => applyColumnFilters(confirm)}>
              {t('posmOrders.applyFilter')}
            </Button>
            <Button size="small" onClick={() => clearColumnFilters([minKey, maxKey], confirm)}>
              {t('posmOrders.resetFilter')}
            </Button>
          </Space>
        </Space>
      </div>
    )

  const sortOrderFor = (field: PosmSalesOrderSortField, columnKey?: string) =>
    sort.field === field && (!columnKey || sortColumnKey === columnKey)
      ? sort.direction === 'asc'
        ? 'ascend' as const
        : 'descend' as const
      : null

  const numberFilterProps = (
    minKey: keyof PosmSalesOrderColumnFilters,
    maxKey: keyof PosmSalesOrderColumnFilters,
    columnLabel: string,
    integer = false,
  ) => ({
    filterDropdown: makeNumberFilterDropdown(minKey, maxKey, columnLabel, integer),
    onFilterDropdownOpenChange: initializeColumnFilterDraft,
    filterIcon,
    filtered: typeof columnFilters[minKey] === 'number' || typeof columnFilters[maxKey] === 'number',
  })

  const columns: ColumnsType<PosmSalesOrder> = [
    {
      title: t('posmOrders.serialNo'),
      width: 60,
      align: 'right',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      key: 'orderGuid',
      title: t('posmOrders.orderNo'),
      dataIndex: 'orderGuid',
      width: 120,
      sorter: true,
      sortOrder: sortOrderFor('orderGuid'),
      filterDropdown: makeTextFilterDropdown(
        'orderGuidKeyword',
        t('posmOrders.filterOrderGuid'),
        t('posmOrders.filterOrderGuidAria'),
      ),
      onFilterDropdownOpenChange: initializeColumnFilterDraft,
      filterIcon,
      filtered: Boolean(columnFilters.orderGuidKeyword?.trim()),
      render: (_, record) => (
        <Text ellipsis={{ tooltip: record.orderGuid }}>{record.orderGuid?.slice(-6) || '-'}</Text>
      ),
    },
    {
      key: 'branchName',
      title: t('posmOrders.branch'),
      dataIndex: 'branchName',
      width: 140,
      sorter: true,
      sortOrder: sortOrderFor('branchCode'),
      filterDropdown: makeBranchFilterDropdown,
      onFilterDropdownOpenChange: initializeColumnFilterDraft,
      filterIcon,
      filtered: Boolean(columnFilters.branchCode),
      render: (_, record) => (
        <Tag icon={<ShopOutlined />} color={getBranchColor(record.branchCode)}>
          {record.branchName || '-'}
        </Tag>
      ),
    },
    {
      key: 'deviceCode',
      title: t('posmOrders.device'),
      dataIndex: 'deviceCode',
      width: 110,
      sorter: true,
      sortOrder: sortOrderFor('deviceCode'),
      filterDropdown: makeTextFilterDropdown(
        'deviceCodeKeyword',
        t('posmOrders.filterDevice'),
        t('posmOrders.filterDeviceAria'),
      ),
      onFilterDropdownOpenChange: initializeColumnFilterDraft,
      filterIcon,
      filtered: Boolean(columnFilters.deviceCodeKeyword?.trim()),
      render: (value) => value || '-',
    },
    {
      key: 'date',
      title: t('posmOrders.date'),
      dataIndex: 'orderTime',
      width: 110,
      sorter: true,
      sortOrder: sortOrderFor('orderTime', 'date'),
      filterDropdown: makeDateFilterDropdown,
      onFilterDropdownOpenChange: initializeColumnFilterDraft,
      filterIcon,
      filtered: Boolean(columnFilters.startDate || columnFilters.endDate),
      render: (_, record) => formatPosmSalesOrderLocalTime(record.orderTime, 'YYYY-MM-DD'),
    },
    {
      key: 'time',
      title: t('posmOrders.time'),
      dataIndex: 'orderTime',
      width: 90,
      sorter: true,
      sortOrder: sortOrderFor('orderTime', 'time'),
      filterDropdown: makeTimeFilterDropdown,
      onFilterDropdownOpenChange: initializeColumnFilterDraft,
      filterIcon,
      filtered: Boolean(columnFilters.timeStart || columnFilters.timeEnd),
      render: (_, record) => formatPosmSalesOrderLocalTime(record.orderTime, 'HH:mm:ss'),
    },
    {
      key: 'skuCount',
      title: t('posmOrders.skuCount'),
      dataIndex: 'skuCount',
      width: 80,
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('skuCount'),
      ...numberFilterProps('skuCountMin', 'skuCountMax', t('posmOrders.skuCount'), true),
    },
    {
      key: 'itemCount',
      title: t('posmOrders.itemCount'),
      dataIndex: 'itemCount',
      width: 80,
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('itemCount'),
      ...numberFilterProps('itemCountMin', 'itemCountMax', t('posmOrders.itemCount'), true),
    },
    {
      key: 'totalAmount',
      title: t('posmOrders.totalAmount'),
      dataIndex: 'totalAmount',
      width: 110,
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('totalAmount'),
      ...numberFilterProps(
        'totalAmountMin',
        'totalAmountMax',
        t('posmOrders.totalAmount'),
      ),
      render: (_, record) => <Text>${(record.totalAmount || 0).toFixed(2)}</Text>,
    },
    {
      key: 'discountAmount',
      title: t('posmOrders.discount'),
      dataIndex: 'discountAmount',
      width: 100,
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('discountAmount'),
      ...numberFilterProps(
        'discountAmountMin',
        'discountAmountMax',
        t('posmOrders.discount'),
      ),
      render: (_, record) => (
        <Text type="secondary">${(record.discountAmount || 0).toFixed(2)}</Text>
      ),
    },
    {
      key: 'actualAmount',
      title: t('posmOrders.actualPay'),
      dataIndex: 'actualAmount',
      width: 110,
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('actualPay'),
      ...numberFilterProps('actualPayMin', 'actualPayMax', t('posmOrders.actualPay')),
      render: (_, record) => (
        <Text type="danger" strong>
          ${((record.totalAmount || 0) - (record.discountAmount || 0)).toFixed(2)}
        </Text>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 100,
      render: (_, record) => (
        <Space size="small">
          <Button
            type="text"
            icon={<FilePdfOutlined />}
            size="small"
            onClick={() => handlePreviewPdf(record.orderGuid || '')}
            title={t('posmOrders.previewInvoice')}
          />
          <Button
            type="text"
            icon={<DownloadOutlined />}
            size="small"
            onClick={() => handleDownloadPdf(record.orderGuid || '')}
            title={t('posmOrders.downloadInvoice')}
          />
        </Space>
      ),
    },
  ]

  return (
    <Card
      title={t('posmOrders.cashierRecords')}
      extra={
        <Space>
          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            {t('common.refresh')}
          </Button>
        </Space>
      }
      styles={{ body: { padding: 0 } }}
    >
      <div
        style={{
          padding: '12px 16px',
          display: 'flex',
          gap: 12,
          flexWrap: 'wrap',
          alignItems: 'center',
        }}
      >
        <DatePicker.RangePicker
          value={filterDateRange}
          onChange={(dates) => {
            const nextRange =
              dates?.[0] && dates?.[1] ? [dates[0], dates[1]] as [dayjs.Dayjs, dayjs.Dayjs] : null
            setFilterDateRange(nextRange)
          }}
          format="YYYY-MM-DD"
          style={{ width: 260 }}
        />
        <Select
          placeholder={t('posmOrders.branch')}
          value={filterBranchCode || undefined}
          onChange={(value) => {
            setFilterBranchCode(value || '')
          }}
          style={{ width: 180 }}
          allowClear
          showSearch
          filterOption={(input, option) =>
            String(option?.label ?? '')
              .toLowerCase()
              .includes(input.toLowerCase())
          }
          options={stores}
        />
        <Select
          placeholder={t('posmOrders.orderType')}
          value={filterOrderType}
          onChange={(value: OrderType) => setFilterOrderType(value)}
          style={{ width: 130 }}
          options={orderTypeOptions}
        />
        <Input
          placeholder={t('posmOrders.keyword')}
          value={filterKeyword}
          onChange={(e) => setFilterKeyword(e.target.value)}
          onPressEnter={handleSearch}
          allowClear
          style={{ width: 180 }}
        />
        <Button type="primary" onClick={handleSearch}>
          {t('common.query')}
        </Button>
        <Button onClick={handleReset}>{t('common.reset')}</Button>
      </div>

      <div
        ref={wrapRef}
        style={{
          height: 'calc(100vh - 160px)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <div style={{ flex: 1, minHeight: 0 }}>
          <Table
            rowKey="orderGuid"
            loading={loading}
            dataSource={data}
            columns={columns}
            pagination={false}
            onChange={(_pagination, _filters, sorter, extra) => {
              if (extra.action !== 'sort') return
              const activeSorter = Array.isArray(sorter) ? sorter[0] : sorter
              const nextSort = mapPosmSalesOrderSortState(
                activeSorter?.columnKey,
                activeSorter?.order,
              )
              const nextState = applyPosmSalesOrderQueryChange(getCurrentQueryState(), {
                sort: nextSort,
              })
              setSort(nextSort)
              setSortColumnKey(
                nextSort.field === 'orderTime'
                  ? String(activeSorter?.columnKey || 'date')
                  : String(activeSorter?.columnKey || nextSort.field),
              )
              if (page !== 1) {
                skipNextPageLoadRef.current = true
              }
              setPage(1)
              void loadData(nextState)
            }}
            scroll={tableScrollY ? { y: tableScrollY } : undefined}
            rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
            expandable={{
              expandedRowKeys,
              onExpandedRowsChange: (keys) => setExpandedRowKeys([...keys]),
              expandedRowRender: (record) => {
                const detail = detailData[record.orderGuid!]
                if (!detail) return <div>{t('common.loading')}</div>

                return (
                  <div style={{ padding: 16 }}>
                    <Card
                      title={t('posmOrders.orderDetail')}
                      size="small"
                      style={{ marginBottom: 16 }}
                    >
                      {detail.orderDetails?.map((item, index) => (
                        <div
                          key={index}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 12,
                            padding: 8,
                            background: index % 2 === 0 ? '#fafafa' : '#fff',
                          }}
                        >
                          {item.productImage && (
                            <Image
                              src={item.productImage}
                              alt={item.productName}
                              width={50}
                              height={50}
                              style={{ objectFit: 'cover' }}
                            />
                          )}
                          <div style={{ flex: 1 }}>
                            <div style={{ marginBottom: 4 }}>
                              <Text strong>{item.productName}</Text>
                            </div>
                            <div style={{ fontSize: 12 }}>
                              <Space size="large">
                                <span>
                                  {t('posmOrders.quantity')}: {item.quantity}
                                </span>
                                <span>
                                  {t('posmOrders.unitPrice')}: $
                                  {(item.unitPrice || 0).toFixed(2)}
                                </span>
                                <span>
                                  {t('posmOrders.discount')}: $
                                  {(item.discountAmount || 0).toFixed(2)}
                                </span>
                                <Text type="danger" strong>
                                  {t('posmOrders.subtotal')}: $
                                  {(item.actualAmount || 0).toFixed(2)}
                                </Text>
                              </Space>
                            </div>
                          </div>
                        </div>
                      ))}
                    </Card>

                    {detail.paymentDetails && detail.paymentDetails.length > 0 && (
                      <Card title={t('posmOrders.paymentInfo')} size="small">
                        {detail.paymentDetails.map((payment, index) => (
                          <div
                            key={index}
                            style={{
                              display: 'flex',
                              justifyContent: 'space-between',
                              padding: 8,
                              background: index % 2 === 0 ? '#fafafa' : '#fff',
                            }}
                          >
                            <Text>
                              {formatPosmSalesOrderLocalTime(payment.paymentTime, 'HH:mm:ss')}
                            </Text>
                            <Tag color="green">
                              {payment.paymentMethodName || t('posmOrders.payment')}
                            </Tag>
                            <Text type="success" strong>
                              ${(payment.amount || 0).toFixed(2)}
                            </Text>
                          </div>
                        ))}
                      </Card>
                    )}
                  </div>
                )
              },
              onExpand: (expanded, record) => {
                if (expanded) {
                  void loadDetail(record)
                }
              },
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
            zIndex: 1,
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
            pageSizeOptions={[10, 20, 50, 100]}
          />
        </div>
      </div>

      <Modal
        title={t('posmOrders.invoicePreview')}
        open={pdfModalVisible}
        onCancel={() => {
          setPdfModalVisible(false)
          if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
          setPdfBlobUrl('')
          setPdfOrderGuid('')
        }}
        footer={[
          <Button
            key="download"
            type="primary"
            icon={<DownloadOutlined />}
            onClick={() => {
              if (pdfOrderGuid) handleDownloadPdf(pdfOrderGuid)
            }}
          >
            {t('common.download')}
          </Button>,
          <Button
            key="close"
            onClick={() => {
              setPdfModalVisible(false)
              if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
              setPdfBlobUrl('')
              setPdfOrderGuid('')
            }}
          >
            {t('common.close')}
          </Button>,
        ]}
        width={900}
        centered
        destroyOnHidden
      >
        <div style={{ height: 600, overflow: 'auto' }}>
          {pdfLoading ? (
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                height: '100%',
              }}
            >
              <span>{t('common.loading')}</span>
            </div>
          ) : pdfBlobUrl ? (
            <iframe
              src={pdfBlobUrl}
              style={{ width: '100%', height: '100%', border: 'none' }}
              title={t('posmOrders.invoicePreview')}
            />
          ) : null}
        </div>
      </Modal>
    </Card>
  )
}
