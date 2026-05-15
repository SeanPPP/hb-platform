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
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getActiveStores } from '../../services/storeService'
import { getSalesOrderDetail, getSalesOrderList, getTaxInvoicePdfUrl, fetchTaxInvoicePdf } from '../../services/posmSalesOrderService'
import type { PosmSalesOrder, PosmSalesOrderDetailResponse } from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'

const { Text } = Typography

const BRANCH_COLORS = [
  'blue', 'green', 'orange', 'red', 'cyan', 'purple',
  'magenta', 'lime', 'gold', 'volcano', 'geekblue',
]

function getBranchColor(branchCode?: string): string {
  if (!branchCode) return 'default'
  let hash = 0
  for (let i = 0; i < branchCode.length; i++) {
    const char = branchCode.charCodeAt(i)
    hash = (hash << 5) - hash + char
    hash = hash & hash
  }
  return BRANCH_COLORS[Math.abs(hash) % BRANCH_COLORS.length]
}

function getOrderTypeOptions(t: (key: string, fb: string) => string) {
  return [
    { label: t('posmOrders.all', '全部'), value: OrderType.All },
    { label: t('posmOrders.pending', '待付款'), value: OrderType.Pending },
    { label: t('posmOrders.paid', '已付款'), value: OrderType.Paid },
    { label: t('posmOrders.cancelled', '已取消'), value: OrderType.Cancelled },
    { label: t('posmOrders.refunded', '已退款'), value: OrderType.Refunded },
    { label: t('posmOrders.installment', '分期'), value: OrderType.Installment },
  ]
}

export default function PosmSalesOrdersPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<PosmSalesOrder[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [stores, setStores] = useState<{ label: string; value: string }[]>([])

  const [filterBranchCode, setFilterBranchCode] = useState('')
  const [filterOrderType, setFilterOrderType] = useState<OrderType>(OrderType.All)
  const [filterKeyword, setFilterKeyword] = useState('')
  const [filterDateRange, setFilterDateRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>([dayjs(), dayjs()])

  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([])
  const [detailData, setDetailData] = useState<Record<string, PosmSalesOrderDetailResponse>>({})

  const [pdfModalVisible, setPdfModalVisible] = useState(false)
  const [pdfBlobUrl, setPdfBlobUrl] = useState('')
  const [pdfLoading, setPdfLoading] = useState(false)
  const [pdfOrderGuid, setPdfOrderGuid] = useState('')

  const wrapRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)

  const loadData = async () => {
    setLoading(true)
    try {
      const result = await getSalesOrderList({
        startDate: filterDateRange?.[0]?.format('YYYY-MM-DD') || dayjs().format('YYYY-MM-DD'),
        endDate: filterDateRange?.[1]?.format('YYYY-MM-DD') || dayjs().format('YYYY-MM-DD'),
        branchCode: filterBranchCode || undefined,
        orderType: filterOrderType,
        keyword: filterKeyword || undefined,
        pageNumber: page,
        pageSize,
      })
      setData(result?.items ?? [])
      setTotal(result?.total ?? 0)
    } catch {
      message.error(t('posmOrders.loadFailed', '加载订单列表失败'))
    } finally {
      setLoading(false)
    }
  }

  const loadStores = async () => {
    try {
      const storeOptions = await getActiveStores()
      setStores(storeOptions)
    } catch {
      // ignore
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
    loadStores()
  }, [])

  useEffect(() => {
    loadData()
  }, [page, pageSize])

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
    loadData()
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
      message.error(t('posmOrders.getInvoiceFailed', '获取发票失败'))
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
      title: t('posmOrders.serialNo', '序号'),
      width: 60,
      align: 'right',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('posmOrders.orderNo', '订单号'),
      dataIndex: 'orderGuid',
      width: 120,
      render: (_, record) => <Text ellipsis={{ tooltip: record.orderGuid }}>{record.orderGuid?.slice(-6) || '-'}</Text>,
    },
    {
      title: t('posmOrders.branch', '分店'),
      dataIndex: 'branchName',
      width: 120,
      render: (_, record) => (
        <Tag icon={<ShopOutlined />} color={getBranchColor(record.branchCode)}>
          {record.branchName || '-'}
        </Tag>
      ),
    },
    {
      title: t('posmOrders.device', '设备'),
      dataIndex: 'deviceCode',
      width: 100,
    },
    {
      title: t('posmOrders.date', '日期'),
      dataIndex: 'orderTime',
      width: 110,
      render: (_, record) => (record.orderTime ? dayjs(record.orderTime).format('YYYY-MM-DD') : '-'),
    },
    {
      title: t('posmOrders.time', '时间'),
      dataIndex: 'orderTime',
      width: 90,
      render: (_, record) => (record.orderTime ? dayjs(record.orderTime).format('HH:mm:ss') : '-'),
    },
    {
      title: t('posmOrders.skuCount', 'SKU数'),
      dataIndex: 'skuCount',
      width: 80,
      align: 'right',
    },
    {
      title: t('posmOrders.itemCount', '件数'),
      dataIndex: 'itemCount',
      width: 80,
      align: 'right',
    },
    {
      title: t('posmOrders.totalAmount', '总金额'),
      dataIndex: 'totalAmount',
      width: 110,
      align: 'right',
      render: (_, record) => <Text>${(record.totalAmount || 0).toFixed(2)}</Text>,
    },
    {
      title: t('posmOrders.discount', '折扣'),
      dataIndex: 'discountAmount',
      width: 100,
      align: 'right',
      render: (_, record) => <Text type="secondary">${(record.discountAmount || 0).toFixed(2)}</Text>,
    },
    {
      title: t('posmOrders.actualPay', '实付'),
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
      title: t('common.action', '操作'),
      key: 'action',
      width: 100,
      render: (_, record) => (
        <Space size="small">
          <Button type="text" icon={<FilePdfOutlined />} size="small" onClick={() => handlePreviewPdf(record.orderGuid || '')} title={t('posmOrders.previewInvoice', '预览发票')} />
          <Button type="text" icon={<DownloadOutlined />} size="small" onClick={() => handleDownloadPdf(record.orderGuid || '')} title={t('posmOrders.downloadInvoice', '下载发票')} />
        </Space>
      ),
    },
  ]

  return (
    <Card
      title={t('posmOrders.cashierRecords', '收银记录')}
      extra={
        <Space>
          <Button icon={<ReloadOutlined />} onClick={handleSearch}>
            {t('common.refresh', '刷新')}
          </Button>
        </Space>
      }
      styles={{ body: { padding: 0 } }}
    >
      <div style={{ padding: '12px 16px', display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
        <DatePicker.RangePicker
          value={filterDateRange}
          onChange={(dates) => setFilterDateRange(dates?.[0] && dates?.[1] ? [dates[0], dates[1]] : null)}
          format="YYYY-MM-DD"
          style={{ width: 260 }}
        />
        <Select
          placeholder={t('posmOrders.branch', '分店')}
          value={filterBranchCode || undefined}
          onChange={setFilterBranchCode}
          style={{ width: 180 }}
          allowClear
          showSearch
          filterOption={(input, option) => (option?.label ?? '').toLowerCase().includes(input.toLowerCase())}
          options={stores}
        />
        <Select
          placeholder={t('posmOrders.orderType', '订单类型')}
          value={filterOrderType}
          onChange={(v: OrderType) => setFilterOrderType(v)}
          style={{ width: 130 }}
          options={getOrderTypeOptions(t)}
        />
        <Input
          placeholder={t('posmOrders.keyword', '关键词')}
          value={filterKeyword}
          onChange={(e) => setFilterKeyword(e.target.value)}
          onPressEnter={handleSearch}
          allowClear
          style={{ width: 180 }}
        />
        <Button type="primary" onClick={handleSearch}>
          {t('common.query', '查询')}
        </Button>
        <Button onClick={handleReset}>
          {t('common.reset', '重置')}
        </Button>
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
                if (!detail) return <div>{t('common.loading', '加载中...')}</div>

                return (
                  <div style={{ padding: 16 }}>
                    <Card title={t('posmOrders.orderDetail', '订单明细')} size="small" style={{ marginBottom: 16 }}>
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
                            <Image src={item.productImage} alt={item.productName} width={50} height={50} style={{ objectFit: 'cover' }} />
                          )}
                          <div style={{ flex: 1 }}>
                            <div style={{ marginBottom: 4 }}><Text strong>{item.productName}</Text></div>
                            <div style={{ fontSize: 12 }}>
                              <Space size="large">
                                <span>{t('posmOrders.quantity', '数量')}: {item.quantity}</span>
                                <span>{t('posmOrders.unitPrice', '单价')}: ${(item.unitPrice || 0).toFixed(2)}</span>
                                <span>{t('posmOrders.discount', '折扣')}: ${(item.discountAmount || 0).toFixed(2)}</span>
                                <Text type="danger" strong>{t('posmOrders.subtotal', '小计')}: ${(item.actualAmount || 0).toFixed(2)}</Text>
                              </Space>
                            </div>
                          </div>
                        </div>
                      ))}
                    </Card>

                    {detail.paymentDetails && detail.paymentDetails.length > 0 && (
                      <Card title={t('posmOrders.paymentInfo', '支付信息')} size="small">
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
                            <Text>{payment.paymentTime ? dayjs(payment.paymentTime).format('HH:mm:ss') : '-'}</Text>
                            <Tag color="green">{payment.paymentMethodName || t('posmOrders.payment', '支付')}</Tag>
                            <Text type="success" strong>${(payment.amount || 0).toFixed(2)}</Text>
                          </div>
                        ))}
                      </Card>
                    )}
                  </div>
                )
              },
              onExpand: (expanded, record) => {
                if (expanded) loadDetail(record)
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
        title={t('posmOrders.invoicePreview', '发票预览')}
        open={pdfModalVisible}
        onCancel={() => {
          setPdfModalVisible(false)
          if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
          setPdfBlobUrl('')
          setPdfOrderGuid('')
        }}
        footer={[
          <Button key="download" type="primary" icon={<DownloadOutlined />} onClick={() => { if (pdfOrderGuid) handleDownloadPdf(pdfOrderGuid) }}>
            {t('common.download', '下载')}
          </Button>,
          <Button key="close" onClick={() => {
            setPdfModalVisible(false)
            if (pdfBlobUrl) URL.revokeObjectURL(pdfBlobUrl)
            setPdfBlobUrl('')
            setPdfOrderGuid('')
          }}>
            {t('common.close', '关闭')}
          </Button>,
        ]}
        width={900}
        centered
        destroyOnClose
      >
        <div style={{ height: 600, overflow: 'auto' }}>
          {pdfLoading ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
              <span>{t('common.loading', '加载中...')}</span>
            </div>
          ) : pdfBlobUrl ? (
            <iframe src={pdfBlobUrl} style={{ width: '100%', height: '100%', border: 'none' }} title="Tax Invoice PDF Preview" />
          ) : null}
        </div>
      </Modal>
    </Card>
  )
}
