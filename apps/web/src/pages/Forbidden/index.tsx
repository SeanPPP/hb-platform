import { Button, Result } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../store/auth'
import { getDefaultWebPath, WEB_NO_ACCESS_PATH } from '../../utils/webPortalAccess'

export default function ForbiddenPage() {
  const navigate = useNavigate()
  const { t } = useTranslation()
  const { access, logout } = useAuthStore()
  const homePage = getDefaultWebPath(access)
  const hasWebPortal = homePage !== WEB_NO_ACCESS_PATH

  const handleAction = async () => {
    if (hasWebPortal) {
      navigate(homePage)
      return
    }
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <Result
      status="403"
      title="403"
      subTitle={t('forbidden.subTitle')}
      extra={
        <Button type="primary" onClick={() => void handleAction()}>
          {hasWebPortal
            ? t(homePage === '/shop' ? 'forbidden.backToShop' : 'forbidden.backToDashboard')
            : t('webAccessDenied.logout')}
        </Button>
      }
    />
  )
}
