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

const STATUS_COLOR_MAP: Record<number, string> = {
  [-1]: 'gold',
  0: 'default',
  1: 'green',
  2: 'red',
  3: 'blue',
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

export default function DeviceRegistrationPage() {
  const { t } = useTranslation()
  const [items, setItems] = useState<DeviceRegistrationItem[]>([])
  const [stores, setStores] = useState<StoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [selectedStoreCode, setSelectedStoreCode] = useState<string>()
  const [actionDeviceId, setActionDeviceId] = useState<number | null>(null)

  async function loadStores() {
    try {
      const nextStores = await getStoreOptions()
      setStores(nextStores)
    } catch (error) {
      console.error(t('posAdmin.devices.loadStoresFailed', '加载分店失败'), error)
      message.error(t('posAdmin.devices.loadStoresFailed', '加载分店失败'))
    }
  }

  async function loadDevices(storeCode?: string) {
    setLoading(true)
    try {
      const result = await getDeviceRegistrations({
        page: 1,
        pageSize: 200,
        storeCode,
      })
      setItems(result.devices)
    } catch (error) {
      console.error(t('posAdmin.devices.loadFailed', '加载设备失败'), error)
      message.error(t('posAdmin.devices.loadFailed', '加载设备失败'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadStores()
  }, [])

  useEffect(() => {
    void loadDevices(selectedStoreCode)
  }, [selectedStoreCode])

  async function runAction(
    item: DeviceRegistrationItem,
    action: 'activate' | 'disable' | 'lock'
  ) {
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

      await loadDevices(selectedStoreCode)
    } catch (error) {
      console.error(t('message.deviceStatusFailed', '设备状态更新失败'), error)
      message.error(t('message.deviceStatusFailed', '设备状态更新失败'))
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

  const columns = useMemo<ColumnsType<DeviceRegistrationItem>>(
    () => [
      {
        title: t('posAdmin.devices.deviceNo', '设备编号'),
        dataIndex: 'systemDeviceNumber',
        width: 180,
      },
      {
        title: t('posAdmin.devices.hardwareId', '硬件标识'),
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
        title: t('posAdmin.devices.deviceType', '设备类型'),
        dataIndex: 'deviceType',
        width: 120,
      },
      {
        title: t('posAdmin.devices.deviceSystem', '系统'),
        dataIndex: 'deviceSystem',
        width: 120,
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
        title: t('posAdmin.devices.lastModified', '最后更新'),
        dataIndex: 'lastModified',
        width: 180,
        render: (value: string | null | undefined) => formatDateTime(value),
      },
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
    ],
    [actionDeviceId, storeNameMap, t]
  )

  return (
    <Card
      title={t('posAdmin.devices.title')}
      extra={
        <Space wrap>
          <Select
            allowClear
            placeholder={t('posAdmin.devices.filterByStore', '按分店筛选')}
            style={{ width: 240 }}
            value={selectedStoreCode}
            onChange={(value) => setSelectedStoreCode(value)}
            options={stores.map((store) => ({
              label: `${store.storeCode} / ${store.storeName}`,
              value: store.storeCode,
            }))}
          />
          <Button onClick={() => void loadDevices(selectedStoreCode)}>{t('common.refresh')}</Button>
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
          scroll={{ x: 1180 }}
          pagination={false}
        />
      </Space>
    </Card>
  )
}
