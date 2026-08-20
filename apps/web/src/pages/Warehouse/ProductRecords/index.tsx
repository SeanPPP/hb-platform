import { ArrowLeftOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Image,
  Result,
  Skeleton,
  Space,
  Tabs,
  Tag,
  Typography,
} from 'antd'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { queryWarehouseProductRecordSummary } from '../../../services/warehouseProductRecordsService'
import { useAuthStore } from '../../../store/auth'
import type { WarehouseProductRecordSummary } from '../../../types/warehouseProductRecords'
import { createLatestRequestGuard } from '../../../utils/latestRequestGuard'
import { isAbortError } from './logic'
import ContainersPanel from './ContainersPanel'
import AllocationsPanel from './AllocationsPanel'
import SalesPanel from './SalesPanel'

const PRODUCT_IMAGE_FALLBACK = `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" rx="6" fill="#f5f5f5"/><text x="32" y="36" text-anchor="middle" font-size="12" fill="#999">无图</text></svg>',
)}`

type ProductRecordTabKey = 'containers' | 'allocations' | 'sales'

export default function WarehouseProductRecordsPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const route = useStableRouteContext()
  const { active } = useKeepAliveContext()
  const { access } = useAuthStore()
  const productCode = route?.params.productCode || ''

  const [summary, setSummary] = useState<WarehouseProductRecordSummary | null>(null)
  const [summaryLoading, setSummaryLoading] = useState(true)
  const [summaryError, setSummaryError] = useState<unknown>(null)
  const abortRef = useRef<AbortController>()
  const guardRef = useRef(createLatestRequestGuard())

  const [activeTab, setActiveTab] = useState<ProductRecordTabKey>('containers')
  const [visitedTabState, setVisitedTabState] = useState<{
    productCode: string
    keys: ProductRecordTabKey[]
  }>({ productCode: '', keys: [] })

  useEffect(() => {
    abortRef.current?.abort()
    guardRef.current.invalidate()
    if (!active || !productCode) {
      setSummaryLoading(false)
      return
    }

    const controller = new AbortController()
    abortRef.current = controller
    const requestId = guardRef.current.begin()
    setSummaryLoading(true)
    setSummaryError(null)

    queryWarehouseProductRecordSummary(productCode, controller.signal)
      .then((result) => {
        if (!guardRef.current.isLatest(requestId)) return
        setSummary(result)
      })
      .catch((nextError) => {
        if (!guardRef.current.isLatest(requestId) || isAbortError(nextError)) return
        setSummaryError(nextError)
      })
      .finally(() => {
        if (guardRef.current.isLatest(requestId)) setSummaryLoading(false)
      })

    return () => {
      controller.abort()
      guardRef.current.invalidate()
    }
  }, [active, productCode])

  const canViewContainers = access.canViewContainers
  const canViewSales = access.canViewProductSalesAnalysis
  const availableKeys: ProductRecordTabKey[] = [
    ...(canViewContainers ? (['containers', 'allocations'] as ProductRecordTabKey[]) : []),
    ...(canViewSales ? (['sales'] as ProductRecordTabKey[]) : []),
  ]
  const firstTabKey = availableKeys[0] ?? null
  const effectiveActiveTab: ProductRecordTabKey = availableKeys.includes(activeTab)
    ? activeTab
    : (firstTabKey ?? 'containers')
  const productVisitedTabKeys = visitedTabState.productCode === productCode
    ? visitedTabState.keys
    : []
  const visitedTabKeys = productVisitedTabKeys.includes(effectiveActiveTab)
    ? productVisitedTabKeys
    : [...productVisitedTabKeys, effectiveActiveTab]

  const handleTabChange = (key: string) => {
    const nextKey = key as ProductRecordTabKey
    setVisitedTabState((current) => {
      const currentKeys = current.productCode === productCode
        ? current.keys
        : [effectiveActiveTab]
      return {
        productCode,
        keys: currentKeys.includes(nextKey) ? currentKeys : [...currentKeys, nextKey],
      }
    })
    setActiveTab(nextKey)
  }

  const tabItems: { key: ProductRecordTabKey; label: string; children: React.ReactNode }[] = []
  if (canViewContainers) {
    tabItems.push({
      key: 'containers',
      label: t('warehouseProductRecords.tabContainers'),
      children: <ContainersPanel productCode={productCode} enabled={active && visitedTabKeys.includes('containers')} />,
    })
    tabItems.push({
      key: 'allocations',
      label: t('warehouseProductRecords.tabAllocations'),
      children: <AllocationsPanel productCode={productCode} enabled={active && visitedTabKeys.includes('allocations')} />,
    })
  }
  if (canViewSales) {
    tabItems.push({
      key: 'sales',
      label: t('warehouseProductRecords.tabSales'),
      children: <SalesPanel productCode={productCode} enabled={active && visitedTabKeys.includes('sales')} />,
    })
  }

  const hasUsableTab = firstTabKey !== null

  return (
    <PageContainer
      title={t('warehouseProductRecords.title')}
      subtitle={t('warehouseProductRecords.subtitle')}
      extra={(
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/warehouse/products')}>
          {t('warehouseProductRecords.back')}
        </Button>
      )}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Card size="small">
          {summaryLoading ? (
            <Skeleton active paragraph={{ rows: 2 }} />
          ) : summaryError ? (
            <Result
              status="error"
              title={t('warehouseProductRecords.loadFailed')}
              subTitle={summaryError instanceof Error ? summaryError.message : undefined}
            />
          ) : (
            <Space size={16} wrap align="start">
              <Image
                src={summary?.imageUrl || PRODUCT_IMAGE_FALLBACK}
                alt=""
                width={64}
                height={64}
                style={{ objectFit: 'contain', borderRadius: 6, border: '1px solid #f0f0f0' }}
                preview={false}
                fallback={PRODUCT_IMAGE_FALLBACK}
              />
              <Space direction="vertical" size={2}>
                <Space size={8} wrap>
                  <Typography.Text strong style={{ fontSize: 16 }}>
                    {summary?.productName || summary?.englishName || summary?.productCode || '-'}
                  </Typography.Text>
                  {summary?.englishName ? (
                    <Typography.Text type="secondary">{summary.englishName}</Typography.Text>
                  ) : null}
                  {summary?.isActive ? (
                    <Tag color="green">{t('warehouseProductRecords.active')}</Tag>
                  ) : (
                    <Tag>{t('warehouseProductRecords.inactive')}</Tag>
                  )}
                </Space>
                <Typography.Text type="secondary">
                  {t('warehouseProductRecords.productCode')}: {summary?.productCode || '-'}
                  {' · '}
                  {t('warehouseProductRecords.itemNumber')}: {summary?.itemNumber || '-'}
                  {' · '}
                  {t('warehouseProductRecords.barcode')}: {summary?.barcode || '-'}
                </Typography.Text>
              </Space>
            </Space>
          )}
        </Card>

        {hasUsableTab ? (
          <Tabs
            activeKey={effectiveActiveTab}
            onChange={handleTabChange}
            items={tabItems}
            destroyOnHidden={false}
          />
        ) : (
          <Result
            status="403"
            title={t('warehouseProductRecords.noPermission')}
          />
        )}
      </Space>
    </PageContainer>
  )
}
