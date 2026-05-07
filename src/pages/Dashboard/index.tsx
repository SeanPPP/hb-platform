import { Card, Col, List, Row, Statistic, Tag, Typography } from 'antd'
import PageContainer from '../../components/PageContainer'
import { useAuthStore } from '../../store/auth'

interface AccessEntry {
  label: string
  enabled: boolean
}

export default function DashboardPage() {
  const { currentUser, access } = useAuthStore()

  const cards = [
    { title: '当前用户', value: currentUser?.username || '--', simple: true },
    { title: '角色数量', value: currentUser?.roleNames.length ?? 0 },
    { title: '权限数量', value: currentUser?.permissions.length ?? 0 },
    { title: '关联分店数', value: currentUser?.stores?.length ?? 0 },
  ]

  const accessEntries: AccessEntry[] = [
    { label: '用户管理', enabled: access.canReadUser },
    { label: '角色管理', enabled: access.canReadRole },
    { label: '分店管理', enabled: access.canReadStore },
    { label: '仓库管理', enabled: access.canManageWarehouse },
  ]

  return (
    <PageContainer
      title="工作台"
      subtitle="这里已经接入了当前用户、权限状态和新的路由驱动 Tabs 体系。"
      extra={<Tag color="processing">Phase 1</Tag>}
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
          <Card title="当前角色">
            <List
              dataSource={currentUser?.roleNames ?? []}
              renderItem={(item) => <List.Item>{item}</List.Item>}
              locale={{ emptyText: '暂无角色信息' }}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="系统管理访问能力">
            <List
              dataSource={accessEntries}
                  renderItem={(item) => (
                <List.Item>
                      <Typography.Text>{item.label}</Typography.Text>
                      <Tag color={item.enabled ? 'success' : 'default'}>
                        {item.enabled ? '可访问' : '无权限'}
                      </Tag>
                </List.Item>
              )}
            />
          </Card>
        </Col>
      </Row>
    </PageContainer>
  )
}
