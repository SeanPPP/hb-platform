import { Card, Descriptions, List, Space, Spin, Tag, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router-dom'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { getRoleByGuid } from '../../../services/roleService'
import type { RoleDetailDto } from '../../../types/role'
import RolePermissionManager from './RolePermissionManager'

export default function RoleDetailPage() {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [role, setRole] = useState<RoleDetailDto | null>(null)

  const loadData = async () => {
    setLoading(true)
    try {
      const detail = await getRoleByGuid(id)
      setRole(detail)
    } catch (error) {
      console.error(error)
      message.error(t('system.roles.loadDetailFailed'))
    } finally {
      setLoading(false)
    }
  }

  useDynamicTabTitle(role ? t('system.roles.detailTitle', { name: role.roleName }) : t('system.roles.detailTitle', { name: id }))

  useEffect(() => {
    void loadData()
  }, [id])

  if (loading) {
    return <Spin size="large" className="page-spin" />
  }

  if (!role) {
    return <Typography.Text type="danger">{t('system.roles.notFound')}</Typography.Text>
  }

  return (
    <PageContainer
      title={t('system.roles.detailTitle', { name: role.roleName })}
      subtitle={t('system.roles.detailTabSubtitle')}
    >
      <Card>
        <Descriptions bordered column={2}>
          <Descriptions.Item label={t('system.roles.roleName')}>{role.roleName}</Descriptions.Item>
          <Descriptions.Item label={t('column.status')}>
            <Tag color={role.isActive ? 'success' : 'default'}>{role.isActive ? t('common.active') : t('common.inactive')}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('column.description')} span={2}>
            {role.description || '--'}
          </Descriptions.Item>
          <Descriptions.Item label={t('system.roles.linkedUserCount')}>{role.userCount}</Descriptions.Item>
          <Descriptions.Item label={t('system.users.updatedAt')}>{role.updatedAt}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title={t('system.roles.permManagement')}>
        <HasPermission
          code={P.Roles.ManagePermissions}
          fallback={
            <Space wrap>
              {role.permissions?.length
                ? role.permissions.map((item) => <Tag key={item}>{item}</Tag>)
                : t('system.roles.noPermissions')}
            </Space>
          }
        >
          <RolePermissionManager
            roleGuid={role.roleGUID}
            roleName={role.roleName}
            onChanged={() => {
              void loadData()
            }}
          />
        </HasPermission>
      </Card>

      <Card title={t('system.roles.linkedUsers')}>
        <List
          dataSource={role.users ?? []}
                locale={{ emptyText: t('system.roles.noLinkedUsers') }}
          renderItem={(item) => (
            <List.Item>
              <Space>
                <Typography.Text strong>{item.username}</Typography.Text>
                <Typography.Text type="secondary">{item.email}</Typography.Text>
              </Space>
            </List.Item>
          )}
        />
      </Card>
    </PageContainer>
  )
}
