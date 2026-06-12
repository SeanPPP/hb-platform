import { Card, Descriptions, List, Space, Spin, Tag, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { getUserByGuid, getUserStores } from '../../../services/userService'
import type { UserDetailDto, UserStoreDto } from '../../../types/user'

export default function UserDetailPage() {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [user, setUser] = useState<UserDetailDto | null>(null)
  const [stores, setStores] = useState<UserStoreDto[]>([])

  useDynamicTabTitle(user ? t('system.users.userDetailTitle', { name: user.username }) : t('system.users.userDetailTitle', { name: id }))

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
        message.error(t('system.users.loadDetailFailed'))
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
    return <Typography.Text type="danger">{t('system.users.userNotFound')}</Typography.Text>
  }

  return (
    <PageContainer
      title={t('system.users.userDetailTitle', { name: user.username })}
      subtitle={t('system.users.detailTabSubtitle')}
    >
      <Card>
        <Descriptions bordered column={2}>
          <Descriptions.Item label={t('system.users.username')}>{user.username}</Descriptions.Item>
          <Descriptions.Item label={t('system.users.fullName')}>{user.fullName || '--'}</Descriptions.Item>
          <Descriptions.Item label={t('system.users.email')}>{user.email}</Descriptions.Item>
          <Descriptions.Item label={t('system.users.status')}>
            <Tag color={user.isActive ? 'success' : 'default'}>{user.isActive ? t('system.users.active') : t('system.users.inactive')}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('system.users.roles')} span={2}>
            <Space wrap>
              {user.roleNames?.length ? user.roleNames.map((item) => <Tag key={item}>{item}</Tag>) : '--'}
            </Space>
          </Descriptions.Item>
          <Descriptions.Item label={t('system.users.createdAt')}>{user.createdAt}</Descriptions.Item>
          <Descriptions.Item label={t('system.users.updatedAt')}>{user.updatedAt}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title={t('system.users.linkedStores')}>
        <List
          dataSource={stores}
          locale={{ emptyText: t('system.users.noLinkedStores') }}
          renderItem={(item) => (
            <List.Item>
              <Space>
                <Typography.Text strong>{item.storeName}</Typography.Text>
                <Tag>{item.storeCode}</Tag>
                {item.isManageable ? <Tag color="processing">{t('system.users.manageableStore')}</Tag> : null}
              </Space>
            </List.Item>
          )}
        />
      </Card>
    </PageContainer>
  )
}
