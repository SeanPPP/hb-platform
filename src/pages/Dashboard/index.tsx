import { Card, Col, List, Row, Statistic, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../components/PageContainer'
import { useAuthStore } from '../../store/auth'

export default function DashboardPage() {
  const { currentUser } = useAuthStore()
  const { t } = useTranslation()

  const userPermCount = currentUser?.permissions?.length ?? 0

  const cards = [
    { title: t('dashboard.currentUser'), value: currentUser?.username || '--', simple: true },
    { title: t('dashboard.roleCount'), value: currentUser?.roleNames?.length ?? 0 },
    { title: t('dashboard.permCount'), value: userPermCount },
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
        <Col xs={24}>
          <Card title={t('dashboard.currentRoles')}>
            <List
              dataSource={currentUser?.roleNames ?? []}
              renderItem={(item) => <List.Item>{item}</List.Item>}
              locale={{ emptyText: t('dashboard.noRoleInfo') }}
            />
          </Card>
        </Col>
      </Row>
    </PageContainer>
  )
}
