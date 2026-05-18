import {
  DownloadOutlined,
  FilePdfOutlined,
  ReloadOutlined,
  ShopOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Image,
  Input,
  Modal,
  Pagination,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../../store/auth'
import { getActiveStores } from '../../services/storeService'
import {
  fetchTaxInvoicePdf,
  getSalesOrderDetail,
  getSalesOrderList,
  getTaxInvoicePdfUrl,
} from '../../services/posmSalesOrderService'
import type { PosmSalesOrder, PosmSalesOrderDetailResponse } from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'

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
  const [filterDateRange, setFilterDateRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>([
    dayjs(),
    dayjs(),
  ])

  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([])
  const [detailData, setDetailData] = useState<Record<string, PosmSalesOrderDetailResponse>>({})

  const [pdfModalVisible, setPdfModalVisible] = useState(false)
  const [pdfBlobUrl, setPdfBlobUrl] = useState('')
  const [pdfLoading, setPdfLoading] = useState(false)
  const [pdfOrderGuid, setPdfOrderGuid] = useState('')

  const wrapRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
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

  const loadData = async () => {
    setLoading(true)
    try {
      const result = await getSalesOrderList({
        startDate:
          filterDateRange?.[0]?.format('YYYY-MM-DD') || dayjs().format('YYYY-MM-DD'),
        endDate:
          filterDateRange?.[1]?.format('YYYY-MM-DD') || dayjs().format('YYYY-MM-DD'),
        branchCode: filterBranchCode || undefined,
        orderType: filterOrderType,
        keyword: filterKeyword || undefined,
        pageNumber: page,
        pageSize,
      })
      setData(result?.items ?? [])
      setTotal(result?.total ?? 0)
    } catch {
      message.error(t('posmOrders.loadFailed'))
    } finally {
      setLoading(false)
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
    setPage(1)
    void loadData()
  }

  const handleReset = () => {
    setFilterBranchCode('')
    setFilterOrderType(OrderType.All)
    setFilterKeyword('')
    setFilterDateRange([dayjs(), dayjs()])
    setPage(1)
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

  const columns: ColumnsType<PosmSalesOrder> = [
    {
      title: t('posmOrders.serialNo'),
      width: 60,
      align: 'right',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('posmOrders.orderNo'),
      dataIndex: 'orderGuid',
      width: 120,
      render: (_, record) => (
        <Text ellipsis={{ tooltip: record.orderGuid }}>{record.orderGuid?.slice(-6) || '-'}</Text>
      ),
    },
    {
      title: t('posmOrders.branch'),
      dataIndex: 'branchName',
      width: 140,
      render: (_, record) => (
        <Tag icon={<ShopOutlined />} color={getBranchColor(record.branchCode)}>
          {record.branchName || '-'}
        </Tag>
      ),
    },
    {
      title: t('posmOrders.device'),
      dataIndex: 'deviceCode',
      width: 110,
      render: (value) => value || '-',
    },
    {
      title: t('posmOrders.date'),
      dataIndex: 'orderTime',
      width: 110,
      render: (_, record) =>
        record.orderTime ? dayjs(record.orderTime).format('YYYY-MM-DD') : '-',
    },
    {
      title: t('posmOrders.time'),
      dataIndex: 'orderTime',
      width: 90,
      render: (_, record) =>
        record.orderTime ? dayjs(record.orderTime).format('HH:mm:ss') : '-',
    },
    {
      title: t('posmOrders.skuCount'),
      dataIndex: 'skuCount',
      width: 80,
      align: 'right',
    },
    {
      title: t('posmOrders.itemCount'),
      dataIndex: 'itemCount',
      width: 80,
      align: 'right',
    },
    {
      title: t('posmOrders.totalAmount'),
      dataIndex: 'totalAmount',
      width: 110,
      align: 'right',
      render: (_, record) => <Text>${(record.totalAmount || 0).toFixed(2)}</Text>,
    },
    {
      title: t('posmOrders.discount'),
      dataIndex: 'discountAmount',
      width: 100,
      align: 'right',
      render: (_, record) => (
        <Text type="secondary">${(record.discountAmount || 0).toFixed(2)}</Text>
      ),
    },
    {
      title: t('posmOrders.actualPay'),
      dataIndex: 'actualAmount',
      width: 110,
      align: 'right',
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
          <Button icon={<ReloadOutlined />} onClick={handleSearch}>
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
          onChange={(dates) =>
            setFilterDateRange(dates?.[0] && dates?.[1] ? [dates[0], dates[1]] : null)
          }
          format="YYYY-MM-DD"
          style={{ width: 260 }}
        />
        <Select
          placeholder={t('posmOrders.branch')}
          value={filterBranchCode || undefined}
          onChange={setFilterBranchCode}
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
                              {payment.paymentTime
                                ? dayjs(payment.paymentTime).format('HH:mm:ss')
                                : '-'}
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
        destroyOnClose
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
