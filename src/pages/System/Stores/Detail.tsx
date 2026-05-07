import { Card, Descriptions, Spin, Tag, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { getStoreByGuid } from '../../../services/storeService'
import type { StoreDto } from '../../../types/store'

export default function StoreDetailPage() {
  const { id = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [store, setStore] = useState<StoreDto | null>(null)

  useDynamicTabTitle(store ? `分店详情 - ${store.storeCode}` : `分店详情 - ${id}`)

  useEffect(() => {
    const run = async () => {
      setLoading(true)
      try {
        const detail = await getStoreByGuid(id)
        setStore(detail)
      } catch (error) {
        console.error(error)
        message.error('加载分店详情失败')
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
    return <Typography.Text type="danger">未找到分店信息</Typography.Text>
  }

  return (
    <PageContainer
      title={`分店详情 - ${store.storeCode}`}
      subtitle="这里展示的是旧系统分店模块在新框架中的详情页打开方式。"
    >
      <Card>
        <Descriptions bordered column={2}>
          <Descriptions.Item label="分店名称">{store.storeName}</Descriptions.Item>
          <Descriptions.Item label="分店编码">{store.storeCode}</Descriptions.Item>
          <Descriptions.Item label="品牌名称">{store.brandName || '--'}</Descriptions.Item>
          <Descriptions.Item label="ABN">{store.abn || '--'}</Descriptions.Item>
          <Descriptions.Item label="联系电话">{store.contactPhone || '--'}</Descriptions.Item>
          <Descriptions.Item label="状态">
            <Tag color={store.isActive ? 'success' : 'default'}>{store.isActive ? '启用' : '停用'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="地址" span={2}>
            {store.address || '--'}
          </Descriptions.Item>
          <Descriptions.Item label="创建时间">{store.createdAt}</Descriptions.Item>
          <Descriptions.Item label="更新时间">{store.updatedAt}</Descriptions.Item>
        </Descriptions>
      </Card>
    </PageContainer>
  )
}
