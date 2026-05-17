import { Card, Col, Collapse, Empty, List, Row, Space, Spin, Statistic, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../components/PageContainer'
import { getPermissions } from '../../services/roleService'
import { useAuthStore } from '../../store/auth'
import type { PermissionCategoryDto, PermissionDto } from '../../types/role'

interface PermissionItem extends PermissionDto {
  hasPermission: boolean
}

interface PermissionGroup {
  category: string
  displayName: string
  permissions: PermissionItem[]
}

export default function DashboardPage() {
  const { currentUser } = useAuthStore()
  const { t } = useTranslation()
  const [permGroups, setPermGroups] = useState<PermissionGroup[]>([])
  const [permLoading, setPermLoading] = useState(false)

  useEffect(() => {
    const fetchPerms = async () => {
      setPermLoading(true)
      try {
        const categories = await getPermissions()
        const userPerms = new Set(currentUser?.permissions ?? [])
        const groups: PermissionGroup[] = categories.map((cat: PermissionCategoryDto) => ({
          category: cat.category,
          displayName: cat.displayName || cat.category,
          permissions: cat.permissions.map((p: PermissionDto) => ({
            ...p,
            hasPermission: userPerms.has(p.name),
          })),
        }))
        setPermGroups(groups)
      } catch {
        setPermGroups([])
      } finally {
        setPermLoading(false)
      }
    }
    void fetchPerms()
  }, [currentUser])

  const userPermCount = currentUser?.permissions?.length ?? 0
  const totalPermCount = permGroups.reduce((sum, g) => sum + g.permissions.length, 0)
  const grantedCount = permGroups.reduce((sum, g) => sum + g.permissions.filter((p) => p.hasPermission).length, 0)

  const cards = [
    { title: t('dashboard.currentUser'), value: currentUser?.username || '--', simple: true },
    { title: t('dashboard.roleCount'), value: currentUser?.roleNames?.length ?? 0 },
    { title: t('dashboard.permCount'), value: `${grantedCount}/${totalPermCount || userPermCount}`, altValue: userPermCount },
    { title: t('dashboard.storeCount'), value: currentUser?.stores?.length ?? 0 },
  ]

  return (
    <PageContainer
      title={t('dashboard.title')}
      subtitle={t('dashboard.subtitle')}
    >
      <Row gutter={[16, 16]}>
        {cards.map((item) => (
          <Col xs={24} sm={12} xl={6} key={item.title}>
            <Card>
              {item.simple ? (
                <>
                  <Typography.Text type="secondary">{item.title}</Typography.Text>
                  <Typography.Title level={3} style={{ margin: '8px 0 0' }}>
                    {item.value}
                  </Typography.Title>
                </>
              ) : (
                <Statistic title={item.title} value={item.value} />
              )}
            </Card>
          </Col>
        ))}
      </Row>

      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title={t('dashboard.currentRoles')}>
            <List
              dataSource={currentUser?.roleNames ?? []}
              renderItem={(item) => <List.Item>{item}</List.Item>}
              locale={{ emptyText: t('dashboard.noRoleInfo') }}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title={t('dashboard.myPermissions', '我的权限')}>
            {permLoading ? (
              <div style={{ textAlign: 'center', padding: 24 }}><Spin /></div>
            ) : permGroups.length === 0 ? (
              <Empty description={t('dashboard.noPermData', '暂无权限数据')} />
            ) : (
              <Collapse
                size="small"
                items={permGroups.map((group) => {
                  const groupGranted = group.permissions.filter((p) => p.hasPermission).length
                  return {
                    key: group.category,
                    label: (
                      <Space>
                        <span>{group.displayName}</span>
                        <Tag color={groupGranted > 0 ? 'blue' : 'default'}>
                          {groupGranted}/{group.permissions.length}
                        </Tag>
                      </Space>
                    ),
                    children: (
                      <List
                        size="small"
                        dataSource={group.permissions}
                        renderItem={(perm) => (
                          <List.Item style={{ padding: '4px 0' }}>
                            <Typography.Text
                              type={perm.hasPermission ? undefined : 'secondary'}
                              delete={!perm.hasPermission}
                            >
                              {perm.displayName || perm.name}
                            </Typography.Text>
                            <Tag color={perm.hasPermission ? 'success' : 'default'}>
                              {perm.hasPermission ? t('dashboard.granted', '已授权') : t('dashboard.notGranted', '未授权')}
                            </Tag>
                          </List.Item>
                        )}
                      />
                    ),
                  }
                })}
              />
            )}
          </Card>
        </Col>
      </Row>
    </PageContainer>
  )
}
