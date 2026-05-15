import { Button, Result } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'

export default function ForbiddenPage() {
  const navigate = useNavigate()
  const { t } = useTranslation()

  return (
    <Result
      status="403"
      title="403"
      subTitle={t('forbidden.subTitle')}
      extra={
        <Button type="primary" onClick={() => navigate('/dashboard')}>
          {t('forbidden.backToDashboard')}
        </Button>
      }
    />
  )
}
