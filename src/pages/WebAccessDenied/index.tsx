import { Button, Result } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../store/auth'

export default function WebAccessDeniedPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const logout = useAuthStore((state) => state.logout)

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <Result
      status="403"
      title={t('webAccessDenied.title')}
      subTitle={t('webAccessDenied.subTitle')}
      extra={
        <Button type="primary" onClick={() => void handleLogout()}>
          {t('webAccessDenied.logout')}
        </Button>
      }
    />
  )
}
