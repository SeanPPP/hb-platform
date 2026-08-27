import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Alert,
  Button,
  Descriptions,
  Form,
  Input,
  Modal,
  QRCode,
  Select,
  Space,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { MeasuredTable } from '../../../components/MeasuredTable'
import {
  createDeviceActivationCode,
  getDeviceActivationCodes,
  getDeviceActivationManageableStores,
  revokeDeviceActivationCode,
} from '../../../services/deviceActivationCodeService'
import type {
  DeviceActivationCodeCreatePayload,
  DeviceActivationCodeSummary,
  DeviceActivationManageableStore,
  DeviceActivationStatus,
  DeviceActivationSystem,
} from '../../../types/deviceActivationCode'
import {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from '../../../utils/latestRequestGuard'

const PAGE_SIZE = 30
const EMPTY_VALUE = '--'
const DEVICE_SYSTEMS: DeviceActivationSystem[] = ['Windows', 'iPadOS', 'Android', 'iOS']
const STATUSES: DeviceActivationStatus[] = ['Available', 'Consumed', 'Expired', 'Revoked']
const CREATE_DEFAULTS: Pick<DeviceActivationCodeCreatePayload, 'deviceSystem' | 'validForMinutes'> = {
  deviceSystem: 'Windows',
  validForMinutes: 1440,
}

const STATUS_COLORS: Record<DeviceActivationStatus, string> = {
  Available: 'green',
  Consumed: 'blue',
  Expired: 'default',
  Revoked: 'red',
}

type CreateFormValues = DeviceActivationCodeCreatePayload
type RevokeFormValues = { reason: string }

interface DeviceActivationCodePanelProps {
  canManage: boolean
}

function formatDateTime(value?: string | null) {
  if (!value) return EMPTY_VALUE
  const timestamp = Date.parse(value)
  return Number.isNaN(timestamp) ? value : new Date(timestamp).toLocaleString()
}

export default function DeviceActivationCodePanel({ canManage }: DeviceActivationCodePanelProps) {
  const { t } = useTranslation()
  const [createForm] = Form.useForm<CreateFormValues>()
  const [revokeForm] = Form.useForm<RevokeFormValues>()
  const [items, setItems] = useState<DeviceActivationCodeSummary[]>([])
  const [stores, setStores] = useState<DeviceActivationManageableStore[]>([])
  const [loading, setLoading] = useState(false)
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [storeCode, setStoreCode] = useState<string>()
  const [deviceSystem, setDeviceSystem] = useState<DeviceActivationSystem>()
  const [status, setStatus] = useState<DeviceActivationStatus>()
  const [createOpen, setCreateOpen] = useState(false)
  const [createSaving, setCreateSaving] = useState(false)
  const [createdGrant, setCreatedGrant] = useState<DeviceActivationCodeSummary | null>(null)
  const [createdCode, setCreatedCode] = useState<string | null>(null)
  const [revokeGrant, setRevokeGrant] = useState<DeviceActivationCodeSummary | null>(null)
  const [revokeSaving, setRevokeSaving] = useState(false)
  const listRequestGuardRef = useRef(createLatestRequestGuard())

  const load = useCallback(async () => {
    await runLatestGuardedRequest(listRequestGuardRef.current, () => getDeviceActivationCodes({
        page,
        pageSize: PAGE_SIZE,
        storeCode,
        deviceSystem,
        status,
      }), {
        onStart: () => setLoading(true),
        onSuccess: (result) => {
          setItems(result.items)
          setTotal(result.total)
        },
        onError: (error) => {
          console.error(t('posAdmin.devices.activation.loadFailed'), error)
          message.error(t('posAdmin.devices.activation.loadFailed'))
        },
        onSettled: () => setLoading(false),
      })
  }, [deviceSystem, page, status, storeCode, t])

  useEffect(() => {
    void getDeviceActivationManageableStores()
      .then(setStores)
      .catch((error) => {
        console.error(t('posAdmin.devices.activation.loadStoresFailed'), error)
        message.error(t('posAdmin.devices.activation.loadStoresFailed'))
      })
  }, [t])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => () => {
    listRequestGuardRef.current.invalidate()
  }, [])

  const storeNameMap = useMemo(
    () => Object.fromEntries(stores.map((store) => [store.storeCode, store.storeName])),
    [stores],
  )

  async function submitCreate() {
    try {
      const values = await createForm.validateFields()
      setCreateSaving(true)
      const result = await createDeviceActivationCode(values)
      setCreatedGrant(result.grant)
      setCreatedCode(result.activationCode)
      setCreateOpen(false)
      createForm.resetFields()
      message.success(t('posAdmin.devices.activation.createSuccess'))
      // 当前已经在第一页时主动刷新；否则只切换页码，让现有 effect 使用新页码加载，避免旧闭包重复请求错误页面。
      if (page === 1) {
        void load()
      } else {
        setPage(1)
      }
    } catch (error) {
      if ((error as { errorFields?: unknown[] })?.errorFields) return
      console.error(t('posAdmin.devices.activation.createFailed'), error)
      message.error(t('posAdmin.devices.activation.createFailed'))
    } finally {
      setCreateSaving(false)
    }
  }

  function closeCreatedResult() {
    // 明文只允许停留在本次创建结果的内存中，关闭后不可恢复。
    setCreatedCode(null)
    setCreatedGrant(null)
  }

  async function copyCreatedCode() {
    if (!createdCode) return
    try {
      await navigator.clipboard.writeText(createdCode)
      message.success(t('posAdmin.devices.activation.copySuccess'))
    } catch (error) {
      console.error(t('posAdmin.devices.activation.copyFailed'), error)
      message.error(t('posAdmin.devices.activation.copyFailed'))
    }
  }

  function downloadCreatedQrCode() {
    const canvas = document.querySelector<HTMLCanvasElement>('#device-activation-code-qr canvas')
    if (!canvas || !createdGrant) {
      message.error(t('posAdmin.devices.activation.downloadFailed'))
      return
    }
    const link = document.createElement('a')
    link.download = `hbpos-device-${createdGrant.storeCode}-${createdGrant.deviceSystem}.png`
    link.href = canvas.toDataURL('image/png')
    link.click()
  }

  async function submitRevoke() {
    if (!revokeGrant) return
    try {
      const values = await revokeForm.validateFields()
      setRevokeSaving(true)
      await revokeDeviceActivationCode(revokeGrant.grantId, values.reason)
      setRevokeGrant(null)
      revokeForm.resetFields()
      message.success(t('posAdmin.devices.activation.revokeSuccess'))
      await load()
    } catch (error) {
      if ((error as { errorFields?: unknown[] })?.errorFields) return
      console.error(t('posAdmin.devices.activation.revokeFailed'), error)
      message.error(t('posAdmin.devices.activation.revokeFailed'))
    } finally {
      setRevokeSaving(false)
    }
  }

  const columns = useMemo<ColumnsType<DeviceActivationCodeSummary>>(() => [
    {
      title: t('column.store'),
      width: 180,
      render: (_value, record) =>
        `${record.storeCode}${(record.storeName || storeNameMap[record.storeCode])
          ? ` / ${record.storeName || storeNameMap[record.storeCode]}`
          : ''}`,
    },
    {
      title: t('posAdmin.devices.deviceSystem'),
      dataIndex: 'deviceSystem',
      width: 110,
      render: (value: DeviceActivationSystem) => <Tag color="geekblue">{value}</Tag>,
    },
    {
      title: t('column.status'),
      dataIndex: 'status',
      width: 110,
      render: (value: DeviceActivationStatus) => (
        <Tag color={STATUS_COLORS[value]}>
          {t(`posAdmin.devices.activation.statuses.${value}`)}
        </Tag>
      ),
    },
    {
      title: t('posAdmin.devices.activation.expiresAt'),
      dataIndex: 'expiresAtUtc',
      width: 180,
      render: formatDateTime,
    },
    {
      title: t('posAdmin.devices.activation.createdAudit'),
      width: 220,
      render: (_value, record) => (
        <Space direction="vertical" size={0}>
          <Typography.Text>{record.createdBy || EMPTY_VALUE}</Typography.Text>
          <Typography.Text type="secondary">{formatDateTime(record.createdAtUtc)}</Typography.Text>
          <Typography.Text type="secondary" ellipsis={{ tooltip: record.reason }}>
            {record.reason || EMPTY_VALUE}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: t('posAdmin.devices.activation.consumedAudit'),
      width: 230,
      render: (_value, record) => record.consumedAtUtc ? (
        <Space direction="vertical" size={0}>
          <Typography.Text>{record.consumedDeviceCode || EMPTY_VALUE}</Typography.Text>
          <Typography.Text type="secondary">{formatDateTime(record.consumedAtUtc)}</Typography.Text>
          <Typography.Text type="secondary">{record.consumptionKind || EMPTY_VALUE}</Typography.Text>
        </Space>
      ) : EMPTY_VALUE,
    },
    {
      title: t('column.actions'),
      width: 100,
      fixed: 'right',
      render: (_value, record) => canManage && (
        record.status === 'Available' || record.status === 'Expired'
      ) ? (
        <Button danger type="link" onClick={() => setRevokeGrant(record)}>
          {t('posAdmin.devices.activation.revoke')}
        </Button>
      ) : EMPTY_VALUE,
    },
  ], [canManage, storeNameMap, t])

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Alert
        type="info"
        showIcon
        message={t('posAdmin.devices.activation.noteTitle')}
        description={t('posAdmin.devices.activation.noteDescription')}
      />

      <Space wrap>
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('posAdmin.devices.filterByStore')}
          style={{ width: 240 }}
          value={storeCode}
          onChange={(value) => { setPage(1); setStoreCode(value) }}
          options={stores.map((store) => ({
            label: `${store.storeCode} / ${store.storeName}`,
            value: store.storeCode,
          }))}
        />
        <Select<DeviceActivationSystem>
          allowClear
          placeholder={t('posAdmin.devices.filterByDeviceSystem')}
          style={{ width: 150 }}
          value={deviceSystem}
          onChange={(value) => { setPage(1); setDeviceSystem(value) }}
          options={DEVICE_SYSTEMS.map((value) => ({ label: value, value }))}
        />
        <Select<DeviceActivationStatus>
          allowClear
          placeholder={t('posAdmin.devices.activation.filterByStatus')}
          style={{ width: 150 }}
          value={status}
          onChange={(value) => { setPage(1); setStatus(value) }}
          options={STATUSES.map((value) => ({
            label: t(`posAdmin.devices.activation.statuses.${value}`),
            value,
          }))}
        />
        <Button onClick={() => void load()}>{t('common.refresh')}</Button>
        {canManage ? (
          <Button type="primary" onClick={() => setCreateOpen(true)}>
            {t('posAdmin.devices.activation.create')}
          </Button>
        ) : null}
      </Space>

      <MeasuredTable<DeviceActivationCodeSummary> metricId="pos-admin.device-registration.table-3"
        rowKey="grantId"
        loading={loading}
        columns={columns}
        dataSource={items}
        scroll={{ x: 1200 }}
        pagination={{
          current: page,
          pageSize: PAGE_SIZE,
          total,
          showSizeChanger: false,
          onChange: setPage,
        }}
      />

      <Modal
        open={createOpen}
        title={t('posAdmin.devices.activation.createTitle')}
        okText={t('posAdmin.devices.activation.create')}
        cancelText={t('common.cancel')}
        confirmLoading={createSaving}
        onOk={() => void submitCreate()}
        onCancel={() => {
          setCreateOpen(false)
          createForm.resetFields()
        }}
        destroyOnHidden
      >
        <Form form={createForm} layout="vertical" initialValues={CREATE_DEFAULTS}>
          <Form.Item
            name="storeCode"
            label={t('column.store')}
            rules={[{ required: true, message: t('posAdmin.devices.activation.storeRequired') }]}
          >
            <Select
              showSearch
              optionFilterProp="label"
              options={stores.map((store) => ({
                label: `${store.storeCode} / ${store.storeName}`,
                value: store.storeCode,
              }))}
            />
          </Form.Item>
          <Form.Item
            name="deviceSystem"
            label={t('posAdmin.devices.deviceSystem')}
            rules={[{ required: true, message: t('posAdmin.devices.deviceSystemRequired') }]}
          >
            <Select options={DEVICE_SYSTEMS.map((value) => ({ label: value, value }))} />
          </Form.Item>
          <Form.Item
            name="validForMinutes"
            label={t('posAdmin.devices.activation.validFor')}
            rules={[{ required: true }]}
          >
            <Select options={[
              { value: 30, label: t('posAdmin.devices.activation.ttl30Minutes') },
              { value: 120, label: t('posAdmin.devices.activation.ttl2Hours') },
              { value: 1440, label: t('posAdmin.devices.activation.ttl24Hours') },
            ]} />
          </Form.Item>
          <Form.Item
            name="reason"
            label={t('posAdmin.devices.activation.reason')}
            rules={[
              { required: true, message: t('posAdmin.devices.activation.reasonRequired') },
              { max: 200, message: t('posAdmin.devices.activation.reasonTooLong') },
            ]}
          >
            <Input.TextArea rows={3} maxLength={200} showCount />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={Boolean(createdCode && createdGrant)}
        title={t('posAdmin.devices.activation.createdTitle')}
        onCancel={closeCreatedResult}
        destroyOnHidden
        width={680}
        footer={(
          <Space>
            <Button onClick={() => void copyCreatedCode()}>{t('posAdmin.devices.activation.copy')}</Button>
            <Button onClick={downloadCreatedQrCode}>{t('posAdmin.devices.activation.download')}</Button>
            <Button type="primary" onClick={closeCreatedResult}>{t('common.close')}</Button>
          </Space>
        )}
      >
        {createdGrant && createdCode ? (
          <Space direction="vertical" align="center" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={1} size="small" style={{ width: '100%' }}>
              <Descriptions.Item label={t('column.store')}>
                {createdGrant.storeCode} / {createdGrant.storeName || storeNameMap[createdGrant.storeCode] || EMPTY_VALUE}
              </Descriptions.Item>
              <Descriptions.Item label={t('posAdmin.devices.deviceSystem')}>
                {createdGrant.deviceSystem}
              </Descriptions.Item>
              <Descriptions.Item label={t('posAdmin.devices.activation.expiresAt')}>
                {formatDateTime(createdGrant.expiresAtUtc)}
              </Descriptions.Item>
            </Descriptions>
            <div id="device-activation-code-qr">
              <QRCode value={createdCode} size={320} errorLevel="M" />
            </div>
            <Typography.Text code copyable={{ text: createdCode }}>{createdCode}</Typography.Text>
            <Alert type="warning" showIcon message={t('posAdmin.devices.activation.oneTimeWarning')} />
          </Space>
        ) : null}
      </Modal>

      <Modal
        open={Boolean(revokeGrant)}
        title={t('posAdmin.devices.activation.revokeTitle')}
        okText={t('posAdmin.devices.activation.revoke')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={revokeSaving}
        onOk={() => void submitRevoke()}
        onCancel={() => {
          setRevokeGrant(null)
          revokeForm.resetFields()
        }}
        destroyOnHidden
      >
        <Form form={revokeForm} layout="vertical">
          <Form.Item
            name="reason"
            label={t('posAdmin.devices.activation.revokeReason')}
            rules={[
              { required: true, message: t('posAdmin.devices.activation.revokeReasonRequired') },
              { max: 200, message: t('posAdmin.devices.activation.reasonTooLong') },
            ]}
          >
            <Input.TextArea rows={3} maxLength={200} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}
