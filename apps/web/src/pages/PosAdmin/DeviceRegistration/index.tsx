import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Alert,
  Button,
  Card,
  Col,
  Descriptions,
  Form,
  Input,
  Modal,
  Popconfirm,
  QRCode,
  Row,
  Segmented,
  Select,
  Space,
  Spin,
  Statistic,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  activateDevice,
  createEmergencyLoginGrant,
  disableDevice,
  getAppDeviceStatuses,
  getAppDeviceStatusSummary,
  getDeviceRegistrationDetail,
  getDeviceRegistrations,
  getEmergencyLoginGrant,
  getStoreOptions,
  isDeviceRuntimeOnline,
  lockDevice,
  revokeEmergencyLoginGrant,
  updateDeviceRegistration,
} from '../../../services/deviceRegistrationService'
import type {
  AppDeviceOnlineState,
  AppDeviceStatus,
  AppDeviceStatusSummary,
  DeviceRegistrationDetail,
  DeviceRegistrationItem,
  EmergencyLoginGrantSummary,
  StoreOption,
  UpdateDeviceRegistrationPayload,
} from '../../../types/deviceRegistration'
import { useAuthStore } from '../../../store/auth'

const STATUS_COLOR_MAP: Record<number, string> = {
  [-1]: 'gold',
  0: 'default',
  1: 'green',
  2: 'red',
  3: 'blue',
}

const DEVICE_TYPE_OPTIONS = ['Mobile', 'PDA', 'POS', 'Admin']
const DEVICE_SYSTEM_OPTIONS = ['Android', 'iOS', 'Windows', 'Mac']
const APP_ONLINE_STATE_OPTIONS: AppDeviceOnlineState[] = ['all', 'online', 'offline']
const APP_USAGE_PAGE_SIZE = 200
const EMPTY_VALUE = '--'

type DeviceRegistrationViewMode = 'registered' | 'appUsage'

const DEVICE_TYPE_COLOR_MAP: Record<string, string> = {
  mobile: 'blue',
  pda: 'purple',
  pos: 'volcano',
  admin: 'gold',
}

const DEVICE_SYSTEM_COLOR_MAP: Record<string, string> = {
  android: 'green',
  ios: 'magenta',
  windows: 'geekblue',
  mac: 'cyan',
}

const APP_UPDATE_SOURCE_COLOR_MAP: Record<string, string> = {
  ota: 'green',
  embedded: 'blue',
  unknown: 'default',
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return '--'
  }

  const timestamp = Date.parse(value)
  if (Number.isNaN(timestamp)) {
    return value
  }

  return new Date(timestamp).toLocaleString()
}

function getTagColor(value: string, colorMap: Record<string, string>) {
  return colorMap[value.trim().toLowerCase()] ?? 'default'
}

function renderDeviceTypeTag(value?: string | null) {
  return value ? <Tag color={getTagColor(value, DEVICE_TYPE_COLOR_MAP)}>{value}</Tag> : '--'
}

function renderDeviceSystemTag(value?: string | null) {
  return value ? <Tag color={getTagColor(value, DEVICE_SYSTEM_COLOR_MAP)}>{value}</Tag> : EMPTY_VALUE
}

function getUpdateTail(updateId?: string | null) {
  const value = updateId?.trim()
  if (!value) {
    return EMPTY_VALUE
  }
  return value.length <= 10 ? value : `...${value.slice(-10)}`
}

function renderAppUpdateId(value?: string | null) {
  const updateId = value?.trim()
  if (!updateId) {
    return EMPTY_VALUE
  }

  return (
    <Tooltip title={updateId}>
      <Typography.Text copyable={{ text: updateId }}>
        {getUpdateTail(updateId)}
      </Typography.Text>
    </Tooltip>
  )
}

function getAppUpdateSourceLabel(
  value: string | null | undefined,
  t: (key: string) => string
) {
  const normalized = value?.trim().toLowerCase()
  if (!normalized) {
    return EMPTY_VALUE
  }

  if (normalized === 'ota' || normalized === 'embedded' || normalized === 'unknown') {
    return t(`posAdmin.devices.appUpdateSources.${normalized}`)
  }

  return value?.trim() || EMPTY_VALUE
}

function renderAppUpdateSourceTag(
  value: string | null | undefined,
  t: (key: string) => string
) {
  const normalized = value?.trim().toLowerCase()
  if (!normalized) {
    return EMPTY_VALUE
  }

  return (
    <Tag color={APP_UPDATE_SOURCE_COLOR_MAP[normalized] ?? 'default'}>
      {getAppUpdateSourceLabel(value, t)}
    </Tag>
  )
}

function getAppPackageVersion(item: AppDeviceStatus) {
  if (item.appVersion && item.appBuildVersion) {
    return `${item.appVersion} (${item.appBuildVersion})`
  }
  return item.appVersion || item.appBuildVersion || EMPTY_VALUE
}

function getAppDeviceUser(item: AppDeviceStatus, fallback: string) {
  return item.lastSeenUserFullName || item.lastSeenUsername || item.lastSeenUserGuid || fallback
}

function renderRuntimeStatus(record: DeviceRegistrationItem, t: ReturnType<typeof useTranslation>['t']) {
  const online = isDeviceRuntimeOnline(record)
  return (
    <Space direction="vertical" size={0}>
      <Tag color={online ? 'green' : 'default'}>
        {online ? t('posAdmin.devices.online') : t('posAdmin.devices.offline')}
      </Tag>
      <Typography.Text type="secondary">
        {formatDateTime(record.lastHeartbeatAt)}
      </Typography.Text>
    </Space>
  )
}

function renderCashierStatus(record: DeviceRegistrationItem, t: ReturnType<typeof useTranslation>['t']) {
  const online = isDeviceRuntimeOnline(record)
  if (!online || !record.currentCashierName) {
    return <Tag>{t('posAdmin.devices.cashierNotLoggedIn')}</Tag>
  }

  return (
    <Space direction="vertical" size={0}>
      <Typography.Text>{record.currentCashierName}</Typography.Text>
      <Tag color="green">{t('posAdmin.devices.cashierLoggedIn')}</Tag>
      <Typography.Text type="secondary">
        {formatDateTime(record.cashierLoginAt)}
      </Typography.Text>
    </Space>
  )
}

type DeviceEditFormValues = UpdateDeviceRegistrationPayload
type EmergencyGrantFormValues = { reason: string }

export default function DeviceRegistrationPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const [editForm] = Form.useForm<DeviceEditFormValues>()
  const [emergencyForm] = Form.useForm<EmergencyGrantFormValues>()
  const [viewMode, setViewMode] = useState<DeviceRegistrationViewMode>('registered')
  const [items, setItems] = useState<DeviceRegistrationItem[]>([])
  const [appItems, setAppItems] = useState<AppDeviceStatus[]>([])
  const [appSummary, setAppSummary] = useState<AppDeviceStatusSummary>({
    total: 0,
    online: 0,
    offline: 0,
    android: 0,
    ios: 0,
    unknownSystem: 0,
  })
  const [stores, setStores] = useState<StoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [appLoading, setAppLoading] = useState(false)
  const [selectedStoreCode, setSelectedStoreCode] = useState<string>()
  const [selectedDeviceType, setSelectedDeviceType] = useState<string>()
  const [selectedDeviceSystem, setSelectedDeviceSystem] = useState<string>()
  const [selectedAppOnlineState, setSelectedAppOnlineState] = useState<AppDeviceOnlineState>('all')
  const [appKeyword, setAppKeyword] = useState('')
  const [actionDeviceId, setActionDeviceId] = useState<number | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editSaving, setEditSaving] = useState(false)
  const [editingDevice, setEditingDevice] = useState<DeviceRegistrationDetail | null>(null)
  const [emergencyOpen, setEmergencyOpen] = useState(false)
  const [emergencyLoading, setEmergencyLoading] = useState(false)
  const [emergencySaving, setEmergencySaving] = useState(false)
  const [emergencyRevoking, setEmergencyRevoking] = useState(false)
  const [emergencyGrant, setEmergencyGrant] = useState<EmergencyLoginGrantSummary | null>(null)
  const [emergencyToken, setEmergencyToken] = useState<string | null>(null)

  async function loadStores() {
    try {
      const nextStores = await getStoreOptions()
      setStores(nextStores)
    } catch (error) {
      console.error(t('posAdmin.devices.loadStoresFailed'), error)
      message.error(t('posAdmin.devices.loadStoresFailed'))
    }
  }

  async function loadDevices(showLoading = true) {
    if (showLoading) {
      setLoading(true)
    }
    try {
      const result = await getDeviceRegistrations({
        page: 1,
        pageSize: 200,
        storeCode: selectedStoreCode,
        deviceType: selectedDeviceType,
        deviceSystem: selectedDeviceSystem,
      })
      setItems(result.devices)
    } catch (error) {
      console.error(t('posAdmin.devices.loadFailed'), error)
      message.error(t('posAdmin.devices.loadFailed'))
    } finally {
      if (showLoading) {
        setLoading(false)
      }
    }
  }

  async function loadAppDevices() {
    setAppLoading(true)
    try {
      const [list, summary] = await Promise.all([
        getAppDeviceStatuses({
          page: 1,
          pageSize: APP_USAGE_PAGE_SIZE,
          storeCode: selectedStoreCode,
          deviceSystem: selectedDeviceSystem,
          onlineState: selectedAppOnlineState,
          keyword: appKeyword,
        }),
        getAppDeviceStatusSummary({
          storeCode: selectedStoreCode,
          deviceSystem: selectedDeviceSystem,
          keyword: appKeyword,
        }),
      ])
      setAppItems(list.devices)
      setAppSummary(summary)
    } catch (error) {
      console.error(t('posAdmin.devices.appUsageLoadFailed'), error)
      message.error(t('posAdmin.devices.appUsageLoadFailed'))
    } finally {
      setAppLoading(false)
    }
  }

  useEffect(() => {
    void loadStores()
  }, [])

  useEffect(() => {
    if (viewMode !== 'registered') {
      return
    }

    void loadDevices()
    const intervalId = window.setInterval(() => {
      void loadDevices(false)
    }, 15_000)

    return () => window.clearInterval(intervalId)
  }, [viewMode, selectedStoreCode, selectedDeviceType, selectedDeviceSystem])

  useEffect(() => {
    if (viewMode === 'appUsage') {
      void loadAppDevices()
    }
  }, [viewMode, selectedStoreCode, selectedDeviceSystem, selectedAppOnlineState, appKeyword])

  async function runAction(
    item: DeviceRegistrationItem,
    action: 'activate' | 'disable' | 'lock'
  ) {
    if (!access.canManageDeviceRegistration) {
      return
    }
    setActionDeviceId(item.id)
    try {
      if (action === 'activate') {
        await activateDevice(item.id)
        message.success(t('message.deviceEnabled', { deviceNo: item.systemDeviceNumber }))
      } else if (action === 'disable') {
        await disableDevice(item.id)
        message.success(t('message.deviceDisabled', { deviceNo: item.systemDeviceNumber }))
      } else {
        await lockDevice(item.id)
        message.success(t('message.deviceLocked', { deviceNo: item.systemDeviceNumber }))
      }

      await loadDevices()
    } catch (error) {
      console.error(t('message.deviceStatusFailed'), error)
      message.error(t('message.deviceStatusFailed'))
    } finally {
      setActionDeviceId(null)
    }
  }

  async function openEditModal(item: DeviceRegistrationItem) {
    if (!access.canManageDeviceRegistration) {
      return
    }

    setEditOpen(true)
    setEditLoading(true)
    setEditingDevice(null)
    editForm.resetFields()
    try {
      const detail = await getDeviceRegistrationDetail(item.id)
      setEditingDevice(detail)
      editForm.setFieldsValue({
        deviceType: detail.deviceType,
        deviceSystem: detail.deviceSystem,
        remark: detail.remark ?? '',
      })
    } catch (error) {
      console.error(t('posAdmin.devices.loadDetailFailed'), error)
      message.error(t('posAdmin.devices.loadDetailFailed'))
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  function closeEditModal() {
    setEditOpen(false)
    setEditingDevice(null)
    setEditLoading(false)
    editForm.resetFields()
  }

  async function submitEditModal() {
    if (!editingDevice) {
      return
    }

    try {
      const values = await editForm.validateFields()
      setEditSaving(true)
      await updateDeviceRegistration(editingDevice.id, {
        deviceType: values.deviceType,
        deviceSystem: values.deviceSystem,
        remark: values.remark ?? '',
      })
      message.success(t('posAdmin.devices.updateSuccess'))
      closeEditModal()
      await loadDevices()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(t('posAdmin.devices.updateFailed'), error)
      message.error(t('posAdmin.devices.updateFailed'))
    } finally {
      setEditSaving(false)
    }
  }

  async function openEmergencyModal() {
    if (
      !selectedStoreCode ||
      !access.canManageDeviceRegistration ||
      !access.canManageSystemSettings
    ) {
      return
    }

    setEmergencyOpen(true)
    setEmergencyLoading(true)
    setEmergencyGrant(null)
    setEmergencyToken(null)
    emergencyForm.resetFields()
    try {
      setEmergencyGrant(await getEmergencyLoginGrant(selectedStoreCode))
    } catch (error) {
      console.error(t('posAdmin.devices.emergencyLoadFailed'), error)
      message.error(t('posAdmin.devices.emergencyLoadFailed'))
    } finally {
      setEmergencyLoading(false)
    }
  }

  function closeEmergencyModal() {
    setEmergencyOpen(false)
    setEmergencyGrant(null)
    setEmergencyToken(null)
    emergencyForm.resetFields()
  }

  async function submitEmergencyGrant() {
    if (!selectedStoreCode) {
      return
    }

    try {
      const values = await emergencyForm.validateFields()
      setEmergencySaving(true)
      const result = await createEmergencyLoginGrant(selectedStoreCode, values.reason)
      setEmergencyGrant(result.grant)
      setEmergencyToken(result.token)
      message.success(t('posAdmin.devices.emergencyCreateSuccess'))
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(t('posAdmin.devices.emergencyCreateFailed'), error)
      message.error(t('posAdmin.devices.emergencyCreateFailed'))
    } finally {
      setEmergencySaving(false)
    }
  }

  async function revokeEmergencyGrant() {
    if (!emergencyGrant) {
      return
    }

    try {
      setEmergencyRevoking(true)
      const revoked = await revokeEmergencyLoginGrant(
        emergencyGrant.grantId,
        t('posAdmin.devices.emergencyRevokeReason')
      )
      setEmergencyGrant(revoked)
      setEmergencyToken(null)
      message.success(t('posAdmin.devices.emergencyRevokeSuccess'))
    } catch (error) {
      console.error(t('posAdmin.devices.emergencyRevokeFailed'), error)
      message.error(t('posAdmin.devices.emergencyRevokeFailed'))
    } finally {
      setEmergencyRevoking(false)
    }
  }

  async function copyEmergencyToken() {
    if (!emergencyToken) {
      return
    }
    try {
      await navigator.clipboard.writeText(emergencyToken)
      message.success(t('posAdmin.devices.emergencyCopySuccess'))
    } catch (error) {
      console.error(t('posAdmin.devices.emergencyCopyFailed'), error)
      message.error(t('posAdmin.devices.emergencyCopyFailed'))
    }
  }

  function downloadEmergencyQrCode() {
    const canvas = document.querySelector<HTMLCanvasElement>('#emergency-login-qr canvas')
    if (!canvas || !emergencyGrant) {
      message.error(t('posAdmin.devices.emergencyDownloadFailed'))
      return
    }

    const link = document.createElement('a')
    link.download = `hbpos-emergency-${emergencyGrant.storeCode}-${emergencyGrant.businessDate}.png`
    link.href = canvas.toDataURL('image/png')
    link.click()
  }

  const storeNameMap = useMemo(
    () =>
      stores.reduce<Record<string, string>>((accumulator, store) => {
        accumulator[store.storeCode] = store.storeName
        return accumulator
      }, {}),
    [stores]
  )

  const columns = useMemo<ColumnsType<DeviceRegistrationItem>>(() => {
    const baseColumns: ColumnsType<DeviceRegistrationItem> = [
      {
        title: t('posAdmin.devices.deviceNo'),
        dataIndex: 'systemDeviceNumber',
        width: 180,
      },
      {
        title: t('posAdmin.devices.hardwareId'),
        dataIndex: 'hardwareId',
        ellipsis: true,
      },
      {
        title: t('column.store'),
        dataIndex: 'storeCode',
        width: 180,
        render: (value: string | null | undefined) =>
          value ? `${value}${storeNameMap[value] ? ` / ${storeNameMap[value]}` : ''}` : '--',
      },
      {
        title: t('posAdmin.devices.deviceType'),
        dataIndex: 'deviceType',
        width: 120,
        render: (value: string | null | undefined) => renderDeviceTypeTag(value),
      },
      {
        title: t('posAdmin.devices.deviceSystem'),
        dataIndex: 'deviceSystem',
        width: 120,
        render: (value: string | null | undefined) => renderDeviceSystemTag(value),
      },
      {
        title: t('posAdmin.devices.onlineStatus'),
        key: 'onlineStatus',
        width: 150,
        render: (_value, record) => renderRuntimeStatus(record, t),
      },
      {
        title: t('posAdmin.devices.currentCashier'),
        key: 'currentCashier',
        width: 180,
        render: (_value, record) => renderCashierStatus(record, t),
      },
      {
        title: t('column.status'),
        dataIndex: 'statusDescription',
        width: 120,
        render: (_value: string, record) => (
          <Tag color={STATUS_COLOR_MAP[record.status] ?? 'default'}>
            {record.statusDescription || record.status}
          </Tag>
        ),
      },
      {
        title: t('column.createTime'),
        dataIndex: 'createdAt',
        width: 180,
        render: (value: string | undefined) => formatDateTime(value),
      },
      {
        title: t('posAdmin.devices.lastModified'),
        dataIndex: 'lastModified',
        width: 180,
        render: (value: string | null | undefined) => formatDateTime(value),
      },
    ]

    if (!access.canManageDeviceRegistration) {
      return baseColumns
    }

    return [
      ...baseColumns,
      {
        title: t('column.action'),
        key: 'actions',
        width: 300,
        fixed: 'right',
        render: (_value, record) => (
          <Space wrap>
            <Button size="small" onClick={() => void openEditModal(record)}>
              {t('common.edit')}
            </Button>
            <Button
              type="primary"
              size="small"
              loading={actionDeviceId === record.id}
              onClick={() => void runAction(record, 'activate')}
            >
              {t('posAdmin.devices.enable')}
            </Button>
            <Button
              size="small"
              loading={actionDeviceId === record.id}
              onClick={() => void runAction(record, 'disable')}
            >
              {t('posAdmin.devices.disable')}
            </Button>
            <Button
              danger
              size="small"
              loading={actionDeviceId === record.id}
              onClick={() => void runAction(record, 'lock')}
            >
              {t('posAdmin.devices.lock')}
            </Button>
          </Space>
        ),
      },
    ]
  }, [access.canManageDeviceRegistration, actionDeviceId, storeNameMap, t])

  const appColumns = useMemo<ColumnsType<AppDeviceStatus>>(() => [
    {
      title: t('posAdmin.devices.deviceNo'),
      dataIndex: 'systemDeviceNumber',
      width: 190,
      render: (_value: string | undefined, record) => (
        <Space direction="vertical" size={0}>
          <Typography.Text strong>{record.systemDeviceNumber || record.hardwareId}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {record.hardwareId}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: t('column.store'),
      dataIndex: 'storeCode',
      width: 180,
      render: (value: string | undefined) =>
        value ? `${value}${storeNameMap[value] ? ` / ${storeNameMap[value]}` : ''}` : EMPTY_VALUE,
    },
    {
      title: t('posAdmin.devices.appUsageStatus'),
      dataIndex: 'isOnline',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'green' : 'default'}>
          {t(value ? 'posAdmin.devices.appOnline' : 'posAdmin.devices.appOffline')}
        </Tag>
      ),
    },
    {
      title: t('posAdmin.devices.deviceSystem'),
      dataIndex: 'deviceSystem',
      width: 110,
      render: (_value: string | undefined, record) => renderDeviceSystemTag(record.deviceSystem || record.platform),
    },
    {
      title: t('posAdmin.devices.appPackageVersion'),
      width: 170,
      render: (_value, record) => getAppPackageVersion(record),
    },
    {
      title: t('posAdmin.devices.appRuntime'),
      dataIndex: 'runtimeVersion',
      width: 150,
      ellipsis: true,
      render: (value: string | undefined) => value || EMPTY_VALUE,
    },
    {
      title: t('posAdmin.devices.appChannel'),
      dataIndex: 'channel',
      width: 130,
      ellipsis: true,
      render: (value: string | undefined) => value || EMPTY_VALUE,
    },
    {
      title: t('posAdmin.devices.appUpdateSource'),
      dataIndex: 'updateSource',
      width: 120,
      render: (value: string | undefined) => renderAppUpdateSourceTag(value, t),
    },
    {
      title: t('posAdmin.devices.appUpdateId'),
      dataIndex: 'updateId',
      width: 160,
      render: (value: string | undefined) => renderAppUpdateId(value),
    },
    {
      title: t('posAdmin.devices.appLastUser'),
      width: 160,
      ellipsis: true,
      render: (_value, record) => getAppDeviceUser(record, t('posAdmin.devices.appNoRecentUser')),
    },
    {
      title: t('posAdmin.devices.appAuthMode'),
      dataIndex: 'lastAuthMode',
      width: 120,
      render: (value: string | undefined) => value || EMPTY_VALUE,
    },
    {
      title: t('posAdmin.devices.appLastSeen'),
      dataIndex: 'lastSeenAtUtc',
      width: 180,
      render: (value: string | undefined) => formatDateTime(value),
    },
  ], [storeNameMap, t])

  const renderStore = (device: DeviceRegistrationDetail) =>
    device.storeCode ? `${device.storeCode}${device.storeName ? ` / ${device.storeName}` : ''}` : '--'

  const refreshCurrentView = () => {
    if (viewMode === 'appUsage') {
      void loadAppDevices()
      return
    }

    void loadDevices()
  }

  return (
    <>
      <Card
        title={t(viewMode === 'appUsage' ? 'posAdmin.devices.appUsageTitle' : 'posAdmin.devices.title')}
        extra={
          <Space wrap>
            <Segmented<DeviceRegistrationViewMode>
              value={viewMode}
              onChange={setViewMode}
              options={[
                { label: t('posAdmin.devices.viewRegistered'), value: 'registered' },
                { label: t('posAdmin.devices.viewAppUsage'), value: 'appUsage' },
              ]}
            />
            <Select
              allowClear
              placeholder={t('posAdmin.devices.filterByStore')}
              style={{ width: 240 }}
              value={selectedStoreCode}
              onChange={(value) => setSelectedStoreCode(value)}
              options={stores.map((store) => ({
                label: `${store.storeCode} / ${store.storeName}`,
                value: store.storeCode,
              }))}
            />
            {viewMode === 'registered' ? (
              <Select
                allowClear
                placeholder={t('posAdmin.devices.filterByDeviceType')}
                style={{ width: 160 }}
                value={selectedDeviceType}
                onChange={(value) => setSelectedDeviceType(value)}
                options={DEVICE_TYPE_OPTIONS.map((deviceType) => ({
                  label: renderDeviceTypeTag(deviceType),
                  value: deviceType,
                }))}
              />
            ) : null}
            <Select
              allowClear
              placeholder={t('posAdmin.devices.filterByDeviceSystem')}
              style={{ width: 160 }}
              value={selectedDeviceSystem}
              onChange={(value) => setSelectedDeviceSystem(value)}
              options={DEVICE_SYSTEM_OPTIONS.map((deviceSystem) => ({
                label: renderDeviceSystemTag(deviceSystem),
                value: deviceSystem,
              }))}
            />
            {viewMode === 'appUsage' ? (
              <>
                <Select<AppDeviceOnlineState>
                  placeholder={t('posAdmin.devices.filterByOnline')}
                  style={{ width: 140 }}
                  value={selectedAppOnlineState}
                  onChange={(value) => setSelectedAppOnlineState(value)}
                  options={APP_ONLINE_STATE_OPTIONS.map((onlineState) => ({
                    label: t(`posAdmin.devices.appOnlineFilters.${onlineState}`),
                    value: onlineState,
                  }))}
                />
                <Input.Search
                  allowClear
                  placeholder={t('posAdmin.devices.appSearchPlaceholder')}
                  style={{ width: 220 }}
                  onSearch={(value) => setAppKeyword(value.trim())}
                  onChange={(event) => {
                    if (!event.target.value) {
                      setAppKeyword('')
                    }
                  }}
                />
              </>
            ) : null}
            {viewMode === 'registered' &&
            access.canManageDeviceRegistration &&
            access.canManageSystemSettings ? (
              <Tooltip
                title={!selectedStoreCode ? t('posAdmin.devices.emergencySelectStoreFirst') : undefined}
              >
                <Button
                  danger
                  disabled={!selectedStoreCode}
                  onClick={() => void openEmergencyModal()}
                >
                  {t('posAdmin.devices.emergencyAction')}
                </Button>
              </Tooltip>
            ) : null}
            <Button onClick={refreshCurrentView}>{t('common.refresh')}</Button>
          </Space>
        }
      >
        {viewMode === 'appUsage' ? (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Typography.Text type="secondary">
              {t('posAdmin.devices.appUsageNote')}
            </Typography.Text>
            <Row gutter={[12, 12]}>
              <Col xs={12} md={6}>
                <Statistic title={t('posAdmin.devices.appSummaryTotal')} value={appSummary.total} />
              </Col>
              <Col xs={12} md={6}>
                <Statistic title={t('posAdmin.devices.appSummaryOnline')} value={appSummary.online} />
              </Col>
              <Col xs={12} md={6}>
                <Statistic title={t('posAdmin.devices.appSummaryAndroid')} value={appSummary.android} />
              </Col>
              <Col xs={12} md={6}>
                <Statistic title={t('posAdmin.devices.appSummaryIos')} value={appSummary.ios} />
              </Col>
            </Row>
            <Table<AppDeviceStatus>
              rowKey={(record) => record.id || record.hardwareId}
              loading={appLoading}
              columns={appColumns}
              dataSource={appItems}
              scroll={{ x: 1600 }}
              pagination={false}
            />
          </Space>
        ) : (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Typography.Text type="secondary">
              {t('posAdmin.devices.deviceNote')}
            </Typography.Text>
            <Table<DeviceRegistrationItem>
              rowKey="id"
              loading={loading}
              columns={columns}
              dataSource={items}
              scroll={{ x: access.canManageDeviceRegistration ? 1650 : 1350 }}
              pagination={false}
            />
          </Space>
        )}
      </Card>

      <Modal
        open={editOpen}
        title={t('posAdmin.devices.editTitle')}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={editSaving}
        okButtonProps={{ disabled: editLoading || !editingDevice }}
        onOk={() => void submitEditModal()}
        onCancel={closeEditModal}
        destroyOnHidden
        width={760}
      >
        <Spin spinning={editLoading}>
          {editingDevice ? (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2} size="small">
              <Descriptions.Item label={t('posAdmin.devices.deviceNo')}>
                {editingDevice.systemDeviceNumber || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('posAdmin.devices.hardwareId')}>
                {editingDevice.hardwareId || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.store')}>
                {renderStore(editingDevice)}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.status')}>
                <Tag color={STATUS_COLOR_MAP[editingDevice.status] ?? 'default'}>
                  {editingDevice.statusDescription || String(editingDevice.status)}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('column.createTime')}>
                {formatDateTime(editingDevice.createdAt)}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.creator')}>
                {editingDevice.createdBy || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('posAdmin.devices.lastModified')}>
                {formatDateTime(editingDevice.lastModified)}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.updater')}>
                {editingDevice.lastModifiedBy || '--'}
              </Descriptions.Item>
            </Descriptions>

            <Form form={editForm} layout="vertical">
              <Form.Item
                name="deviceType"
                label={t('posAdmin.devices.deviceType')}
                rules={[{ required: true, message: t('posAdmin.devices.deviceTypeRequired') }]}
              >
                <Select
                  options={DEVICE_TYPE_OPTIONS.map((deviceType) => ({
                    label: renderDeviceTypeTag(deviceType),
                    value: deviceType,
                  }))}
                />
              </Form.Item>
              <Form.Item
                name="deviceSystem"
                label={t('posAdmin.devices.deviceSystem')}
                rules={[{ required: true, message: t('posAdmin.devices.deviceSystemRequired') }]}
              >
                <Select
                  options={DEVICE_SYSTEM_OPTIONS.map((deviceSystem) => ({
                    label: renderDeviceSystemTag(deviceSystem),
                    value: deviceSystem,
                  }))}
                />
              </Form.Item>
              <Form.Item name="remark" label={t('column.remarks')}>
                <Input.TextArea
                  rows={3}
                  maxLength={500}
                  showCount
                  placeholder={t('posAdmin.devices.remarkPlaceholder')}
                />
              </Form.Item>
            </Form>
          </Space>
          ) : null}
        </Spin>
      </Modal>

      <Modal
        open={emergencyOpen}
        title={t('posAdmin.devices.emergencyTitle')}
        onCancel={closeEmergencyModal}
        destroyOnHidden
        width={680}
        footer={
          emergencyToken ? (
            <Space>
              <Button onClick={() => void copyEmergencyToken()}>
                {t('posAdmin.devices.emergencyCopy')}
              </Button>
              <Button onClick={downloadEmergencyQrCode}>
                {t('posAdmin.devices.emergencyDownload')}
              </Button>
              <Button type="primary" onClick={closeEmergencyModal}>
                {t('common.close')}
              </Button>
            </Space>
          ) : emergencyGrant?.status === 'Active' ? (
            <Space>
              <Popconfirm
                title={t('posAdmin.devices.emergencyRevokeConfirm')}
                onConfirm={() => void revokeEmergencyGrant()}
              >
                <Button danger loading={emergencyRevoking}>
                  {t('posAdmin.devices.emergencyRevoke')}
                </Button>
              </Popconfirm>
              <Button onClick={closeEmergencyModal}>{t('common.close')}</Button>
            </Space>
          ) : (
            <Space>
              <Button onClick={closeEmergencyModal}>{t('common.cancel')}</Button>
              <Button
                danger
                type="primary"
                loading={emergencySaving}
                disabled={emergencyLoading}
                onClick={() => void submitEmergencyGrant()}
              >
                {t('posAdmin.devices.emergencyCreate')}
              </Button>
            </Space>
          )
        }
      >
        <Spin spinning={emergencyLoading}>
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Alert
              type="warning"
              showIcon
              message={t('posAdmin.devices.emergencyWarningTitle')}
              description={t('posAdmin.devices.emergencyWarningDescription')}
            />
            <Descriptions bordered column={1} size="small">
              <Descriptions.Item label={t('column.store')}>
                {selectedStoreCode
                  ? `${selectedStoreCode}${storeNameMap[selectedStoreCode] ? ` / ${storeNameMap[selectedStoreCode]}` : ''}`
                  : EMPTY_VALUE}
              </Descriptions.Item>
              {emergencyGrant ? (
                <>
                  <Descriptions.Item label={t('posAdmin.devices.emergencyStatus')}>
                    <Tag color={emergencyGrant.status === 'Active' ? 'red' : 'default'}>
                      {t(`posAdmin.devices.emergencyStatuses.${emergencyGrant.status}`)}
                    </Tag>
                  </Descriptions.Item>
                  <Descriptions.Item label={t('posAdmin.devices.emergencyGrantId')}>
                    <Typography.Text copyable>{emergencyGrant.grantId}</Typography.Text>
                  </Descriptions.Item>
                  <Descriptions.Item label={t('posAdmin.devices.emergencyExpiresAt')}>
                    {formatDateTime(emergencyGrant.expiresAtUtc)}
                  </Descriptions.Item>
                  <Descriptions.Item label={t('posAdmin.devices.emergencyReason')}>
                    {emergencyGrant.reason || EMPTY_VALUE}
                  </Descriptions.Item>
                </>
              ) : null}
            </Descriptions>

            {emergencyToken ? (
              <Space direction="vertical" align="center" size={12} style={{ width: '100%' }}>
                <div id="emergency-login-qr">
                  <QRCode value={emergencyToken} size={320} errorLevel="M" />
                </div>
                <Alert
                  type="info"
                  showIcon
                  message={t('posAdmin.devices.emergencyTokenOneTime')}
                />
              </Space>
            ) : emergencyGrant?.status === 'Active' ? (
              <Alert type="info" showIcon message={t('posAdmin.devices.emergencyActiveSummary')} />
            ) : (
              <Form form={emergencyForm} layout="vertical">
                <Form.Item
                  name="reason"
                  label={t('posAdmin.devices.emergencyReason')}
                  rules={[
                    { required: true, message: t('posAdmin.devices.emergencyReasonRequired') },
                    { max: 200, message: t('posAdmin.devices.emergencyReasonTooLong') },
                  ]}
                >
                  <Input.TextArea rows={3} maxLength={200} showCount />
                </Form.Item>
              </Form>
            )}
          </Space>
        </Spin>
      </Modal>
    </>
  )
}
