import { Card, Col, List, Row, Statistic, Tag, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../components/PageContainer'
import { useAuthStore } from '../../store/auth'

interface AccessEntry {
  label: string
  enabled: boolean
}

export default function DashboardPage() {
  const { currentUser, access } = useAuthStore()
  const { t } = useTranslation()

  const cards = [
    { title: t('dashboard.currentUser'), value: currentUser?.username || '--', simple: true },
    { title: t('dashboard.roleCount'), value: currentUser?.roleNames.length ?? 0 },
    { title: t('dashboard.permCount'), value: currentUser?.permissions.length ?? 0 },
    { title: t('dashboard.storeCount'), value: currentUser?.stores?.length ?? 0 },
  ]

  const accessEntries: AccessEntry[] = [
    { label: t('dashboard.accessUsers'), enabled: access.canReadUser },
    { label: t('dashboard.accessRoles'), enabled: access.canReadRole },
    { label: t('dashboard.accessStores'), enabled: access.canReadStore },
    { label: t('dashboard.accessWarehouse'), enabled: access.canManageWarehouse },
  ]

  return (
    <PageContainer
      title={t('dashboard.title')}
      subtitle={t('dashboard.subtitle')}
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
          <Card title={t('dashboard.currentRoles')}>
            <List
              dataSource={currentUser?.roleNames ?? []}
              renderItem={(item) => <List.Item>{item}</List.Item>}
              locale={{ emptyText: t('dashboard.noRoleInfo') }}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title={t('dashboard.systemAccess')}>
            <List
              dataSource={accessEntries}
              renderItem={(item) => (
                <List.Item>
                  <Typography.Text>{item.label}</Typography.Text>
                  <Tag color={item.enabled ? 'success' : 'default'}>
                    {item.enabled ? t('dashboard.canAccess') : t('dashboard.noPermission')}
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
