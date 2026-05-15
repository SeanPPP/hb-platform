import {
  LockOutlined,
  LoginOutlined,
  SafetyOutlined,
  ThunderboltOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Alert, Button, Checkbox, Form, Input, Space, Typography, message } from 'antd'
import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import LanguageSwitch from '../../components/LanguageSwitch'
import { useAuthStore } from '../../store/auth'
import type { LoginRequest } from '../../types/auth'
import { hashPassword } from '../../utils/password'
import type { RequestError } from '../../utils/request'

const REMEMBERED_USERNAME_KEY = 'remembered_username'

export default function LoginPage() {
  const { t } = useTranslation()
  const [form] = Form.useForm<LoginRequest>()
  const [errorMessage, setErrorMessage] = useState('')
  const [rememberUsername, setRememberUsername] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const { login, loginLoading } = useAuthStore()

  const rememberedUsername = localStorage.getItem(REMEMBERED_USERNAME_KEY) || ''

  useEffect(() => {
    if (rememberedUsername) {
      setRememberUsername(true)
      form.setFieldsValue({ username: rememberedUsername })
    }
  }, [form])

  const handleSubmit = async (values: LoginRequest) => {
    setErrorMessage('')
    try {
      await login({
        username: values.username,
        password: hashPassword(values.password),
      })
      if (rememberUsername && values.username) {
        localStorage.setItem(REMEMBERED_USERNAME_KEY, values.username)
      } else {
        localStorage.removeItem(REMEMBERED_USERNAME_KEY)
      }
      message.success(t('login.success'))
      const redirect = searchParams.get('redirect')
      const target = (location.state as { from?: { pathname?: string } } | undefined)?.from?.pathname
      navigate(redirect || target || '/dashboard', { replace: true })
    } catch (error) {
      const requestError = error as RequestError
      setErrorMessage(requestError.message || t('login.failed'))
    }
  }

  return (
    <div className="login-container">
      <div className="login-brand-section">
        <div className="login-brand-content">
          <div className="login-brand-logo">HB</div>
          <h1 className="login-brand-title">HB Platform</h1>
          <p className="login-brand-subtitle">
            {t('login.brandSubtitle', 'Enterprise-grade business management platform')}
          </p>
          <div className="login-brand-features">
            <div className="login-brand-feature">
              <div className="login-brand-feature-icon"><SafetyOutlined /></div>
              <div className="login-brand-feature-text">{t('login.secureReliable', 'Secure & Reliable')}</div>
            </div>
            <div className="login-brand-feature">
              <div className="login-brand-feature-icon"><ThunderboltOutlined /></div>
              <div className="login-brand-feature-text">{t('login.efficientFast', 'Efficient & Fast')}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="login-form-section">
        <div className="login-form-wrapper">
          <div className="login-mobile-logo">
            <div className="login-mobile-logo-icon">HB</div>
            <div className="login-mobile-logo-text">HB Platform</div>
          </div>

          <Space direction="vertical" size={20} style={{ width: '100%' }}>
            <div className="login-form-header" style={{ position: 'relative' }}>
              <Typography.Title level={3} style={{ marginBottom: 4 }}>
                {t('login.welcome')}
              </Typography.Title>
              <Typography.Text type="secondary" style={{ fontSize: 14 }}>
                {t('login.subtitle')}
              </Typography.Text>
              <LanguageSwitch className="login-language-switch" size="small" />
            </div>

            {errorMessage ? <Alert type="error" showIcon message={errorMessage} /> : null}

            <Form<LoginRequest>
              form={form}
              layout="vertical"
              initialValues={{ username: '', password: '' }}
              onFinish={(values) => void handleSubmit(values)}
              size="large"
            >
              <Form.Item label={t('login.username')} name="username" rules={[{ required: true, message: t('login.usernameRequired') }]}>
                <Input prefix={<UserOutlined />} placeholder={t('login.usernamePlaceholder')} autoComplete="username" />
              </Form.Item>
              <Form.Item label={t('login.password')} name="password" rules={[{ required: true, message: t('login.passwordRequired') }]}>
                <Input.Password prefix={<LockOutlined />} placeholder={t('login.passwordPlaceholder')} autoComplete="current-password" />
              </Form.Item>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
                <Checkbox checked={rememberUsername} onChange={(e) => setRememberUsername(e.target.checked)}>
                  {t('login.rememberUsername')}
                </Checkbox>
              </div>
              <Button
                type="primary"
                htmlType="submit"
                loading={loginLoading}
                icon={<LoginOutlined />}
                block
                className="login-submit-btn"
              >
                {t('login.submit')}
              </Button>
            </Form>
          </Space>
        </div>
      </div>
    </div>
  )
}
