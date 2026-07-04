import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  activateDevice,
  disableDevice,
  getDeviceRegistrationDetail,
  getDeviceRegistrations,
  getStoreOptions,
  isDeviceRuntimeOnline,
  lockDevice,
  updateDeviceRegistration,
} from '../../../services/deviceRegistrationService'
import type {
  DeviceRegistrationDetail,
  DeviceRegistrationItem,
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
  return value ? <Tag color={getTagColor(value, DEVICE_SYSTEM_COLOR_MAP)}>{value}</Tag> : '--'
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

export default function DeviceRegistrationPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const [editForm] = Form.useForm<DeviceEditFormValues>()
  const [items, setItems] = useState<DeviceRegistrationItem[]>([])
  const [stores, setStores] = useState<StoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [selectedStoreCode, setSelectedStoreCode] = useState<string>()
  const [selectedDeviceType, setSelectedDeviceType] = useState<string>()
  const [selectedDeviceSystem, setSelectedDeviceSystem] = useState<string>()
  const [actionDeviceId, setActionDeviceId] = useState<number | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editSaving, setEditSaving] = useState(false)
  const [editingDevice, setEditingDevice] = useState<DeviceRegistrationDetail | null>(null)

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

  useEffect(() => {
    void loadStores()
  }, [])

  useEffect(() => {
    void loadDevices()
    const intervalId = window.setInterval(() => {
      void loadDevices(false)
    }, 15_000)

    return () => window.clearInterval(intervalId)
  }, [selectedStoreCode, selectedDeviceType, selectedDeviceSystem])

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

  const renderStore = (device: DeviceRegistrationDetail) =>
    device.storeCode ? `${device.storeCode}${device.storeName ? ` / ${device.storeName}` : ''}` : '--'

  return (
    <>
      <Card
        title={t('posAdmin.devices.title')}
        extra={
          <Space wrap>
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
            <Button onClick={() => void loadDevices()}>{t('common.refresh')}</Button>
          </Space>
        }
      >
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
    </>
  )
}
