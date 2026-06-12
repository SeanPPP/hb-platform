import { Card, Descriptions, Spin, Tag, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { getStoreByGuid } from '../../../services/storeService'
import type { StoreDto } from '../../../types/store'

export default function StoreDetailPage() {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [store, setStore] = useState<StoreDto | null>(null)

  useDynamicTabTitle(store ? t('system.stores.detailTitle', { name: store.storeCode }) : t('system.stores.detailTitle', { name: id }))

  useEffect(() => {
    const run = async () => {
      setLoading(true)
      try {
        const detail = await getStoreByGuid(id)
        setStore(detail)
      } catch (error) {
        console.error(error)
        message.error(t('system.stores.loadDetailFailed'))
      } finally {
        setLoading(false)
      }
    }

    void run()
  }, [id])

  if (loading) {
    return <Spin size="large" className="page-spin" />
  }

  if (!store) {
    return <Typography.Text type="danger">{t('system.stores.notFound')}</Typography.Text>
  }

  return (
    <PageContainer
      title={t('system.stores.detailTitle', { name: store.storeCode })}
      subtitle={t('system.stores.detailTabSubtitle')}
    >
      <Card>
        <Descriptions bordered column={2}>
          <Descriptions.Item label={t('system.stores.storeName')}>{store.storeName}</Descriptions.Item>
          <Descriptions.Item label={t('system.stores.storeCode')}>{store.storeCode}</Descriptions.Item>
          <Descriptions.Item label={t('system.stores.brandName')}>{store.brandName || '--'}</Descriptions.Item>
          <Descriptions.Item label="ABN">{store.abn || '--'}</Descriptions.Item>
          <Descriptions.Item label={t('system.stores.contactPhone')}>{store.contactPhone || '--'}</Descriptions.Item>
          <Descriptions.Item label={t('system.stores.cashRegisterEnabled')}>
            <Tag color={store.isActive ? 'success' : 'default'}>{store.isActive ? t('common.active') : t('common.inactive')}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('system.stores.address')} span={2}>
            {store.address || '--'}
          </Descriptions.Item>
          <Descriptions.Item label={t('column.createTime')}>{store.createdAt}</Descriptions.Item>
          <Descriptions.Item label={t('system.users.updatedAt')}>{store.updatedAt}</Descriptions.Item>
        </Descriptions>
      </Card>
    </PageContainer>
  )
}
