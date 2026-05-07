import { LockOutlined, LoginOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, Space, Typography, message } from 'antd'
import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../store/auth'
import type { LoginRequest } from '../../types/auth'
import { hashPassword } from '../../utils/password'
import type { RequestError } from '../../utils/request'

export default function LoginPage() {
  const [form] = Form.useForm<LoginRequest>()
  const [errorMessage, setErrorMessage] = useState('')
  const navigate = useNavigate()
  const location = useLocation()
  const { login, loginLoading } = useAuthStore()

  const handleSubmit = async (values: LoginRequest) => {
    setErrorMessage('')
    try {
      await login({
        username: values.username,
        password: hashPassword(values.password),
      })
      message.success('登录成功')
      const target = (location.state as { from?: { pathname?: string } } | undefined)?.from?.pathname
      navigate(target || '/dashboard', { replace: true })
    } catch (error) {
      const requestError = error as RequestError
      setErrorMessage(requestError.message || '登录失败')
    }
  }

  return (
    <div className="login-page">
      <div className="login-hero">
        <Space direction="vertical" size={16}>
          <Typography.Title level={1} style={{ margin: 0, color: '#fff' }}>
            HB 管理后台
          </Typography.Title>
          <Typography.Paragraph style={{ color: 'rgba(255,255,255,0.88)', margin: 0 }}>
            旧版 Umi 前端正在迁移到新的 React + Vite 框架。
            这一阶段先完成登录、权限、系统管理和新的 Tabs 机制接入。
          </Typography.Paragraph>
        </Space>
      </div>

          <Card className="login-card" variant="borderless">
        <Space direction="vertical" size={20} style={{ width: '100%' }}>
          <div>
            <Typography.Title level={3} style={{ marginBottom: 8 }}>
              欢迎登录
            </Typography.Title>
            <Typography.Text type="secondary">
              使用后端 .NET 接口进行 Cookie 登录认证
            </Typography.Text>
          </div>

          {errorMessage ? <Alert type="error" showIcon message={errorMessage} /> : null}

          <Form<LoginRequest>
            form={form}
            layout="vertical"
            initialValues={{ username: '', password: '' }}
            onFinish={(values) => void handleSubmit(values)}
          >
            <Form.Item label="用户名" name="username" rules={[{ required: true, message: '请输入用户名' }]}>
              <Input prefix={<UserOutlined />} placeholder="请输入用户名" />
            </Form.Item>
            <Form.Item label="密码" name="password" rules={[{ required: true, message: '请输入密码' }]}>
              <Input.Password prefix={<LockOutlined />} placeholder="请输入密码" />
            </Form.Item>
            <Button
              type="primary"
              htmlType="submit"
              loading={loginLoading}
              icon={<LoginOutlined />}
              block
            >
              登录
            </Button>
          </Form>
        </Space>
      </Card>
    </div>
  )
}
