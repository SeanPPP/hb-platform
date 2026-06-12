import { MailOutlined, ReloadOutlined, SaveOutlined, SendOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  Form,
  Input,
  InputNumber,
  Row,
  Space,
  Switch,
  Tag,
  Typography,
  message,
} from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  getInvoiceEmailSettings,
  saveInvoiceEmailSettings,
  sendInvoiceEmailSettingsTestEmail,
} from '../../../services/invoiceEmailSettingsService'
import { useAuthStore } from '../../../store/auth'
import type { InvoiceEmailSettingsDto } from '../../../types/invoiceEmailSettings'
import {
  buildInvoiceEmailSettingsSavePayload,
  buildInvoiceEmailSettingsTestPayload,
  createInvoiceEmailSettingsFormValues,
  type InvoiceEmailSettingsFormValues,
} from './pageLogic'

function PasswordStateTag({ hasPassword }: { hasPassword: boolean }) {
  const { t } = useTranslation()

  return hasPassword ? (
    <Tag color="green">{t('invoiceEmailSettings.passwordConfigured')}</Tag>
  ) : (
    <Tag>{t('invoiceEmailSettings.passwordNotConfigured')}</Tag>
  )
}

export default function InvoiceEmailSettingsPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const canManageSettings = access.hasPermission('System.ManageSettings')
  const [form] = Form.useForm<InvoiceEmailSettingsFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [settings, setSettings] = useState<InvoiceEmailSettingsDto | null>(null)

  const loadSettings = async () => {
    setLoading(true)
    try {
      const result = await getInvoiceEmailSettings()
      setSettings(result)
      form.setFieldsValue(createInvoiceEmailSettingsFormValues(result))
    } catch (error) {
      console.error(error)
      message.error(t('invoiceEmailSettings.loadFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadSettings()
  }, [])

  const handleSave = async () => {
    const values = await form.validateFields()
    setSaving(true)
    try {
      const result = await saveInvoiceEmailSettings(buildInvoiceEmailSettingsSavePayload(values))
      setSettings(result)
      form.setFieldsValue(createInvoiceEmailSettingsFormValues(result))
      message.success(t('invoiceEmailSettings.saveSuccess'))
    } catch (error) {
      console.error(error)
      message.error(t('invoiceEmailSettings.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  const handleSendTest = async () => {
    const values = await form.validateFields()
    setTesting(true)
    try {
      const result = await sendInvoiceEmailSettingsTestEmail(buildInvoiceEmailSettingsTestPayload(values))
      message.success(result.message || t('invoiceEmailSettings.testSuccess'))
    } catch (error) {
      console.error(error)
      message.error(t('invoiceEmailSettings.testFailed'))
    } finally {
      setTesting(false)
    }
  }

  return (
    <PageContainer
      title={t('invoiceEmailSettings.title')}
      subtitle={t('invoiceEmailSettings.subtitle')}
      extra={(
        <Space>
          <Button icon={<ReloadOutlined />} onClick={() => void loadSettings()} loading={loading}>
            {t('common.refresh')}
          </Button>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            onClick={() => void handleSave()}
            loading={saving}
            disabled={!canManageSettings}
          >
            {t('common.save')}
          </Button>
        </Space>
      )}
    >
      {!canManageSettings ? (
        <Alert
          showIcon
          type="warning"
          message={t('invoiceEmailSettings.noPermission')}
          style={{ marginBottom: 16 }}
        />
      ) : null}

      <Card loading={loading}>
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Space size={12}>
            <MailOutlined />
            <Typography.Text strong>{t('invoiceEmailSettings.passwordStatus')}</Typography.Text>
            <PasswordStateTag hasPassword=REDACTED ?? false} />
          </Space>

          <Form
            form={form}
            layout="vertical"
            disabled={loading}
            initialValues={{
              port: 25,
              useSsl: true,
              checkCertificateRevocation: true,
              clearPassword: false,
              maxAttachmentBytes: 10485760,
              testToEmail: '',
            }}
          >
            <Row gutter={16}>
              <Col xs={24} md={12}>
                <Form.Item
                  label={t('invoiceEmailSettings.host')}
                  name="host"
                  rules={[{ required: true, message: t('invoiceEmailSettings.validation.host') }]}
                >
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item
                  label={t('invoiceEmailSettings.port')}
                  name="port"
                  rules={[{ required: true, message: t('invoiceEmailSettings.validation.port') }]}
                >
                  <InputNumber min={1} max={65535} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item label={t('invoiceEmailSettings.username')} name="username">
                  <Input autoComplete="off" />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item label={t('invoiceEmailSettings.password')} name="password">
                  <Input.Password autoComplete="new-password" />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item
                  label={t('invoiceEmailSettings.fromEmail')}
                  name="fromEmail"
                  rules={[
                    { required: true, message: t('invoiceEmailSettings.validation.fromEmail') },
                    { type: 'email', message: t('invoiceEmailSettings.validation.email') },
                  ]}
                >
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item label={t('invoiceEmailSettings.fromName')} name="fromName">
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item
                  label={t('invoiceEmailSettings.maxAttachmentBytes')}
                  name="maxAttachmentBytes"
                  rules={[{ required: true, message: t('invoiceEmailSettings.validation.maxAttachmentBytes') }]}
                >
                  <InputNumber min={1} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item
                  label={t('invoiceEmailSettings.testToEmail')}
                  name="testToEmail"
                  rules={[
                    { required: true, message: t('invoiceEmailSettings.validation.testToEmail') },
                    { type: 'email', message: t('invoiceEmailSettings.validation.email') },
                  ]}
                >
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item
                  label={t('invoiceEmailSettings.useSsl')}
                  name="useSsl"
                  valuePropName="checked"
                >
                  <Switch />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item
                  label={t('invoiceEmailSettings.checkCertificateRevocation')}
                  name="checkCertificateRevocation"
                  valuePropName="checked"
                >
                  <Switch />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item
                  label={t('invoiceEmailSettings.clearPassword')}
                  name="clearPassword"
                  valuePropName="checked"
                >
                  <Switch />
                </Form.Item>
              </Col>
            </Row>

            <Alert
              showIcon
              type="info"
              message={t('invoiceEmailSettings.passwordHint')}
              style={{ marginBottom: 16 }}
            />

            <Space>
              <Button
                type="primary"
                icon={<SaveOutlined />}
                onClick={() => void handleSave()}
                loading={saving}
                disabled={!canManageSettings}
              >
                {t('common.save')}
              </Button>
              <Button
                icon={<SendOutlined />}
                onClick={() => void handleSendTest()}
                loading={testing}
                disabled={!canManageSettings}
              >
                {t('invoiceEmailSettings.sendTest')}
              </Button>
            </Space>
          </Form>
        </Space>
      </Card>
    </PageContainer>
  )
}
