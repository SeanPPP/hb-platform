import {
  CheckCircleOutlined,
  DeleteOutlined,
  MailOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
  SendOutlined,
} from '@ant-design/icons'
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
  createNewInvoiceEmailAccountFormValue,
  ensureInvoiceEmailDefaultAccount,
  resolveInvoiceEmailSettingsErrorMessage,
  setInvoiceEmailDefaultAccount,
  type InvoiceEmailAccountFormValues,
  type InvoiceEmailSettingsFormValues,
} from './pageLogic'

const EMAIL_REGEXP = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

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
  const watchedAccounts = Form.useWatch('accounts', form) as InvoiceEmailAccountFormValues[] | undefined
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testingAccountIndex, setTestingAccountIndex] = useState<number | null>(null)
  const [settings, setSettings] = useState<InvoiceEmailSettingsDto | null>(null)

  const getAccountValues = () => (form.getFieldValue('accounts') ?? []) as InvoiceEmailAccountFormValues[]

  const loadSettings = async () => {
    setLoading(true)
    try {
      const result = await getInvoiceEmailSettings()
      setSettings(result)
      form.setFieldsValue(createInvoiceEmailSettingsFormValues(result))
    } catch (error) {
      console.error(error)
      message.error(resolveInvoiceEmailSettingsErrorMessage(error, t('invoiceEmailSettings.loadFailed')))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadSettings()
  }, [])

  const handleSave = async () => {
    await form.validateFields()
    const values: InvoiceEmailSettingsFormValues = {
      accounts: getAccountValues(),
    }
    setSaving(true)
    try {
      const result = await saveInvoiceEmailSettings(buildInvoiceEmailSettingsSavePayload(values))
      setSettings(result)
      form.setFieldsValue(createInvoiceEmailSettingsFormValues(result))
      message.success(t('invoiceEmailSettings.saveSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolveInvoiceEmailSettingsErrorMessage(error, t('invoiceEmailSettings.saveFailed')))
    } finally {
      setSaving(false)
    }
  }

  const handleAddAccount = () => {
    const accounts = getAccountValues()
    form.setFieldsValue({
      accounts: [
        ...accounts,
        createNewInvoiceEmailAccountFormValue(accounts.length, {
          defaultName: t('invoiceEmailSettings.defaultAccountName'),
          accountNamePrefix: t('invoiceEmailSettings.accountNamePrefix'),
        }),
      ],
    })
  }

  const handleRemoveAccount = (index: number) => {
    const accounts = getAccountValues()
    if (accounts.length <= 1) {
      message.warning(t('invoiceEmailSettings.keepOneAccount'))
      return
    }

    const nextAccounts = accounts.filter((_, accountIndex) => accountIndex !== index)
    form.setFieldsValue({
      accounts: ensureInvoiceEmailDefaultAccount(nextAccounts, 0),
    })
  }

  const handleSetDefaultAccount = (index: number) => {
    form.setFieldsValue({
      accounts: setInvoiceEmailDefaultAccount(getAccountValues(), index),
    })
  }

  const validateAccountForTest = async (index: number) => {
    const field = (name: string) => ['accounts', index, name] as const
    await form.validateFields([
      field('name'),
      field('host'),
      field('port'),
      field('fromEmail'),
      field('maxAttachmentMegabytes'),
    ])

    const account = getAccountValues()[index]
    const testToEmail = account?.testToEmail?.trim()
    if (!testToEmail) {
      throw new Error(t('invoiceEmailSettings.validation.testToEmail'))
    }
    if (!EMAIL_REGEXP.test(testToEmail)) {
      throw new Error(t('invoiceEmailSettings.validation.email'))
    }

    return account
  }

  const handleSendTest = async (index: number) => {
    setTestingAccountIndex(index)
    try {
      const account = await validateAccountForTest(index)
      const result = await sendInvoiceEmailSettingsTestEmail(buildInvoiceEmailSettingsTestPayload(account))
      message.success(result.message || t('invoiceEmailSettings.testSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolveInvoiceEmailSettingsErrorMessage(error, t('invoiceEmailSettings.testFailed')))
    } finally {
      setTestingAccountIndex(null)
    }
  }

  const accountCount = watchedAccounts?.length ?? settings?.accounts.length ?? 0

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
          <Space size={12} wrap>
            <MailOutlined />
            <Typography.Text strong>{t('invoiceEmailSettings.accountCount')}</Typography.Text>
            <Tag>{accountCount}</Tag>
            <Typography.Text type="secondary">{t('invoiceEmailSettings.defaultAccountHint')}</Typography.Text>
          </Space>

          <Alert
            showIcon
            type="info"
            message={t('invoiceEmailSettings.passwordHint')}
          />

          <Form
            form={form}
            layout="vertical"
            disabled={loading}
            initialValues={{
              accounts: [createNewInvoiceEmailAccountFormValue(0, {
                defaultName: t('invoiceEmailSettings.defaultAccountName'),
                accountNamePrefix: t('invoiceEmailSettings.accountNamePrefix'),
              })],
            }}
          >
            <Form.List name="accounts">
              {(fields) => (
                <Space direction="vertical" size={16} style={{ width: '100%' }}>
                  {fields.map((field, index) => {
                    const account = getAccountValues()[index]
                    const isDefault = Boolean(account?.isDefault)
                    const hasPassword = Boolean(account?.hasPassword)

                    return (
                      <div
                        key={field.key}
                        style={{
                          border: '1px solid #f0f0f0',
                          borderRadius: 8,
                          padding: 16,
                        }}
                      >
                        <Form.Item name={[field.name, 'id']} hidden>
                          <Input />
                        </Form.Item>

                        <Space direction="vertical" size={12} style={{ width: '100%' }}>
                          <Row gutter={[16, 8]} align="middle">
                            <Col flex="auto">
                              <Space size={8} wrap>
                                <Typography.Text strong>
                                  {t('invoiceEmailSettings.accountTitle', { index: index + 1 })}
                                </Typography.Text>
                                {isDefault ? <Tag color="blue">{t('invoiceEmailSettings.defaultAccount')}</Tag> : null}
                                <PasswordStateTag hasPassword={hasPassword} />
                              </Space>
                            </Col>
                            <Col>
                              <Space wrap>
                                <Button
                                  icon={<CheckCircleOutlined />}
                                  onClick={() => handleSetDefaultAccount(index)}
                                  disabled={!canManageSettings || isDefault}
                                >
                                  {t('invoiceEmailSettings.setDefault')}
                                </Button>
                                <Button
                                  icon={<SendOutlined />}
                                  onClick={() => void handleSendTest(index)}
                                  loading={testingAccountIndex === index}
                                  disabled={!canManageSettings}
                                >
                                  {t('invoiceEmailSettings.sendTest')}
                                </Button>
                                <Button
                                  danger
                                  icon={<DeleteOutlined />}
                                  onClick={() => handleRemoveAccount(index)}
                                  disabled={!canManageSettings || fields.length <= 1}
                                >
                                  {t('common.delete')}
                                </Button>
                              </Space>
                            </Col>
                          </Row>

                          <Row gutter={16}>
                            <Col xs={24} md={12}>
                              <Form.Item
                                label={t('invoiceEmailSettings.accountName')}
                                name={[field.name, 'name']}
                                rules={[{ required: true, message: t('invoiceEmailSettings.validation.accountName') }]}
                              >
                                <Input />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={12}>
                              <Form.Item
                                label={t('invoiceEmailSettings.host')}
                                name={[field.name, 'host']}
                                rules={[{ required: true, message: t('invoiceEmailSettings.validation.host') }]}
                              >
                                <Input />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item
                                label={t('invoiceEmailSettings.port')}
                                name={[field.name, 'port']}
                                rules={[{ required: true, message: t('invoiceEmailSettings.validation.port') }]}
                              >
                                <InputNumber min={1} max={65535} style={{ width: '100%' }} />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item label={t('invoiceEmailSettings.username')} name={[field.name, 'username']}>
                                <Input autoComplete="off" />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item label={t('invoiceEmailSettings.password')} name={[field.name, 'password']}>
                                <Input.Password autoComplete="new-password" />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={12}>
                              <Form.Item
                                label={t('invoiceEmailSettings.fromEmail')}
                                name={[field.name, 'fromEmail']}
                                rules={[
                                  { required: true, message: t('invoiceEmailSettings.validation.fromEmail') },
                                  { type: 'email', message: t('invoiceEmailSettings.validation.email') },
                                ]}
                              >
                                <Input />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={12}>
                              <Form.Item label={t('invoiceEmailSettings.fromName')} name={[field.name, 'fromName']}>
                                <Input />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item
                                label={t('invoiceEmailSettings.maxAttachmentBytes')}
                                name={[field.name, 'maxAttachmentMegabytes']}
                                rules={[{ required: true, message: t('invoiceEmailSettings.validation.maxAttachmentBytes') }]}
                              >
                                <InputNumber min={0.01} precision={2} style={{ width: '100%' }} />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item
                                label={t('invoiceEmailSettings.testToEmail')}
                                name={[field.name, 'testToEmail']}
                              >
                                <Input />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item
                                label={t('invoiceEmailSettings.useSsl')}
                                name={[field.name, 'useSsl']}
                                valuePropName="checked"
                              >
                                <Switch />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item
                                label={t('invoiceEmailSettings.checkCertificateRevocation')}
                                name={[field.name, 'checkCertificateRevocation']}
                                valuePropName="checked"
                              >
                                <Switch />
                              </Form.Item>
                            </Col>
                            <Col xs={24} md={8}>
                              <Form.Item
                                label={t('invoiceEmailSettings.clearPassword')}
                                name={[field.name, 'clearPassword']}
                                valuePropName="checked"
                              >
                                <Switch />
                              </Form.Item>
                            </Col>
                          </Row>
                        </Space>
                      </div>
                    )
                  })}

                  <Button
                    icon={<PlusOutlined />}
                    onClick={handleAddAccount}
                    disabled={!canManageSettings}
                  >
                    {t('invoiceEmailSettings.addAccount')}
                  </Button>
                </Space>
              )}
            </Form.List>

            <Space style={{ marginTop: 16 }}>
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
          </Form>
        </Space>
      </Card>
    </PageContainer>
  )
}
