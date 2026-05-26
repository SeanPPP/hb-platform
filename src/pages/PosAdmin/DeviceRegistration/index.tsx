import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Card, Select, Space, Table, Tag, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  activateDevice,
  disableDevice,
  getDeviceRegistrations,
  getStoreOptions,
  lockDevice,
} from '../../../services/deviceRegistrationService'
import type {
  DeviceRegistrationItem,
  StoreOption,
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

export default function DeviceRegistrationPage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const [items, setItems] = useState<DeviceRegistrationItem[]>([])
  const [stores, setStores] = useState<StoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [selectedStoreCode, setSelectedStoreCode] = useState<string>()
  const [selectedDeviceType, setSelectedDeviceType] = useState<string>()
  const [selectedDeviceSystem, setSelectedDeviceSystem] = useState<string>()
  const [actionDeviceId, setActionDeviceId] = useState<number | null>(null)

  async function loadStores() {
    try {
      const nextStores = await getStoreOptions()
      setStores(nextStores)
    } catch (error) {
      console.error(t('posAdmin.devices.loadStoresFailed'), error)
      message.error(t('posAdmin.devices.loadStoresFailed'))
    }
  }

  async function loadDevices() {
    setLoading(true)
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
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadStores()
  }, [])

  useEffect(() => {
    void loadDevices()
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
        width: 240,
        fixed: 'right',
        render: (_value, record) => (
          <Space wrap>
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

  return (
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
          scroll={{ x: access.canManageDeviceRegistration ? 1260 : 1020 }}
          pagination={false}
        />
      </Space>
    </Card>
  )
}
