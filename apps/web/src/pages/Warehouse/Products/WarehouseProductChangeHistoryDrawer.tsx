import { HistoryOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Descriptions,
  Drawer,
  Empty,
  Pagination,
  Space,
  Spin,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getWarehouseProductChangeHistory,
  type WarehouseProductChangeHistoryEvent,
  type WarehouseProductChangeHistoryItem,
  type WarehouseProductChangeHistoryResult,
} from '../../../services/warehouseProductService'
import {
  createWarehouseProductChangeHistoryRequestGuard,
  formatWarehouseProductChangeHistoryValue,
  getWarehouseProductChangeHistoryActionKey,
  isWarehouseProductChangeHistoryAbortError,
} from './WarehouseProductChangeHistoryDrawer.logic'

const CHANGE_HISTORY_PAGE_SIZE = 20

export interface WarehouseProductChangeHistoryDrawerProps {
  open: boolean
  productCode?: string
  itemNumber?: string
  productName?: string
  onClose: () => void
}

function localizeHistoryValue(t: ReturnType<typeof useTranslation>['t'], namespace: string, value: string) {
  if (namespace !== 'actions') {
    return t(`warehouse.changeHistory.${namespace}.${value}`, { defaultValue: value })
  }

  const actionKey = getWarehouseProductChangeHistoryActionKey(value)

  return t(
    `warehouse.changeHistory.actions.${actionKey ?? value}`,
    { defaultValue: value },
  )
}

function formatHistoryDate(value: string, language: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value || '--'
  }

  return new Intl.DateTimeFormat(language, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(date)
}

function formatHistoryFieldValue(
  t: ReturnType<typeof useTranslation>['t'],
  value: unknown,
) {
  if (value === true || value === 'true') {
    return t('warehouse.changeHistory.values.true', '是')
  }
  if (value === false || value === 'false') {
    return t('warehouse.changeHistory.values.false', '否')
  }

  return formatWarehouseProductChangeHistoryValue(value)
}

function ChangeTable({
  changes,
  t,
}: {
  changes: WarehouseProductChangeHistoryItem[]
  t: ReturnType<typeof useTranslation>['t']
}) {
  const columns: ColumnsType<WarehouseProductChangeHistoryItem> = [
    {
      title: t('warehouse.changeHistory.field', '字段'),
      dataIndex: 'fieldKey',
      key: 'fieldKey',
      width: 180,
      render: (value: string) => t(`warehouse.changeHistory.fields.${value}`, { defaultValue: value }),
    },
    {
      title: t('warehouse.changeHistory.before', '修改前'),
      dataIndex: 'beforeValue',
      key: 'beforeValue',
      render: (value: unknown) => (
        <Typography.Text type="secondary" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {formatHistoryFieldValue(t, value)}
        </Typography.Text>
      ),
    },
    {
      title: t('warehouse.changeHistory.after', '修改后'),
      dataIndex: 'afterValue',
      key: 'afterValue',
      render: (value: unknown) => (
        <Typography.Text style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {formatHistoryFieldValue(t, value)}
        </Typography.Text>
      ),
    },
  ]

  return <Table size="small" bordered pagination={false} rowKey={(item) => item.fieldKey} columns={columns} dataSource={changes} />
}

function HistoryEventCard({
  event,
  language,
  t,
}: {
  event: WarehouseProductChangeHistoryEvent
  language: string
  t: ReturnType<typeof useTranslation>['t']
}) {
  const actor = event.actorName || event.actorUserGuid || '--'
  const source = localizeHistoryValue(t, 'sources', event.source)
  const action = localizeHistoryValue(t, 'actions', event.action)

  return (
    <div style={{ border: '1px solid #f0f0f0', borderRadius: 6, padding: 12 }}>
      <Space direction="vertical" size={8} style={{ width: '100%' }}>
        <Space wrap size={[8, 4]}>
          <Typography.Text strong>{action}</Typography.Text>
          <Typography.Text type="secondary">{formatHistoryDate(event.occurredAtUtc, language)}</Typography.Text>
          <Tag>{actor}</Tag>
          <Tag color="blue">{source}</Tag>
          {event.actorType ? <Tag>{localizeHistoryValue(t, 'actorTypes', event.actorType)}</Tag> : null}
          {event.batchGuid ? <Typography.Text type="secondary">{t('warehouse.changeHistory.batch', '批次')}: {event.batchGuid}</Typography.Text> : null}
          {event.sourceReference ? <Typography.Text type="secondary">{event.sourceReference}</Typography.Text> : null}
        </Space>
        <ChangeTable changes={event.changes} t={t} />
      </Space>
    </div>
  )
}

export default function WarehouseProductChangeHistoryDrawer({
  open,
  productCode,
  itemNumber,
  productName,
  onClose,
}: WarehouseProductChangeHistoryDrawerProps) {
  const { t, i18n } = useTranslation()
  const [pageNumber, setPageNumber] = useState(1)
  const [data, setData] = useState<WarehouseProductChangeHistoryResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const requestGuardRef = useRef(createWarehouseProductChangeHistoryRequestGuard())
  const abortControllerRef = useRef<AbortController | null>(null)

  const loadHistory = useCallback(async (targetPage: number) => {
    if (!productCode) {
      return
    }

    abortControllerRef.current?.abort()
    const controller = new AbortController()
    abortControllerRef.current = controller
    const requestToken = requestGuardRef.current.start(productCode, targetPage, CHANGE_HISTORY_PAGE_SIZE)
    setLoading(true)
    setError(null)

    try {
      const result = await getWarehouseProductChangeHistory(
        productCode,
        { pageNumber: targetPage, pageSize: CHANGE_HISTORY_PAGE_SIZE },
        { signal: controller.signal },
      )
      if (!requestGuardRef.current.isCurrent(requestToken) || controller.signal.aborted) {
        return
      }
      setData(result)
    } catch (loadError) {
      if (isWarehouseProductChangeHistoryAbortError(loadError) || !requestGuardRef.current.isCurrent(requestToken)) {
        return
      }
      setError(loadError instanceof Error ? loadError.message : t('warehouse.changeHistory.loadFailed', '加载修改记录失败'))
    } finally {
      if (requestGuardRef.current.isCurrent(requestToken)) {
        setLoading(false)
      }
    }
  }, [productCode, t])

  useEffect(() => {
    abortControllerRef.current?.abort()
    requestGuardRef.current.cancel()

    if (!open || !productCode) {
      setData(null)
      setError(null)
      setLoading(false)
      setPageNumber(1)
      return
    }

    setData(null)
    setError(null)
    setPageNumber(1)
    void loadHistory(1)

    return () => {
      abortControllerRef.current?.abort()
      requestGuardRef.current.cancel()
    }
  }, [loadHistory, open, productCode])

  const handlePageChange = (nextPage: number) => {
    setPageNumber(nextPage)
    void loadHistory(nextPage)
  }

  const handleRetry = () => {
    void loadHistory(pageNumber)
  }

  const events = data?.events ?? []

  return (
    <Drawer
      title={
        <Space size={8}>
          <HistoryOutlined />
          <span>{t('warehouse.changeHistory.title', '商品修改记录')}</span>
        </Space>
      }
      open={open}
      width={920}
      destroyOnHidden
      onClose={onClose}
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Descriptions size="small" bordered column={3}>
          <Descriptions.Item label={t('warehouse.changeHistory.productCode', '商品编码')}>
            {productCode || data?.productCode || '--'}
          </Descriptions.Item>
          <Descriptions.Item label={t('warehouse.changeHistory.itemNumber', '货号')}>
            {itemNumber || data?.itemNumber || '--'}
          </Descriptions.Item>
          <Descriptions.Item label={t('warehouse.changeHistory.productName', '商品名称')}>
            {productName || data?.productName || '--'}
          </Descriptions.Item>
        </Descriptions>

        {error ? (
          <Alert
            type="error"
            showIcon
            message={t('warehouse.changeHistory.loadFailed', '加载修改记录失败')}
            description={error}
            action={
              <Button size="small" icon={<ReloadOutlined />} onClick={handleRetry}>
                {t('warehouse.changeHistory.retry', '重试')}
              </Button>
            }
          />
        ) : null}

        {loading && !events.length ? (
          <div style={{ minHeight: 180, display: 'grid', placeItems: 'center' }}>
            <Spin tip={t('warehouse.changeHistory.loading', '加载中...')} />
          </div>
        ) : null}

        {!loading && !error && !events.length ? (
          <Empty description={t('warehouse.changeHistory.empty', '暂无修改记录')} />
        ) : null}

        {events.length ? (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            {events.map((event) => (
              <HistoryEventCard key={event.eventGuid} event={event} language={i18n.language} t={t} />
            ))}
            <Pagination
              size="small"
              current={data?.pageNumber ?? pageNumber}
              pageSize={data?.pageSize ?? CHANGE_HISTORY_PAGE_SIZE}
              total={data?.total ?? 0}
              showSizeChanger={false}
              onChange={handlePageChange}
              showTotal={(total) => t('warehouse.changeHistory.total', `共 ${total} 条`, { total })}
            />
          </Space>
        ) : null}
      </Space>
    </Drawer>
  )
}
