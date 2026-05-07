import { Card, Descriptions, List, Space, Spin, Tag, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { getUserByGuid, getUserStores } from '../../../services/userService'
import type { UserDetailDto, UserStoreDto } from '../../../types/user'

export default function UserDetailPage() {
  const { id = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [user, setUser] = useState<UserDetailDto | null>(null)
  const [stores, setStores] = useState<UserStoreDto[]>([])

  useDynamicTabTitle(user ? `用户详情 - ${user.username}` : `用户详情 - ${id}`)

  useEffect(() => {
    const run = async () => {
      setLoading(true)
      try {
        const [detail, storeList] = await Promise.all([
          getUserByGuid(id),
          getUserStores(id).catch(() => []),
        ])
        setUser(detail)
        setStores(storeList)
      } catch (error) {
        console.error(error)
        message.error('加载用户详情失败')
      } finally {
        setLoading(false)
      }
    }

    void run()
  }, [id])

  if (loading) {
    return <Spin size="large" className="page-spin" />
  }

  if (!user) {
    return <Typography.Text type="danger">未找到用户信息</Typography.Text>
  }

  return (
    <PageContainer
      title={`用户详情 - ${user.username}`}
      subtitle="这个详情页会使用完整路径作为 Tab key，不同用户会打开不同页签。"
    >
      <Card>
        <Descriptions bordered column={2}>
          <Descriptions.Item label="用户名">{user.username}</Descriptions.Item>
          <Descriptions.Item label="姓名">{user.fullName || '--'}</Descriptions.Item>
          <Descriptions.Item label="邮箱">{user.email}</Descriptions.Item>
          <Descriptions.Item label="状态">
            <Tag color={user.isActive ? 'success' : 'default'}>{user.isActive ? '启用' : '停用'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="角色" span={2}>
            <Space wrap>
              {user.roleNames?.length ? user.roleNames.map((item) => <Tag key={item}>{item}</Tag>) : '--'}
            </Space>
          </Descriptions.Item>
          <Descriptions.Item label="创建时间">{user.createdAt}</Descriptions.Item>
          <Descriptions.Item label="更新时间">{user.updatedAt}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title="关联分店">
        <List
          dataSource={stores}
          locale={{ emptyText: '暂无关联分店' }}
          renderItem={(item) => (
            <List.Item>
              <Space>
                <Typography.Text strong>{item.storeName}</Typography.Text>
                <Tag>{item.storeCode}</Tag>
                {item.isPrimary ? <Tag color="processing">主分店</Tag> : null}
              </Space>
            </List.Item>
          )}
        />
      </Card>
    </PageContainer>
  )
}
