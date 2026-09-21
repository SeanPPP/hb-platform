import { Button, Empty, Space, Spin, Typography, message } from 'antd'
import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import SupplyStatusCard from '../../components/SupplyNotice/SupplyStatusCard'
import { registerPageMessages } from '../../i18n/registerPageMessages'
import { supplyStatusCardMessages } from '../../components/SupplyNotice/supplyNoticeMessages'
import {
  acknowledgeStoreSupplyRestocked,
  getStoreSupplyWatches,
  unwatchStoreSupply,
} from '../../services/supplyNoticeService'
import { useShopStore } from '../../store/shop'
import type { StoreSupplyStatus } from '../../types/supplyNotice'

registerPageMessages(supplyStatusCardMessages)

const { Text, Title } = Typography

/**
 * 我关注的商品：已恢复订货的排最前，其余按关注时间倒序（后端已排好序）。
 * “知道了”只关闭已恢复的关注；仍在等待的关注不受影响。
 */
export default function ShopSupplyWatchesPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const selectedStore = useShopStore((state) => state.selectedStore)
  const refreshSupplyWatchSummary = useShopStore((state) => state.refreshSupplyWatchSummary)
  const storeCode = selectedStore?.storeCode ?? null
  const [items, setItems] = useState<StoreSupplyStatus[]>([])
  const [loading, setLoading] = useState(false)
  const [busyCode, setBusyCode] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!storeCode) {
      setItems([])
      return
    }
    setLoading(true)
    try {
      setItems(await getStoreSupplyWatches(storeCode))
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('supplyStatusCard.actionFailed'))
    } finally {
      setLoading(false)
    }
  }, [storeCode, t])

  useEffect(() => {
    void load()
  }, [load])

  const restocked = items.filter((item) => item.isOrderable)
  const waiting = items.filter((item) => !item.isOrderable)

  const runAction = async (productCode: string | null, action: () => Promise<unknown>) => {
    setBusyCode(productCode)
    try {
      await action()
      await load()
      void refreshSupplyWatchSummary()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('supplyStatusCard.actionFailed'))
    } finally {
      setBusyCode(null)
    }
  }

  const handleUnwatch = (status: StoreSupplyStatus) =>
    storeCode ? void runAction(status.productCode, () => unwatchStoreSupply(storeCode, status.productCode)) : undefined
  const handleAcknowledge = (status: StoreSupplyStatus) =>
    storeCode ? void runAction(status.productCode, () => acknowledgeStoreSupplyRestocked(storeCode, [status.productCode])) : undefined
  const handleAcknowledgeAll = () =>
    storeCode ? void runAction(null, () => acknowledgeStoreSupplyRestocked(storeCode)) : undefined
  // 去订货：按货号搜索，商品已恢复所以正常列表能命中。
  const handleOrder = (status: StoreSupplyStatus) =>
    navigate(`/shop?keyword=${encodeURIComponent(status.itemNumber || status.productCode)}`)

  return (
    <div className="shop-feature-page shop-supply-watches-page" data-testid="shop-supply-watches-page">
      <Title level={4} style={{ marginBottom: 16 }}>{t('supplyStatusCard.watchesTitle')}</Title>
      {loading && !items.length ? (
        <Spin />
      ) : !items.length ? (
        <Empty description={t('supplyStatusCard.watchesEmpty')} />
      ) : (
        <Space direction="vertical" size={20} style={{ width: '100%', maxWidth: 640 }}>
          {restocked.length ? (
            <section>
              <Space style={{ marginBottom: 8, width: '100%', justifyContent: 'space-between' }}>
                <Text strong>{t('supplyStatusCard.restockedSection')}（{restocked.length}）</Text>
                <Button size="small" loading={busyCode === null && loading} onClick={handleAcknowledgeAll}>
                  {t('supplyStatusCard.acknowledgeAll')}
                </Button>
              </Space>
              <Space direction="vertical" size={8} style={{ width: '100%' }}>
                {restocked.map((status) => (
                  <SupplyStatusCard
                    key={status.productCode}
                    status={status}
                    busy={busyCode === status.productCode}
                    onAcknowledge={handleAcknowledge}
                    onOrder={handleOrder}
                  />
                ))}
              </Space>
            </section>
          ) : null}
          {waiting.length ? (
            <section>
              <Text strong style={{ display: 'block', marginBottom: 8 }}>{t('supplyStatusCard.waitingSection')}（{waiting.length}）</Text>
              <Space direction="vertical" size={8} style={{ width: '100%' }}>
                {waiting.map((status) => (
                  <SupplyStatusCard
                    key={status.productCode}
                    status={status}
                    busy={busyCode === status.productCode}
                    onUnwatch={handleUnwatch}
                  />
                ))}
              </Space>
            </section>
          ) : null}
        </Space>
      )}
    </div>
  )
}
