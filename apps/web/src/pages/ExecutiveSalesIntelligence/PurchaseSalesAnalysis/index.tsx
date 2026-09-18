import { Spin, Tabs } from 'antd'
import { Suspense, lazy, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'

import PageContainer from '../../../components/PageContainer'
import { useAuthStore } from '../../../store/auth'

import {
  ADMIN_PURCHASE_SALES_TAB_PARAM,
  resolveAdminPurchaseSalesTab,
  type AdminPurchaseSalesTab,
} from './tabs'

// 两个标签各自是独立的大页面，按需加载；antd Tabs 只在首次切到时挂载内容，切走后保留查询结果。
const BatchProductSalesAnalysisPage = lazy(() => import('../BatchProductSalesAnalysis'))
const LocalSupplierPurchaseSalesAnalysisPage = lazy(() => import('../../PosAdmin/LocalSupplierPurchaseSalesAnalysis'))

function TabFallback() {
  return <div style={{ padding: '48px 0', textAlign: 'center' }}><Spin /></div>
}

/**
 * 后台「销售看板 / 进货销量分析」：批量货号销量与分店进货销量分析合为一页两个标签。
 * 每个标签仍按原页面权限显示；当前标签写在地址栏 ?tab= 中，刷新与旧地址重定向都能落到正确标签。
 */
export default function PurchaseSalesAnalysisPage() {
  const { t } = useTranslation()
  const canViewBatch = useAuthStore((state) => state.access.canViewBatchProductSalesAnalysis)
  const canViewStore = useAuthStore((state) => state.access.canViewLocalSupplierPurchaseSalesAnalysis)
  const [searchParams, setSearchParams] = useSearchParams()
  const activeTab = resolveAdminPurchaseSalesTab(searchParams.get(ADMIN_PURCHASE_SALES_TAB_PARAM), {
    canViewBatch,
    canViewStore,
  })

  const handleTabChange = (key: string) => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current)
      next.set(ADMIN_PURCHASE_SALES_TAB_PARAM, key)
      return next
    }, { replace: true })
  }

  const tabItems: { key: AdminPurchaseSalesTab; label: string; children: ReactNode }[] = []
  if (canViewBatch) {
    tabItems.push({
      key: 'batch',
      label: t('menu.batchProductSalesAnalysis', '批量货号销量'),
      children: (
        <Suspense fallback={<TabFallback />}>
          <BatchProductSalesAnalysisPage embedded />
        </Suspense>
      ),
    })
  }
  if (canViewStore) {
    tabItems.push({
      key: 'store',
      label: t('menu.localSupplierPurchaseSalesAnalysis', '分店进货销量分析'),
      children: (
        <Suspense fallback={<TabFallback />}>
          <LocalSupplierPurchaseSalesAnalysisPage embedded />
        </Suspense>
      ),
    })
  }

  const subtitle = activeTab === 'store'
    ? t('posAdmin.localSupplierPurchaseSalesAnalysis.subtitle', '按分店、供应商和订单日期范围查看商品最近进货与后续销量表现。')
    : t('batchProductSalesAnalysis.subtitle', '批量查询商品在指定时间范围内的销量，支持按分店查看每日销量明细。')

  return (
    <PageContainer title={t('menu.purchaseSalesAnalysis', '进货销量分析')} subtitle={subtitle}>
      {/* 只有一个标签权限时不显示标签栏，直接展示该页面，避免出现只有一个选项的标签。 */}
      {tabItems.length > 1 && activeTab ? (
        <Tabs activeKey={activeTab} onChange={handleTabChange} items={tabItems} />
      ) : (
        tabItems[0]?.children ?? null
      )}
    </PageContainer>
  )
}
