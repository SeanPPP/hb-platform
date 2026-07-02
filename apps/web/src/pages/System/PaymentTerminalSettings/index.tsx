import { ReloadOutlined, SaveOutlined, WalletOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  Form,
  Input,
  Row,
  Segmented,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
  message,
} from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  getPaymentTerminalSettings,
  saveLinklyCredential,
  saveSquareToken,
} from '../../../services/paymentTerminalSettingsService'
import { useAuthStore } from '../../../store/auth'
import type {
  LinklyCloudCredentialAdminDto,
  PaymentTerminalEnvironment,
  PaymentTerminalEnvironmentStatusDto,
  PaymentTerminalSettingsDto,
} from '../../../types/paymentTerminalSettings'
import {
  buildLinklyCredentialPayload,
  buildSquareTokenPayload,
  createLinklyCredentialFormValues,
  createSquareTokenFormValues,
  getEnvironmentStatus,
  isConfiguredStatus,
  resolvePaymentTerminalSettingsErrorMessage,
  type LinklyCredentialFormValues,
  type SquareTokenFormValues,
} from './pageLogic'

const ENVIRONMENTS: PaymentTerminalEnvironment[] = ['Production', 'Sandbox']

function StatusTag({ status }: { status?: PaymentTerminalEnvironmentStatusDto | LinklyCloudCredentialAdminDto }) {
  const { t } = useTranslation()
  return isConfiguredStatus(status)
    ? <Tag color="green">{t('paymentTerminalSettings.configured')}</Tag>
    : <Tag>{t('paymentTerminalSettings.notConfigured')}</Tag>
}

export default function PaymentTerminalSettingsPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const canManageSettings = access.hasPermission('System.ManageSettings')
  const [squareForm] = Form.useForm<SquareTokenFormValues>()
  const [linklyForm] = Form.useForm<LinklyCredentialFormValues>()
  const [environment, setEnvironment] = useState<PaymentTerminalEnvironment>('Production')
  const [settings, setSettings] = useState<PaymentTerminalSettingsDto | null>(null)
  const [selectedStoreCode, setSelectedStoreCode] = useState<string>()
  const [loading, setLoading] = useState(false)
  const [savingSquare, setSavingSquare] = useState(false)
  const [savingLinkly, setSavingLinkly] = useState(false)

  const squareStatus = useMemo(
    () => getEnvironmentStatus(settings?.square ?? [], environment),
    [settings, environment],
  )
  const linklyStatus = useMemo(
    () => getEnvironmentStatus(settings?.linkly ?? [], environment),
    [settings, environment],
  )

  const loadSettings = async (storeCode = selectedStoreCode) => {
    setLoading(true)
    try {
      const result = await getPaymentTerminalSettings(storeCode)
      setSettings(result)
      setSelectedStoreCode(result.selectedStoreCode ?? result.stores[0]?.storeCode)
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.loadFailed')))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadSettings()
  }, [])

  useEffect(() => {
    squareForm.setFieldsValue(createSquareTokenFormValues())
    linklyForm.setFieldsValue(createLinklyCredentialFormValues(linklyStatus))
  }, [environment, linklyStatus, squareForm, linklyForm])

  const handleStoreChange = (storeCode: string) => {
    setSelectedStoreCode(storeCode)
    void loadSettings(storeCode)
  }

  const handleSaveSquare = async () => {
    const values = await squareForm.validateFields()
    setSavingSquare(true)
    try {
      const result = await saveSquareToken(buildSquareTokenPayload(environment, values), selectedStoreCode)
      setSettings(result)
      setSelectedStoreCode(result.selectedStoreCode ?? selectedStoreCode)
      squareForm.setFieldsValue(createSquareTokenFormValues())
      message.success(t('paymentTerminalSettings.saveSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.saveFailed')))
    } finally {
      setSavingSquare(false)
    }
  }

  const handleSaveLinkly = async () => {
    if (!selectedStoreCode) {
      message.warning(t('paymentTerminalSettings.selectStoreRequired'))
      return
    }

    const values = await linklyForm.validateFields()
    setSavingLinkly(true)
    try {
      const result = await saveLinklyCredential(
        buildLinklyCredentialPayload(selectedStoreCode, environment, values),
      )
      setSettings(result)
      linklyForm.setFieldsValue(createLinklyCredentialFormValues(getEnvironmentStatus(result.linkly, environment)))
      message.success(t('paymentTerminalSettings.saveSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.saveFailed')))
    } finally {
      setSavingLinkly(false)
    }
  }

  return (
    <PageContainer
      title={t('paymentTerminalSettings.title')}
      subtitle={t('paymentTerminalSettings.subtitle')}
      extra={(
        <Button icon={<ReloadOutlined />} onClick={() => void loadSettings()} loading={loading}>
          {t('common.refresh')}
        </Button>
      )}
    >
      {!canManageSettings ? (
        <Alert
          showIcon
          type="warning"
          message={t('paymentTerminalSettings.noPermission')}
          style={{ marginBottom: 16 }}
        />
      ) : null}

      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Space wrap>
          <Segmented
            value={environment}
            options={ENVIRONMENTS.map((item) => ({
              label: t(`paymentTerminalSettings.environments.${item}`),
              value: item,
            }))}
            onChange={(value) => setEnvironment(value as PaymentTerminalEnvironment)}
          />
          <Select
            style={{ minWidth: 220 }}
            placeholder={t('paymentTerminalSettings.store')}
            value={selectedStoreCode}
            options={(settings?.stores ?? []).map((store) => ({
              value: store.storeCode,
              label: `${store.storeCode} - ${store.storeName}`,
            }))}
            onChange={handleStoreChange}
            loading={loading}
          />
        </Space>

        <Row gutter={[16, 16]}>
          <Col xs={24} lg={12}>
            <Card
              loading={loading}
              title={(
                <Space>
                  <WalletOutlined />
                  <span>{t('paymentTerminalSettings.squareTitle')}</span>
                  <StatusTag status={squareStatus} />
                </Space>
              )}
            >
              <Form form={squareForm} layout="vertical" initialValues={createSquareTokenFormValues()}>
                <Form.Item label={t('paymentTerminalSettings.accessToken')} name="accessToken">
                  <Input.Password autoComplete="new-password" />
                </Form.Item>
                <Form.Item
                  label={t('paymentTerminalSettings.clearToken')}
                  name="clearToken"
                  valuePropName="checked"
                >
                  <Switch />
                </Form.Item>
                <Space direction="vertical" size={12}>
                  <Typography.Text type="secondary">
                    {t('paymentTerminalSettings.updatedAt')}: {squareStatus?.updatedAtUtc ?? '--'}
                  </Typography.Text>
                  <Button
                    type="primary"
                    icon={<SaveOutlined />}
                    onClick={() => void handleSaveSquare()}
                    loading={savingSquare}
                    disabled={!canManageSettings}
                  >
                    {t('common.save')}
                  </Button>
                </Space>
              </Form>
            </Card>
          </Col>

          <Col xs={24} lg={12}>
            <Card
              loading={loading}
              title={(
                <Space>
                  <WalletOutlined />
                  <span>{t('paymentTerminalSettings.linklyTitle')}</span>
                  <StatusTag status={linklyStatus} />
                </Space>
              )}
            >
              <Form form={linklyForm} layout="vertical" initialValues={createLinklyCredentialFormValues()}>
                <Form.Item
                  label={t('paymentTerminalSettings.username')}
                  name="username"
                  rules={[
                    ({ getFieldValue }) => ({
                      validator: (_, value) => {
                        if (getFieldValue('clearCredential') || String(value ?? '').trim()) {
                          return Promise.resolve()
                        }
                        return Promise.reject(new Error(t('paymentTerminalSettings.validation.username')))
                      },
                    }),
                  ]}
                >
                  <Input autoComplete="off" />
                </Form.Item>
                <Form.Item label={t('paymentTerminalSettings.password')} name="password">
                  <Input.Password autoComplete="new-password" />
                </Form.Item>
                <Form.Item
                  label={t('paymentTerminalSettings.clearCredential')}
                  name="clearCredential"
                  valuePropName="checked"
                >
                  <Switch />
                </Form.Item>
                <Space direction="vertical" size={12}>
                  <Typography.Text type="secondary">
                    {t('paymentTerminalSettings.updatedAt')}: {linklyStatus?.updatedAtUtc ?? '--'}
                  </Typography.Text>
                  <Button
                    type="primary"
                    icon={<SaveOutlined />}
                    onClick={() => void handleSaveLinkly()}
                    loading={savingLinkly}
                    disabled={!canManageSettings || !selectedStoreCode}
                  >
                    {t('common.save')}
                  </Button>
                </Space>
              </Form>
            </Card>
          </Col>
        </Row>
      </Space>
    </PageContainer>
  )
}
