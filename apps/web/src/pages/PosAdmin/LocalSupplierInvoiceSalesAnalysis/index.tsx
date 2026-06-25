import { ArrowLeftOutlined, CopyOutlined, ReloadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Descriptions, Empty, Image, Space, Table, Tag, Tooltip, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { getInvoiceSalesAnalysis } from '../../../services/localSupplierInvoiceService'
import type {
  LocalSupplierInvoiceSalesAnalysisItemDto,
  LocalSupplierInvoiceSalesAnalysisResponseDto,
} from '../../../types/localSupplierInvoice'

const { Text } = Typography
const DEFAULT_PRODUCT_IMAGE_BASE_URL = 'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200'
const PRODUCT_IMAGE_FALLBACK = 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs='
const DATA_COLORS = {
  quantity: '#1677ff',
  price: '#d46b08',
  amount: '#722ed1',
  sales30: '#389e0d',
  sales60: '#08979c',
  sales90: '#531dab',
  intervalTotal: '#c41d7f',
  interval30: '#5b8c00',
  interval60: '#0958d9',
  interval90: '#722ed1',
  date: '#595959',
  previousDate: '#1677ff',
  muted: '#8c8c8c',
}

// 商品图优先使用后端已有图片；没有时沿用商品管理页默认 COS 路径补图。
function buildDefaultProductImageUrl(itemNumber?: string | null, productCode?: string | null) {
  const imageKey = String(itemNumber || productCode || '').trim()
  return imageKey
    ? `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(imageKey)}.jpg`
    : ''
}

function formatDate(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD') : value
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return '--'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm:ss') : value
}

function formatNumber(value?: number | null, digits = 0) {
  if (value === undefined || value === null || !Number.isFinite(value)) {
    return '--'
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })
}

function formatMoney(value?: number | null) {
  return formatNumber(value, 2)
}

function compareNumber(valueA?: number | null, valueB?: number | null) {
  const hasValueA = typeof valueA === 'number' && Number.isFinite(valueA)
  const hasValueB = typeof valueB === 'number' && Number.isFinite(valueB)
  if (!hasValueA && !hasValueB) {
    return 0
  }
  if (!hasValueA) {
    return 1
  }
  if (!hasValueB) {
    return -1
  }
  return valueA - valueB
}

function renderColorNumber(value?: number | null, color = DATA_COLORS.quantity, digits = 0) {
  const normalized = typeof value === 'number' && Number.isFinite(value) ? value : null
  return (
    <Text
      style={{
        color: normalized === null || normalized === 0 ? DATA_COLORS.muted : color,
        fontWeight: normalized && normalized > 0 ? 600 : 400,
        fontVariantNumeric: 'tabular-nums',
      }}
    >
      {formatNumber(value, digits)}
    </Text>
  )
}

function renderColorMoney(value?: number | null, color = DATA_COLORS.price) {
  const normalized = typeof value === 'number' && Number.isFinite(value) ? value : null
  return (
    <Text
      style={{
        color: normalized === null ? DATA_COLORS.muted : color,
        fontWeight: normalized && normalized > 0 ? 600 : 400,
        fontVariantNumeric: 'tabular-nums',
      }}
    >
      {formatMoney(value)}
    </Text>
  )
}

function renderColorDate(value?: string | null, color = DATA_COLORS.date) {
  return (
    <Text style={{ color: value ? color : DATA_COLORS.muted }}>
      {formatDate(value)}
    </Text>
  )
}

function formatStore(result: LocalSupplierInvoiceSalesAnalysisResponseDto | null) {
  if (!result?.storeCode) {
    return '--'
  }
  return result.storeName ? `${result.storeCode} - ${result.storeName}` : result.storeCode
}

function formatSupplier(result: LocalSupplierInvoiceSalesAnalysisResponseDto | null) {
  if (!result?.supplierCode) {
    return '--'
  }
  return result.supplierName ? `${result.supplierCode} - ${result.supplierName}` : result.supplierCode
}

export default function LocalSupplierInvoiceSalesAnalysisPage() {
  const { t } = useTranslation()
  const route = useStableRouteContext()
  const navigate = useNavigate()
  const { active } = useKeepAliveContext()
  const invoiceGuid = route?.params.id

  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<LocalSupplierInvoiceSalesAnalysisResponseDto | null>(null)

  const loadData = useCallback(
    async (signal?: AbortSignal) => {
      if (!invoiceGuid) {
        setResult(null)
        return
      }

      setLoading(true)
      try {
        const data = await getInvoiceSalesAnalysis(invoiceGuid, signal)
        setResult(data)
      } catch (error) {
        if (!signal?.aborted) {
          message.error(
            error instanceof Error
              ? error.message
              : t('posAdmin.localSupplierInvoiceSalesAnalysis.loadFailed'),
          )
        }
      } finally {
        if (!signal?.aborted) {
          setLoading(false)
        }
      }
    },
    [invoiceGuid, t],
  )

  useEffect(() => {
    if (!active) {
      return undefined
    }

    const controller = new AbortController()
    void loadData(controller.signal)
    return () => controller.abort()
  }, [active, loadData])

  const copyItemNumber = useCallback(
    async (value?: string | null) => {
      const text = String(value ?? '').trim()
      if (!text) {
        return
      }

      try {
        await navigator.clipboard.writeText(text)
        message.success(t('common.copySuccess'))
      } catch {
        message.error(t('common.copyFailed'))
      }
    },
    [t],
  )

  const columns = useMemo<ColumnsType<LocalSupplierInvoiceSalesAnalysisItemDto>>(
    () => [
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.product'),
        key: 'product',
        width: 340,
        fixed: 'left',
        render: (_value, record) => {
          const imageUrl =
            record.productImage || buildDefaultProductImageUrl(record.itemNumber, record.productCode)
          return (
            <Space align="start" size={8}>
              <Image
                src={imageUrl || PRODUCT_IMAGE_FALLBACK}
                width={48}
                height={48}
                style={{ objectFit: 'contain', borderRadius: 4, border: '1px solid #f0f0f0' }}
                preview={imageUrl ? { mask: '' } : false}
                fallback={PRODUCT_IMAGE_FALLBACK}
              />
              <Space direction="vertical" size={0}>
                <Text strong>{record.productName || '--'}</Text>
                <Space size={4} wrap>
                  <Text type="secondary">
                    {t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.itemNumber')}: {record.itemNumber || '--'}
                  </Text>
                  {record.itemNumber && (
                    <Tooltip title={t('posAdmin.localSupplierInvoiceSalesAnalysis.copyItemNumber')}>
                      <Button
                        type="text"
                        size="small"
                        icon={<CopyOutlined />}
                        aria-label={t('posAdmin.localSupplierInvoiceSalesAnalysis.copyItemNumber')}
                        onClick={(event) => {
                          event.stopPropagation()
                          void copyItemNumber(record.itemNumber)
                        }}
                      />
                    </Tooltip>
                  )}
                </Space>
                <Text type="secondary">
                  {t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.barcode')}: {record.barcode || '--'}
                </Text>
              </Space>
            </Space>
          )
        },
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.quantity'),
        dataIndex: 'quantity',
        width: 110,
        align: 'right',
        render: (value: number | null) => renderColorNumber(value, DATA_COLORS.quantity, 2),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.purchasePrice'),
        dataIndex: 'purchasePrice',
        width: 100,
        align: 'right',
        render: (value: number | null) => renderColorMoney(value, DATA_COLORS.price),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.retailPrice'),
        dataIndex: 'retailPrice',
        width: 100,
        align: 'right',
        render: (value: number | null) => renderColorMoney(value, DATA_COLORS.price),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.amount'),
        dataIndex: 'amount',
        width: 110,
        align: 'right',
        render: (value: number | null) => renderColorMoney(value, DATA_COLORS.amount),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.afterPurchase30'),
        dataIndex: 'salesQty30',
        width: 110,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesQty30, b.salesQty30),
        render: (value: number) => renderColorNumber(value, DATA_COLORS.sales30),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.afterPurchase60'),
        dataIndex: 'salesQty60',
        width: 110,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesQty60, b.salesQty60),
        render: (value: number) => renderColorNumber(value, DATA_COLORS.sales60),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.afterPurchase90'),
        dataIndex: 'salesQty90',
        width: 110,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesQty90, b.salesQty90),
        render: (value: number) => renderColorNumber(value, DATA_COLORS.sales90),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.previousPurchase'),
        dataIndex: 'previousPurchaseDate',
        width: 120,
        render: (value: string | null) =>
          value ? (
            renderColorDate(value, DATA_COLORS.previousDate)
          ) : (
            <Tag color="default">
              {t('posAdmin.localSupplierInvoiceSalesAnalysis.noPreviousPurchase')}
            </Tag>
          ),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.daysBetween'),
        dataIndex: 'previousToCurrentDays',
        width: 100,
        align: 'right',
        render: (value: number | null) => renderColorNumber(value, DATA_COLORS.date),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.intervalSales'),
        dataIndex: 'salesSincePreviousPurchase',
        width: 100,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesSincePreviousPurchase, b.salesSincePreviousPurchase),
        render: (value: number | null) => renderColorNumber(value, DATA_COLORS.intervalTotal),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.interval30'),
        dataIndex: 'salesSincePreviousPurchase30',
        width: 100,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesSincePreviousPurchase30, b.salesSincePreviousPurchase30),
        render: (value: number | null) => renderColorNumber(value, DATA_COLORS.interval30),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.interval60'),
        dataIndex: 'salesSincePreviousPurchase60',
        width: 100,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesSincePreviousPurchase60, b.salesSincePreviousPurchase60),
        render: (value: number | null) => renderColorNumber(value, DATA_COLORS.interval60),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.interval90'),
        dataIndex: 'salesSincePreviousPurchase90',
        width: 100,
        align: 'right',
        sorter: (a, b) => compareNumber(a.salesSincePreviousPurchase90, b.salesSincePreviousPurchase90),
        render: (value: number | null) => renderColorNumber(value, DATA_COLORS.interval90),
      },
      {
        title: t('posAdmin.localSupplierInvoiceSalesAnalysis.columns.updatedAt'),
        dataIndex: 'salesStatisticLastUpdate',
        width: 160,
        render: (value: string | null) => (
          <Text style={{ color: value ? DATA_COLORS.date : DATA_COLORS.muted }}>
            {formatDateTime(value)}
          </Text>
        ),
      },
    ],
    [copyItemNumber, t],
  )

  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      <Card
        size="small"
        title={t('posAdmin.localSupplierInvoiceSalesAnalysis.title')}
        loading={loading && !result}
        extra={
          <Space>
            <Button
              icon={<ArrowLeftOutlined />}
              onClick={() =>
                navigate(
                  invoiceGuid
                    ? `/pos-admin/invoice-detail/${invoiceGuid}`
                    : '/pos-admin/local-supplier-invoices',
                )
              }
            >
              {t('posAdmin.localSupplierInvoiceSalesAnalysis.back')}
            </Button>
            <Button icon={<ReloadOutlined />} loading={loading} onClick={() => void loadData()}>
              {t('posAdmin.localSupplierInvoiceSalesAnalysis.refresh')}
            </Button>
          </Space>
        }
      >
        <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.invoiceNo')}>
            {result?.invoiceNo || '--'}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.store')}>
            {formatStore(result)}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.supplier')}>
            {formatSupplier(result)}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.analysisDate')}>
            {formatDate(result?.analysisDate)}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.orderDate')}>
            {formatDate(result?.orderDate)}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.inboundDate')}>
            {formatDate(result?.inboundDate)}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.productCount')}>
            {formatNumber(result?.items.length)}
          </Descriptions.Item>
          <Descriptions.Item label={t('posAdmin.localSupplierInvoiceSalesAnalysis.fields.updatedAt')}>
            {formatDateTime(result?.salesStatisticLastUpdate)}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      {result && (
        <Alert
          type="info"
          showIcon
          message={t('posAdmin.localSupplierInvoiceSalesAnalysis.calculationNote')}
        />
      )}

      <Card
        size="small"
        title={t('posAdmin.localSupplierInvoiceSalesAnalysis.detailsTitle', {
          count: result?.items.length ?? 0,
        })}
        styles={{ body: { padding: 0 } }}
      >
        <Table
          rowKey="detailGUID"
          loading={loading}
          dataSource={result?.items ?? []}
          columns={columns}
          pagination={false}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
          scroll={{ x: 1900, y: 'calc(100vh - 360px)' }}
          size="small"
        />
      </Card>
    </Space>
  )
}
