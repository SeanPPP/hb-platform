import {
  EyeOutlined,
  ReloadOutlined,
  SearchOutlined,
  ToolOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Descriptions,
  Drawer,
  Form,
  Input,
  Row,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { RangePickerProps } from 'antd/es/date-picker'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import {
  getOperationAuditDetail,
  getOperationAudits,
} from '../../../services/operationAuditService'
import { getActiveStores, type StoreOption } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import type {
  OperationAuditDetail,
  OperationAuditDetailItem,
  OperationAuditListItem,
  OperationAuditOutcome,
} from '../../../types/operationAudit'
import {
  buildStoreOptionsFromUserStores,
  filterStoreOptionsByManagedCodes,
} from '../../../utils/managedStoreScope'
import {
  OPERATION_TYPE_KEYS,
  buildOperationAuditQuery,
  buildSystemLogLink,
  formatMoney,
  formatSignedMoney,
  summarizeProducts,
} from './operationLogsLogic'

interface OperationAuditFormValues {
  timeRange: [Dayjs, Dayjs]
  storeCode?: string
  cashierKeyword?: string
  deviceCode?: string
  operationType?: string
  outcome?: string
  productKeyword?: string
  orderGuid?: string
  keyword?: string
}

const DEFAULT_PAGE_SIZE = 20

function getDefaultTimeRange(): [Dayjs, Dayjs] {
  return [dayjs().subtract(7, 'day'), dayjs()]
}

function getOutcomeColor(outcome: OperationAuditOutcome) {
  switch (outcome) {
    case 'Succeeded':
      return 'success'
    case 'Denied':
      return 'warning'
    case 'Failed':
      return 'error'
    default:
      return 'default'
  }
}

function formatValue(value: unknown) {
  if (value === null || value === undefined || value === '') {
    return '-'
  }
  return String(value)
}

function formatSafeProperties(value?: string) {
  if (!value) {
    return '-'
  }

  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

export default function PosAdminOperationLogsPage() {
  const { t } = useTranslation()
  const [form] = Form.useForm<OperationAuditFormValues>()
  const access = useAuthStore((state) => state.access)
  const currentUser = useAuthStore((state) => state.currentUser)
  const managedStoreCodes = access.managedStoreCodes?.()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'
  const [storeOptions, setStoreOptions] = useState<StoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState(false)
  const [data, setData] = useState<OperationAuditListItem[]>([])
  const [total, setTotal] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailRecord, setDetailRecord] = useState<OperationAuditDetail | null>(null)

  const visibleStoreOptions = useMemo(
    () => filterStoreOptionsByManagedCodes(storeOptions, managedStoreCodes),
    [managedStoreCodes, storeOptions],
  )

  useEffect(() => {
    if (managedStoreCodes !== null) {
      // 店长直接使用当前会话已授权的可管理门店，避免依赖 Stores.View 全店列表权限。
      setStoreOptions(buildStoreOptionsFromUserStores(currentUser?.stores, { manageableOnly: true }))
      return
    }

    let disposed = false
    void getActiveStores()
      .then((stores) => {
        if (!disposed) {
          setStoreOptions(stores)
        }
      })
      .catch((error) => {
        console.error(error)
        if (!disposed) {
          message.error(t('operationLogs.loadStoresFailed'))
        }
      })
    return () => {
      disposed = true
    }
  }, [currentUser?.stores, managedStoreCodeKey, t])

  const loadData = useCallback(
    async (nextPage = pageNumber, nextPageSize = pageSize) => {
      const values = form.getFieldsValue()
      const [from, to] = values.timeRange ?? getDefaultTimeRange()
      setLoading(true)
      setLoadError(false)
      try {
        // 查询范围最终仍由服务端按当前用户可管理门店强制收窄。
        const result = await getOperationAudits(
          buildOperationAuditQuery({
            startUtc: from.toISOString(),
            endUtc: to.toISOString(),
            storeCode: values.storeCode ?? '',
            cashierKeyword: values.cashierKeyword ?? '',
            deviceCode: values.deviceCode ?? '',
            operationType: values.operationType ?? '',
            outcome: values.outcome ?? '',
            productKeyword: values.productKeyword ?? '',
            orderGuid: values.orderGuid ?? '',
            keyword: values.keyword ?? '',
            page: nextPage,
            pageSize: nextPageSize,
          }),
        )
        setData(result.items)
        setTotal(result.total)
        setPageNumber(result.pageNumber)
        setPageSize(result.pageSize)
      } catch (error) {
        console.error(error)
        setLoadError(true)
        message.error(t('operationLogs.loadFailed'))
      } finally {
        setLoading(false)
      }
    },
    [form, pageNumber, pageSize, t],
  )

  useEffect(() => {
    form.setFieldsValue({ timeRange: getDefaultTimeRange() })
    void loadData(1, DEFAULT_PAGE_SIZE)
    // 首次加载只执行一次，后续查询由用户或分页动作触发。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleReset = () => {
    form.resetFields()
    form.setFieldsValue({ timeRange: getDefaultTimeRange() })
    void loadData(1, DEFAULT_PAGE_SIZE)
  }

  const handleOpenDetail = async (record: OperationAuditListItem) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailRecord(null)
    try {
      setDetailRecord(await getOperationAuditDetail(record.eventId))
    } catch (error) {
      console.error(error)
      message.error(t('operationLogs.loadDetailFailed'))
    } finally {
      setDetailLoading(false)
    }
  }

  const operationLabel = useCallback(
    (operationType: string) => {
      const key = OPERATION_TYPE_KEYS[operationType]
      return key ? t(key) : operationType
    },
    [t],
  )

  const outcomeLabel = useCallback(
    (outcome: OperationAuditOutcome) => t(`operationLogs.outcomes.${outcome.toLowerCase()}`),
    [t],
  )

  const columns = useMemo<ColumnsType<OperationAuditListItem>>(
    () => [
      {
        title: t('operationLogs.columns.time'),
        dataIndex: 'occurredAtUtc',
        width: 175,
        render: (value: string) => dayjs(value).format('YYYY-MM-DD HH:mm:ss'),
      },
      {
        title: t('operationLogs.columns.store'),
        dataIndex: 'storeCode',
        width: 100,
      },
      {
        title: t('operationLogs.columns.employee'),
        key: 'employee',
        width: 150,
        ellipsis: true,
        render: (_, record) => record.cashierName || record.cashierId || record.userGuid || '-',
      },
      {
        title: t('operationLogs.columns.operation'),
        dataIndex: 'operationType',
        width: 185,
        render: (value: string) => operationLabel(value),
      },
      {
        title: t('operationLogs.columns.products'),
        key: 'products',
        minWidth: 180,
        ellipsis: true,
        render: (_, record) => summarizeProducts(record, t('operationLogs.detail.productFallback')),
      },
      {
        title: t('operationLogs.columns.amountChange'),
        key: 'amountDelta',
        width: 120,
        align: 'right',
        render: (_, record) => formatSignedMoney(record.amountDelta, record.currencyCode || 'AUD'),
      },
      {
        title: t('operationLogs.columns.device'),
        dataIndex: 'deviceCode',
        width: 125,
        ellipsis: true,
      },
      {
        title: t('operationLogs.columns.outcome'),
        dataIndex: 'outcome',
        width: 105,
        render: (value: OperationAuditOutcome) => (
          <Tag color={getOutcomeColor(value)}>{outcomeLabel(value)}</Tag>
        ),
      },
      {
        title: t('common.action'),
        key: 'actions',
        width: 90,
        fixed: 'right',
        render: (_, record) => (
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleOpenDetail(record)}>
            {t('common.view')}
          </Button>
        ),
      },
    ],
    [operationLabel, outcomeLabel, t],
  )

  const itemColumns = useMemo<ColumnsType<OperationAuditDetailItem>>(
    () => [
      {
        title: t('operationLogs.detail.product'),
        key: 'product',
        width: 230,
        render: (_, item) => (
          <Space direction="vertical" size={0}>
            <Typography.Text>{item.displayName || item.productCode || '-'}</Typography.Text>
            <Typography.Text type="secondary">
              {[
                item.productCode,
                item.itemNumber ? `${t('operationLogs.detail.itemNumber')}: ${item.itemNumber}` : null,
                item.referenceCode,
                item.lookupCode,
                item.lineKind ? `${t('operationLogs.detail.lineKind')}: ${item.lineKind}` : null,
              ].filter(Boolean).join(' / ') || '-'}
            </Typography.Text>
          </Space>
        ),
      },
      {
        title: t('operationLogs.detail.quantity'),
        key: 'quantity',
        width: 165,
        render: (_, item) => `${formatValue(item.beforeQuantity)} → ${formatValue(item.afterQuantity)} (${formatValue(item.quantityDelta)})`,
      },
      {
        title: t('operationLogs.detail.unitPrice'),
        key: 'unitPrice',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeUnitPrice, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterUnitPrice, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.unitPriceDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
      {
        title: t('operationLogs.detail.discount'),
        key: 'discount',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeDiscountAmount, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterDiscountAmount, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.discountAmountDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
      {
        title: t('operationLogs.detail.gross'),
        key: 'gross',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeGrossAmount, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterGrossAmount, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.grossAmountDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
      {
        title: t('operationLogs.detail.actual'),
        key: 'actual',
        width: 190,
        render: (_, item) => `${formatMoney(item.beforeActualAmount, detailRecord?.currencyCode || 'AUD')} → ${formatMoney(item.afterActualAmount, detailRecord?.currencyCode || 'AUD')} (${formatSignedMoney(item.actualAmountDelta, detailRecord?.currencyCode || 'AUD')})`,
      },
    ],
    [detailRecord?.currencyCode, t],
  )

  const timeRangePresets: RangePickerProps['presets'] = [
    { label: t('operationLogs.presets.today'), value: [dayjs().startOf('day'), dayjs()] },
    { label: t('operationLogs.presets.last7Days'), value: [dayjs().subtract(7, 'day'), dayjs()] },
    { label: t('operationLogs.presets.last30Days'), value: [dayjs().subtract(30, 'day'), dayjs()] },
  ]

  return (
    <PageContainer
      title={t('operationLogs.pageTitle')}
      subtitle={t('operationLogs.pageSubtitle')}
      extra={
        <Button icon={<ReloadOutlined />} onClick={() => void loadData(pageNumber, pageSize)}>
          {t('common.refresh')}
        </Button>
      }
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Card>
          <Form form={form} layout="vertical" onFinish={() => void loadData(1, pageSize)}>
            <Row gutter={16}>
              <Col xs={24} lg={8}>
                <Form.Item label={t('operationLogs.filters.timeRange')} name="timeRange">
                  <DatePicker.RangePicker
                    showTime
                    presets={timeRangePresets}
                    style={{ width: '100%' }}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.store')} name="storeCode">
                  <Select
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    options={visibleStoreOptions}
                    placeholder={t('operationLogs.filters.storePlaceholder')}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.employee')} name="cashierKeyword">
                  <Input allowClear placeholder={t('operationLogs.filters.employeePlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.device')} name="deviceCode">
                  <Input allowClear placeholder={t('operationLogs.filters.devicePlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={4}>
                <Form.Item label={t('operationLogs.filters.outcome')} name="outcome">
                  <Select
                    allowClear
                    options={(['Succeeded', 'Denied', 'Failed'] as OperationAuditOutcome[]).map((value) => ({
                      value,
                      label: outcomeLabel(value),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.operation')} name="operationType">
                  <Select
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    options={Object.keys(OPERATION_TYPE_KEYS).map((value) => ({
                      value,
                      label: operationLabel(value),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.product')} name="productKeyword">
                  <Input allowClear placeholder={t('operationLogs.filters.productPlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.order')} name="orderGuid">
                  <Input allowClear placeholder={t('operationLogs.filters.orderPlaceholder')} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Form.Item label={t('operationLogs.filters.keyword')} name="keyword">
                  <Input allowClear placeholder={t('operationLogs.filters.keywordPlaceholder')} />
                </Form.Item>
              </Col>
            </Row>
            <Space>
              <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
                {t('common.query')}
              </Button>
              <Button onClick={handleReset}>{t('common.reset')}</Button>
            </Space>
          </Form>
        </Card>

        {loadError ? (
          <Alert
            type="error"
            showIcon
            message={t('operationLogs.loadFailed')}
            action={<Button onClick={() => void loadData(pageNumber, pageSize)}>{t('operationLogs.retry')}</Button>}
          />
        ) : null}

        <Card>
          <Table<OperationAuditListItem>
            rowKey="eventId"
            loading={loading}
            columns={columns}
            dataSource={data}
            scroll={{ x: 1280 }}
            locale={{ emptyText: t('operationLogs.empty') }}
            pagination={{
              current: pageNumber,
              pageSize,
              total,
              showSizeChanger: true,
              pageSizeOptions: [20, 50, 100, 200],
              showTotal: (value) => t('operationLogs.paginationTotal', { total: value }),
              onChange: (nextPage, nextPageSize) => void loadData(nextPage, nextPageSize),
            }}
          />
        </Card>
      </Space>

      <Drawer
        title={t('operationLogs.detailTitle')}
        width={960}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailRecord(null)
        }}
        destroyOnHidden
      >
        {detailLoading ? <Typography.Text type="secondary">{t('operationLogs.loadingDetail')}</Typography.Text> : null}
        {detailRecord ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2} size="small">
              <Descriptions.Item label={t('operationLogs.columns.time')}>
                {dayjs(detailRecord.occurredAtUtc).format('YYYY-MM-DD HH:mm:ss')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.receivedAt')}>
                {dayjs(detailRecord.receivedAtUtc).format('YYYY-MM-DD HH:mm:ss')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.store')}>
                {detailRecord.storeCode}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.device')}>
                {detailRecord.deviceCode}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.employee')}>
                {detailRecord.cashierName || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.cashierId')}>
                {detailRecord.cashierId || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.userGuid')}>
                <Typography.Text copyable={Boolean(detailRecord.userGuid)}>{detailRecord.userGuid || '-'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.sessionFlags')}>
                <Space wrap size={[4, 4]}>
                  {detailRecord.isOfflineCached ? <Tag>{t('operationLogs.detail.offlineCached')}</Tag> : null}
                  {detailRecord.isEmergencyOverride ? <Tag color="warning">{t('operationLogs.detail.emergencyOverride')}</Tag> : null}
                  {!detailRecord.isOfflineCached && !detailRecord.isEmergencyOverride ? '-' : null}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.outcome')}>
                <Tag color={getOutcomeColor(detailRecord.outcome)}>{outcomeLabel(detailRecord.outcome)}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.operation')}>
                {operationLabel(detailRecord.operationType)}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.reason')}>
                {detailRecord.reasonCode || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.orderGuid')}>
                <Typography.Text copyable={Boolean(detailRecord.orderGuid)}>{detailRecord.orderGuid || '-'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.receiptNumber')}>
                {detailRecord.receiptNumber || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.paymentMethod')}>
                {detailRecord.paymentMethod || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.paymentAmount')}>
                {formatMoney(detailRecord.paymentAmount, detailRecord.currencyCode || 'AUD')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.beforeAfterActual')}>
                {formatMoney(detailRecord.beforeActual, detailRecord.currencyCode || 'AUD')} →{' '}
                {formatMoney(detailRecord.afterActual, detailRecord.currencyCode || 'AUD')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.columns.amountChange')}>
                {formatSignedMoney(detailRecord.amountDelta, detailRecord.currencyCode || 'AUD')}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.appVersion')}>
                {detailRecord.appVersion || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.instanceId')}>
                {detailRecord.instanceId || '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.correlationId')}>
                <Typography.Text copyable={Boolean(detailRecord.correlationId)}>{detailRecord.correlationId || '-'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.traceId')}>
                <Space>
                  <Typography.Text copyable={Boolean(detailRecord.traceId)}>{detailRecord.traceId || '-'}</Typography.Text>
                  {access.canViewSystemLogs ? (
                    <Link
                      to={buildSystemLogLink({
                        deviceCode: detailRecord.deviceCode,
                        traceId: detailRecord.traceId,
                        occurredAtUtc: detailRecord.occurredAtUtc,
                      })}
                    >
                      <ToolOutlined /> {t('operationLogs.detail.openSystemLogs')}
                    </Link>
                  ) : null}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.safeMessage')} span={2}>
                <Typography.Paragraph style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                  {detailRecord.safeMessage || '-'}
                </Typography.Paragraph>
              </Descriptions.Item>
              <Descriptions.Item label={t('operationLogs.detail.safeProperties')} span={2}>
                <Typography.Paragraph code style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                  {formatSafeProperties(detailRecord.propertiesJson)}
                </Typography.Paragraph>
              </Descriptions.Item>
            </Descriptions>

            <Table<OperationAuditDetailItem>
              rowKey={(item) => `${item.eventId}-${item.lineIndex}`}
              size="small"
              columns={itemColumns}
              dataSource={detailRecord.items ?? []}
              pagination={false}
              scroll={{ x: 1155 }}
              locale={{ emptyText: t('operationLogs.detail.noItems') }}
            />
          </Space>
        ) : null}
      </Drawer>
    </PageContainer>
  )
}
