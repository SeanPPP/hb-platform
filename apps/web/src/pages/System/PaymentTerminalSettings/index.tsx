import {
  EditOutlined,
  DeleteOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
  WalletOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  Divider,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Row,
  Segmented,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
  message,
  type TableProps,
} from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MeasuredTable } from '../../../components/MeasuredTable'
import PageContainer from '../../../components/PageContainer'
import {
  activateLinklyConfiguration,
  createLinklyTerminal,
  deleteLinklyDeviceSelection,
  getLinklyTerminals,
  getPaymentTerminalSettings,
  saveSquareToken,
  updateLinklyDeviceSelection,
  updateLinklyTerminal,
} from '../../../services/paymentTerminalSettingsService'
import { useAuthStore } from '../../../store/auth'
import type {
  LinklyPairingState,
  LinklyTerminalAdminDto,
  LinklyTerminalDeviceAdminDto,
  LinklyTerminalManagementDto,
  PaymentTerminalEnvironment,
  PaymentTerminalEnvironmentStatusDto,
  PaymentTerminalSettingsDto,
} from '../../../types/paymentTerminalSettings'
import {
  buildCreateLinklyTerminalPayload,
  buildSquareTokenPayload,
  buildUpdateLinklyTerminalPayload,
  canActivateLinklyConfiguration,
  createLinklyTerminalFormValues,
  createSquareTokenFormValues,
  getEnvironmentStatus,
  getLinklyTerminalAssignmentOwner,
  isConfiguredStatus,
  resolvePaymentTerminalSettingsErrorMessage,
  type LinklyTerminalFormValues,
  type SquareTokenFormValues,
} from './pageLogic'

const ENVIRONMENTS: PaymentTerminalEnvironment[] = ['Production', 'Sandbox']

function StatusTag({ status }: { status?: PaymentTerminalEnvironmentStatusDto }) {
  const { t } = useTranslation()
  return isConfiguredStatus(status)
    ? <Tag color="green">{t('paymentTerminalSettings.configured')}</Tag>
    : <Tag>{t('paymentTerminalSettings.notConfigured')}</Tag>
}

function PairingStateTag({ state }: { state: LinklyPairingState }) {
  const { t } = useTranslation()
  const colors: Record<LinklyPairingState, string> = {
    Ready: 'green',
    Unpaired: 'default',
    Unknown: 'orange',
    NeedsRepair: 'red',
  }
  return <Tag color={colors[state]}>{t(`paymentTerminalSettings.pairingStates.${state}`)}</Tag>
}

export default function PaymentTerminalSettingsPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const canManageSettings = access.hasPermission('System.ManageSettings')
  const [squareForm] = Form.useForm<SquareTokenFormValues>()
  const [terminalForm] = Form.useForm<LinklyTerminalFormValues>()
  const [environment, setEnvironment] = useState<PaymentTerminalEnvironment>('Production')
  const [settings, setSettings] = useState<PaymentTerminalSettingsDto | null>(null)
  const [linklyManagement, setLinklyManagement] = useState<LinklyTerminalManagementDto | null>(null)
  const [selectedStoreCode, setSelectedStoreCode] = useState<string>()
  const [editingTerminal, setEditingTerminal] = useState<LinklyTerminalAdminDto | null>(null)
  const [terminalModalOpen, setTerminalModalOpen] = useState(false)
  const [settingsLoading, setSettingsLoading] = useState(false)
  const [linklyLoading, setLinklyLoading] = useState(false)
  const [savingSquare, setSavingSquare] = useState(false)
  const [savingTerminal, setSavingTerminal] = useState(false)
  const [activating, setActivating] = useState(false)
  const [savingSelectionDevice, setSavingSelectionDevice] = useState<string>()
  const settingsRequestSequence = useRef(0)
  const linklyRequestSequence = useRef(0)

  const squareStatus = useMemo(
    () => getEnvironmentStatus(settings?.square ?? [], environment),
    [settings, environment],
  )

  const loadSettings = async (storeCode = selectedStoreCode) => {
    const requestSequence = ++settingsRequestSequence.current
    setSettingsLoading(true)
    try {
      const result = await getPaymentTerminalSettings(storeCode)
      if (requestSequence === settingsRequestSequence.current) {
        setSettings(result)
        setSelectedStoreCode(result.selectedStoreCode ?? result.stores[0]?.storeCode)
      }
    } catch (error) {
      if (requestSequence === settingsRequestSequence.current) {
        console.error(error)
        message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.loadFailed')))
      }
    } finally {
      if (requestSequence === settingsRequestSequence.current) {
        setSettingsLoading(false)
      }
    }
  }

  const loadLinklyManagement = async (storeCode: string, targetEnvironment: PaymentTerminalEnvironment) => {
    const requestSequence = ++linklyRequestSequence.current
    setLinklyLoading(true)
    try {
      const result = await getLinklyTerminals(storeCode, targetEnvironment)
      if (requestSequence === linklyRequestSequence.current) {
        setLinklyManagement(result)
      }
    } catch (error) {
      if (requestSequence === linklyRequestSequence.current) {
        console.error(error)
        setLinklyManagement(null)
        message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.loadFailed')))
      }
    } finally {
      if (requestSequence === linklyRequestSequence.current) {
        setLinklyLoading(false)
      }
    }
  }

  useEffect(() => {
    void loadSettings()
  }, [])

  useEffect(() => {
    squareForm.setFieldsValue(createSquareTokenFormValues())
  }, [environment, squareForm])

  useEffect(() => {
    if (!selectedStoreCode) {
      linklyRequestSequence.current += 1
      setLinklyManagement(null)
      setLinklyLoading(false)
      return
    }
    void loadLinklyManagement(selectedStoreCode, environment)
  }, [selectedStoreCode, environment])

  const handleStoreChange = (storeCode: string) => {
    linklyRequestSequence.current += 1
    setSelectedStoreCode(storeCode)
    void loadSettings(storeCode)
  }

  const handleEnvironmentChange = (value: string | number) => {
    linklyRequestSequence.current += 1
    setEnvironment(value as PaymentTerminalEnvironment)
  }

  const handleRefresh = () => {
    void loadSettings(selectedStoreCode)
    if (selectedStoreCode) {
      void loadLinklyManagement(selectedStoreCode, environment)
    }
  }

  const handleSaveSquare = async () => {
    const values = await squareForm.validateFields()
    const requestSequence = ++settingsRequestSequence.current
    setSettingsLoading(false)
    setSavingSquare(true)
    try {
      const result = await saveSquareToken(buildSquareTokenPayload(environment, values), selectedStoreCode)
      if (requestSequence === settingsRequestSequence.current) {
        setSettings(result)
        setSelectedStoreCode(result.selectedStoreCode ?? selectedStoreCode)
        squareForm.setFieldsValue(createSquareTokenFormValues())
      }
      message.success(t('paymentTerminalSettings.saveSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.saveFailed')))
    } finally {
      setSavingSquare(false)
    }
  }

  const openCreateTerminal = () => {
    setEditingTerminal(null)
    terminalForm.setFieldsValue(createLinklyTerminalFormValues())
    setTerminalModalOpen(true)
  }

  const openEditTerminal = (terminal: LinklyTerminalAdminDto) => {
    setEditingTerminal(terminal)
    terminalForm.setFieldsValue(createLinklyTerminalFormValues(terminal))
    setTerminalModalOpen(true)
  }

  const handleSaveTerminal = async () => {
    if (!selectedStoreCode) {
      message.warning(t('paymentTerminalSettings.selectStoreRequired'))
      return
    }

    const values = await terminalForm.validateFields()
    const requestSequence = ++linklyRequestSequence.current
    setLinklyLoading(false)
    setSavingTerminal(true)
    try {
      const result = editingTerminal
        ? await updateLinklyTerminal(
          editingTerminal.terminalId,
          buildUpdateLinklyTerminalPayload(selectedStoreCode, environment, values),
        )
        : await createLinklyTerminal(
          buildCreateLinklyTerminalPayload(selectedStoreCode, environment, values),
        )
      if (requestSequence === linklyRequestSequence.current) {
        setLinklyManagement(result)
        setLinklyLoading(false)
      }
      setTerminalModalOpen(false)
      setEditingTerminal(null)
      terminalForm.resetFields()
      message.success(t('paymentTerminalSettings.saveSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.saveFailed')))
    } finally {
      setSavingTerminal(false)
    }
  }

  const handleDeviceSelection = async (device: LinklyTerminalDeviceAdminDto, terminalId: string) => {
    if (!selectedStoreCode) return

    const requestSequence = ++linklyRequestSequence.current
    setLinklyLoading(false)
    setSavingSelectionDevice(device.deviceCode)
    try {
      const result = await updateLinklyDeviceSelection(device.deviceCode, {
        storeCode: selectedStoreCode,
        environment,
        terminalId,
        ...(device.revision > 0 ? { expectedRevision: device.revision } : {}),
      })
      if (requestSequence === linklyRequestSequence.current) {
        setLinklyManagement(result)
        setLinklyLoading(false)
      }
      message.success(t('paymentTerminalSettings.selectionSaved'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.saveFailed')))
      if (requestSequence === linklyRequestSequence.current) {
        await loadLinklyManagement(selectedStoreCode, environment)
      }
    } finally {
      setSavingSelectionDevice(undefined)
    }
  }

  const handleDeleteDeviceSelection = async (device: LinklyTerminalDeviceAdminDto) => {
    if (!selectedStoreCode || !device.terminalId || device.revision <= 0) return

    const requestSequence = ++linklyRequestSequence.current
    setLinklyLoading(false)
    setSavingSelectionDevice(device.deviceCode)
    try {
      const result = await deleteLinklyDeviceSelection(device.deviceCode, {
        storeCode: selectedStoreCode,
        environment,
        expectedRevision: device.revision,
      })
      if (requestSequence === linklyRequestSequence.current) {
        setLinklyManagement(result)
        setLinklyLoading(false)
      }
      message.success(t('paymentTerminalSettings.selectionReleased'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.saveFailed')))
      if (requestSequence === linklyRequestSequence.current) {
        await loadLinklyManagement(selectedStoreCode, environment)
      }
    } finally {
      setSavingSelectionDevice(undefined)
    }
  }

  const handleActivate = async () => {
    if (!selectedStoreCode) return

    const requestSequence = ++linklyRequestSequence.current
    setLinklyLoading(false)
    setActivating(true)
    try {
      const result = await activateLinklyConfiguration({ storeCode: selectedStoreCode, environment })
      if (requestSequence === linklyRequestSequence.current) {
        setLinklyManagement(result)
        setLinklyLoading(false)
      }
      message.success(t('paymentTerminalSettings.activationSuccess'))
    } catch (error) {
      console.error(error)
      message.error(resolvePaymentTerminalSettingsErrorMessage(error, t('paymentTerminalSettings.activationFailed')))
    } finally {
      setActivating(false)
    }
  }

  const terminalColumns: TableProps<LinklyTerminalAdminDto>['columns'] = [
    { title: t('paymentTerminalSettings.laneNo'), dataIndex: 'laneNo', width: 88 },
    { title: t('paymentTerminalSettings.terminalName'), dataIndex: 'displayName', width: 180 },
    {
      title: t('paymentTerminalSettings.credential'),
      dataIndex: 'usernameMasked',
      width: 190,
      render: (value: string, terminal) => (
        <Space size={6}>
          <Typography.Text code>{value || '--'}</Typography.Text>
          {terminal.hasPassword ? <Tag color="blue">{t('paymentTerminalSettings.passwordSaved')}</Tag> : null}
        </Space>
      ),
    },
    {
      title: t('paymentTerminalSettings.pairingState'),
      dataIndex: 'pairingState',
      width: 128,
      render: (value: LinklyPairingState) => <PairingStateTag state={value} />,
    },
    {
      title: t('paymentTerminalSettings.health'),
      dataIndex: 'lastHealthStatus',
      width: 130,
      render: (value: string | null | undefined) => value
        ? <Tag color={value.toLowerCase() === 'healthy' ? 'green' : 'orange'}>{value}</Tag>
        : <Typography.Text type="secondary">--</Typography.Text>,
    },
    {
      title: t('paymentTerminalSettings.selectedDevices'),
      dataIndex: 'selectedDeviceCount',
      width: 110,
      align: 'right',
    },
    {
      title: t('common.action'),
      key: 'actions',
      fixed: 'right',
      width: 90,
      render: (_, terminal) => (
        <Button
          type="link"
          icon={<EditOutlined />}
          onClick={() => openEditTerminal(terminal)}
          disabled={!canManageSettings}
        >
          {t('common.edit')}
        </Button>
      ),
    },
  ]

  const deviceColumns: TableProps<LinklyTerminalDeviceAdminDto>['columns'] = [
    { title: t('paymentTerminalSettings.deviceCode'), dataIndex: 'deviceCode', width: 180 },
    { title: t('paymentTerminalSettings.deviceSystem'), dataIndex: 'deviceSystem', width: 130 },
    {
      title: t('paymentTerminalSettings.deviceStatus'),
      dataIndex: 'enabled',
      width: 110,
      render: (enabled: boolean, device) => device.deviceMissing
        ? <Tag color="red">{t('paymentTerminalSettings.deviceMissing')}</Tag>
        : enabled
          ? <Tag color="green">{t('paymentTerminalSettings.deviceEnabled')}</Tag>
          : <Tag>{t('paymentTerminalSettings.deviceDisabled')}</Tag>,
    },
    {
      title: t('paymentTerminalSettings.initialTerminal'),
      dataIndex: 'terminalId',
      render: (terminalId: string | null | undefined, device) => (
        <Select
          style={{ width: '100%', minWidth: 220 }}
          value={terminalId ?? undefined}
          placeholder={t('paymentTerminalSettings.selectTerminal')}
          options={(linklyManagement?.terminals ?? []).map((terminal) => {
            const owner = getLinklyTerminalAssignmentOwner(
              linklyManagement,
              terminal.terminalId,
              device.deviceCode,
            )
            return {
              value: terminal.terminalId,
              label: owner
                ? `${terminal.displayName} · Lane ${terminal.laneNo} · ${t('paymentTerminalSettings.assignedToDevice', { deviceCode: owner })}`
                : `${terminal.displayName} · Lane ${terminal.laneNo}`,
              disabled: Boolean(owner),
            }
          })}
          onChange={(value) => void handleDeviceSelection(device, value)}
          loading={savingSelectionDevice === device.deviceCode}
          disabled={!canManageSettings || !device.enabled || linklyManagement?.terminals.length === 0}
        />
      ),
    },
    {
      title: t('common.action'),
      key: 'actions',
      width: 130,
      render: (_, device) => !device.enabled && device.terminalId && device.revision > 0 ? (
        <Popconfirm
          title={t('paymentTerminalSettings.releaseSelectionConfirmTitle')}
          description={t('paymentTerminalSettings.releaseSelectionConfirmDescription', { deviceCode: device.deviceCode })}
          onConfirm={() => void handleDeleteDeviceSelection(device)}
          okText={t('paymentTerminalSettings.releaseSelection')}
          cancelText={t('common.cancel')}
          disabled={!canManageSettings}
        >
          <Button
            type="link"
            danger
            icon={<DeleteOutlined />}
            loading={savingSelectionDevice === device.deviceCode}
            disabled={!canManageSettings}
          >
            {t('paymentTerminalSettings.releaseSelection')}
          </Button>
        </Popconfirm>
      ) : <Typography.Text type="secondary">--</Typography.Text>,
    },
  ]

  const mode = linklyManagement?.mode ?? 'Legacy'
  const canActivate = canManageSettings && canActivateLinklyConfiguration(linklyManagement)
  const hasDuplicateAssignments = linklyManagement?.terminals.some(
    (terminal) => terminal.selectedDeviceCount > 1,
  ) ?? false

  return (
    <PageContainer
      title={t('paymentTerminalSettings.title')}
      subtitle={t('paymentTerminalSettings.subtitle')}
      extra={(
        <Button icon={<ReloadOutlined />} onClick={handleRefresh} loading={settingsLoading || linklyLoading}>
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
            onChange={handleEnvironmentChange}
          />
          <Select
            style={{ minWidth: 240 }}
            placeholder={t('paymentTerminalSettings.store')}
            value={selectedStoreCode}
            options={(settings?.stores ?? []).map((store) => ({
              value: store.storeCode,
              label: `${store.storeCode} - ${store.storeName}`,
            }))}
            onChange={handleStoreChange}
            loading={settingsLoading}
          />
        </Space>

        <Row gutter={[16, 16]}>
          <Col xs={24} xl={8}>
            <Card
              loading={settingsLoading}
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
                <Form.Item label={t('paymentTerminalSettings.clearToken')} name="clearToken" valuePropName="checked">
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

          <Col xs={24} xl={16}>
            <Card
              loading={linklyLoading}
              title={(
                <Space wrap>
                  <WalletOutlined />
                  <span>{t('paymentTerminalSettings.linklyTerminalsTitle')}</span>
                  <Tag color={mode === 'Active' ? 'green' : mode === 'Draft' ? 'gold' : 'default'}>
                    {t(`paymentTerminalSettings.configurationModes.${mode}`)}
                  </Tag>
                </Space>
              )}
              extra={(
                <Space wrap>
                  <Button
                    icon={<PlusOutlined />}
                    onClick={openCreateTerminal}
                    disabled={!canManageSettings || !selectedStoreCode}
                  >
                    {t('paymentTerminalSettings.addTerminal')}
                  </Button>
                  <Popconfirm
                    title={t('paymentTerminalSettings.activateConfirmTitle')}
                    description={t('paymentTerminalSettings.activateConfirmDescription')}
                    onConfirm={() => void handleActivate()}
                    okText={t('paymentTerminalSettings.activate')}
                    cancelText={t('common.cancel')}
                    disabled={!canActivate}
                  >
                    <Button type="primary" loading={activating} disabled={!canActivate}>
                      {t('paymentTerminalSettings.activate')}
                    </Button>
                  </Popconfirm>
                </Space>
              )}
            >
              <Alert
                showIcon
                type={mode === 'Active' ? 'success' : mode === 'Draft' ? 'warning' : 'info'}
                message={t(`paymentTerminalSettings.modeDescriptions.${mode}`)}
                description={t('paymentTerminalSettings.oneCredentialPerTerminal')}
                style={{ marginBottom: 16 }}
              />
              {hasDuplicateAssignments ? (
                <Alert
                  showIcon
                  type="error"
                  message={t('paymentTerminalSettings.duplicateAssignmentWarning')}
                  style={{ marginBottom: 16 }}
                />
              ) : null}
              <MeasuredTable
                metricId="system.payment-terminal-settings.terminals"
                rowKey="terminalId"
                columns={terminalColumns}
                dataSource={linklyManagement?.terminals ?? []}
                pagination={false}
                size="small"
                scroll={{ x: 980 }}
                locale={{ emptyText: t('paymentTerminalSettings.noTerminals') }}
              />

              <Divider orientation="left">{t('paymentTerminalSettings.deviceSelectionsTitle')}</Divider>
              <Typography.Paragraph type="secondary">
                {t('paymentTerminalSettings.deviceSelectionsDescription')}
              </Typography.Paragraph>
              <MeasuredTable
                metricId="system.payment-terminal-settings.device-selections"
                rowKey="deviceCode"
                columns={deviceColumns}
                dataSource={linklyManagement?.devices ?? []}
                pagination={false}
                size="small"
                scroll={{ x: 860 }}
                locale={{ emptyText: t('paymentTerminalSettings.noDevices') }}
              />
            </Card>
          </Col>
        </Row>
      </Space>

      <Modal
        open={terminalModalOpen}
        title={editingTerminal
          ? t('paymentTerminalSettings.editTerminalTitle', { name: editingTerminal.displayName })
          : t('paymentTerminalSettings.addTerminalTitle')}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        onOk={() => void handleSaveTerminal()}
        onCancel={() => {
          setTerminalModalOpen(false)
          setEditingTerminal(null)
          terminalForm.resetFields()
        }}
        confirmLoading={savingTerminal}
        destroyOnClose
      >
        {editingTerminal ? (
          <Alert
            showIcon
            type="warning"
            message={t('paymentTerminalSettings.editCredentialHint', {
              username: editingTerminal.usernameMasked,
            })}
            style={{ marginBottom: 16 }}
          />
        ) : null}
        <Form form={terminalForm} layout="vertical" initialValues={createLinklyTerminalFormValues(editingTerminal)}>
          <Form.Item
            label={t('paymentTerminalSettings.laneNo')}
            name="laneNo"
            rules={[{ required: true, message: t('paymentTerminalSettings.validation.laneNo') }]}
          >
            <InputNumber min={1} max={9999} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            label={t('paymentTerminalSettings.terminalName')}
            name="displayName"
            rules={[{ required: true, whitespace: true, message: t('paymentTerminalSettings.validation.terminalName') }]}
          >
            <Input maxLength={128} />
          </Form.Item>
          <Form.Item
            label={t('paymentTerminalSettings.username')}
            name="username"
            extra={editingTerminal ? t('paymentTerminalSettings.blankKeepsCredential') : undefined}
            rules={editingTerminal
              ? []
              : [{ required: true, whitespace: true, message: t('paymentTerminalSettings.validation.username') }]}
          >
            <Input autoComplete="off" maxLength={128} />
          </Form.Item>
          <Form.Item
            label={t('paymentTerminalSettings.password')}
            name="password"
            extra={editingTerminal ? t('paymentTerminalSettings.blankKeepsCredential') : undefined}
            rules={editingTerminal
              ? []
              : [{ required: true, whitespace: true, message: t('paymentTerminalSettings.validation.password') }]}
          >
            <Input.Password autoComplete="new-password" maxLength={512} />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
