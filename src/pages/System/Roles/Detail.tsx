import { Card, Descriptions, List, Space, Spin, Tag, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { getRoleByGuid } from '../../../services/roleService'
import type { RoleDetailDto } from '../../../types/role'
import RolePermissionManager from './RolePermissionManager'

export default function RoleDetailPage() {
  const { id = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [role, setRole] = useState<RoleDetailDto | null>(null)

  useDynamicTabTitle(role ? `角色详情 - ${role.roleName}` : `角色详情 - ${id}`)

  useEffect(() => {
    const run = async () => {
      setLoading(true)
      try {
        const detail = await getRoleByGuid(id)
        setRole(detail)
      } catch (error) {
        console.error(error)
        message.error('加载角色详情失败')
      } finally {
        setLoading(false)
      }
    }

    void run()
  }, [id])

  if (loading) {
    return <Spin size="large" className="page-spin" />
  }

  if (!role) {
    return <Typography.Text type="danger">未找到角色信息</Typography.Text>
  }

  return (
    <PageContainer
      title={`角色详情 - ${role.roleName}`}
      subtitle="详情页标题支持动态更新，Tab key 则始终保持为完整路径。"
    >
      <Card>
        <Descriptions bordered column={2}>
          <Descriptions.Item label="角色名称">{role.roleName}</Descriptions.Item>
          <Descriptions.Item label="状态">
            <Tag color={role.isActive ? 'success' : 'default'}>{role.isActive ? '启用' : '停用'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="描述" span={2}>
            {role.description || '--'}
          </Descriptions.Item>
          <Descriptions.Item label="关联用户数">{role.userCount}</Descriptions.Item>
          <Descriptions.Item label="更新时间">{role.updatedAt}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title="权限管理">
        <HasPermission
          code={P.Roles.ManagePermissions}
          fallback={
            <Space wrap>
              {role.permissions?.length
                ? role.permissions.map((item) => <Tag key={item}>{item}</Tag>)
                : '暂无权限'}
            </Space>
          }
        >
          <RolePermissionManager
            roleGuid={role.roleGUID}
            roleName={role.roleName}
            onChanged={() => {
              void run()
            }}
          />
        </HasPermission>
      </Card>

      <Card title="关联用户">
        <List
          dataSource={role.users ?? []}
          locale={{ emptyText: '暂无关联用户' }}
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
