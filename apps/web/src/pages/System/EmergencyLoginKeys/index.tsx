import { KeyOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  Modal,
  Skeleton,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import {
  activateEmergencyLoginKey,
  generateEmergencyLoginKey,
  getEmergencyLoginKeys,
  retireEmergencyLoginKey,
} from '../../../services/emergencyLoginKeyService'
import type {
  EmergencyLoginKey,
  EmergencyLoginKeyList,
  EmergencyLoginKeyMissingDevice,
} from '../../../types/emergencyLoginKey'
import {
  getEmergencyLoginKeyActionState,
  getEmergencyLoginKeyDataProtectionStatusKey,
  getLatestEmergencyLoginKeyOperator,
  getShortEmergencyLoginKeyFingerprint,
  isEmergencyLoginKeyVersionConflict,
  resolveEmergencyLoginKeyErrorMessage,
} from './logic'

type MutationOperation =
  | { type: 'generate' }
  | { type: 'activate' | 'forceActivate' | 'retire' | 'discard'; key: EmergencyLoginKey }

interface MutationFormValues {
  reason: string
  confirmKeyId?: string
}

const STATUS_COLORS: Record<string, string> = {
  Staged: 'blue',
  Active: 'green',
  Retiring: 'orange',
  Retired: 'default',
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return '-'
  }
  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm') : value
}

export default function EmergencyLoginKeysPage() {
  const { t } = useTranslation()
  const [mutationForm] = Form.useForm<MutationFormValues>()
  const [keyset, setKeyset] = useState<EmergencyLoginKeyList | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [mutationOperation, setMutationOperation] = useState<MutationOperation | null>(null)
  const [missingDevicesOpen, setMissingDevicesOpen] = useState(false)
  const localizedErrors = useMemo<Record<string, string>>(() => ({
    EMERGENCY_KEY_STAGED_ALREADY_EXISTS: t('emergencyLoginKeys.errors.EMERGENCY_KEY_STAGED_ALREADY_EXISTS'),
    EMERGENCY_KEY_DATA_PROTECTION_UNAVAILABLE: t('emergencyLoginKeys.errors.EMERGENCY_KEY_DATA_PROTECTION_UNAVAILABLE'),
    EMERGENCY_KEY_NOT_STAGED: t('emergencyLoginKeys.errors.EMERGENCY_KEY_NOT_STAGED'),
    EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE: t('emergencyLoginKeys.errors.EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE'),
    EMERGENCY_KEY_NOT_FOUND: t('emergencyLoginKeys.errors.EMERGENCY_KEY_NOT_FOUND'),
    EMERGENCY_KEY_RETIRE_STATE_INVALID: t('emergencyLoginKeys.errors.EMERGENCY_KEY_RETIRE_STATE_INVALID'),
    EMERGENCY_KEY_ACTIVE_GRANTS_EXIST: t('emergencyLoginKeys.errors.EMERGENCY_KEY_ACTIVE_GRANTS_EXIST'),
    EMERGENCY_KEY_REASON_INVALID: t('emergencyLoginKeys.errors.EMERGENCY_KEY_REASON_INVALID'),
    EMERGENCY_KEY_EXPECTED_VERSION_REQUIRED: t('emergencyLoginKeys.errors.EMERGENCY_KEY_EXPECTED_VERSION_REQUIRED'),
    EMERGENCY_KEY_OPERATION_FAILED: t('emergencyLoginKeys.errors.EMERGENCY_KEY_OPERATION_FAILED'),
  }), [t])

  const loadKeyset = useCallback(async () => {
    setLoading(true)
    setLoadError(null)
    try {
      const result = await getEmergencyLoginKeys()
      setKeyset(result)
    } catch (error) {
      console.error('Failed to load emergency login keys', error)
      setLoadError(resolveEmergencyLoginKeyErrorMessage(
        error,
        t('emergencyLoginKeys.versionConflict'),
        t('emergencyLoginKeys.loadFailed'),
        localizedErrors,
      ))
    } finally {
      setLoading(false)
    }
  }, [localizedErrors, t])

  useEffect(() => {
    void loadKeyset()
  }, [loadKeyset])

  const openMutation = (operation: MutationOperation) => {
    mutationForm.resetFields()
    setMutationOperation(operation)
  }

  const closeMutation = () => {
    if (!submitting) {
      setMutationOperation(null)
      mutationForm.resetFields()
    }
  }

  const operationTitle = useMemo(() => {
    switch (mutationOperation?.type) {
      case 'generate': return t('emergencyLoginKeys.generateTitle')
      case 'activate': return t('emergencyLoginKeys.activateTitle')
      case 'forceActivate': return t('emergencyLoginKeys.forceActivateTitle')
      case 'retire': return t('emergencyLoginKeys.retireTitle')
      case 'discard': return t('emergencyLoginKeys.discardTitle')
      default: return ''
    }
  }, [mutationOperation?.type, t])

  const operationConfirmText = useMemo(() => {
    switch (mutationOperation?.type) {
      case 'generate': return t('emergencyLoginKeys.generate')
      case 'activate': return t('emergencyLoginKeys.activate')
      case 'forceActivate': return t('emergencyLoginKeys.forceActivate')
      case 'retire': return t('emergencyLoginKeys.retire')
      case 'discard': return t('emergencyLoginKeys.discard')
      default: return t('common.confirm')
    }
  }, [mutationOperation?.type, t])

  const submitMutation = async (values: MutationFormValues) => {
    if (!mutationOperation || !keyset) {
      return
    }

    const reason = values.reason.trim()
    const expectedVersion = keyset.version
    setSubmitting(true)
    try {
      switch (mutationOperation.type) {
        case 'generate':
          await generateEmergencyLoginKey({ reason, expectedVersion })
          break
        case 'activate':
          await activateEmergencyLoginKey(mutationOperation.key.keyId, {
            reason,
            expectedVersion,
            force: false,
          })
          break
        case 'forceActivate':
          await activateEmergencyLoginKey(mutationOperation.key.keyId, {
            reason,
            expectedVersion,
            force: true,
          })
          break
        case 'retire':
        case 'discard':
          await retireEmergencyLoginKey(mutationOperation.key.keyId, { reason, expectedVersion })
          break
      }

      setMutationOperation(null)
      mutationForm.resetFields()
      await loadKeyset()
      message.success(t('emergencyLoginKeys.mutationSuccess'))
    } catch (error) {
      console.error('Emergency login key mutation failed', error)
      const conflict = isEmergencyLoginKeyVersionConflict(error)
      const errorMessage = resolveEmergencyLoginKeyErrorMessage(
        error,
        t('emergencyLoginKeys.versionConflict'),
        t('emergencyLoginKeys.mutationFailed'),
        localizedErrors,
      )
      if (conflict) {
        // 关键并发处理：发生 409 时先刷新版本，再提示管理员重新确认，禁止复用旧 expectedVersion。
        setMutationOperation(null)
        mutationForm.resetFields()
        await loadKeyset()
        message.warning(errorMessage)
      } else {
        message.error(errorMessage)
      }
    } finally {
      setSubmitting(false)
    }
  }

  const missingDeviceColumns: ColumnsType<EmergencyLoginKeyMissingDevice> = [
    {
      title: t('emergencyLoginKeys.store'),
      dataIndex: 'storeCode',
      width: 100,
      render: (value: string | null) => value || '-',
    },
    {
      title: t('emergencyLoginKeys.deviceNumber'),
      dataIndex: 'deviceNumber',
      width: 140,
    },
    {
      title: t('emergencyLoginKeys.hardwareId'),
      dataIndex: 'hardwareId',
      width: 220,
      ellipsis: true,
    },
    {
      title: t('emergencyLoginKeys.lastOnline'),
      dataIndex: 'lastOnlineAtUtc',
      width: 160,
      render: formatDateTime,
    },
    {
      title: t('emergencyLoginKeys.lastSync'),
      dataIndex: 'lastSyncAtUtc',
      width: 160,
      render: formatDateTime,
    },
  ]

  const columns: ColumnsType<EmergencyLoginKey> = [
    {
      title: t('emergencyLoginKeys.kid'),
      dataIndex: 'keyId',
      width: 210,
      fixed: 'left',
      render: (value: string) => <Typography.Text code copyable={{ text: value }}>{value}</Typography.Text>,
    },
    {
      title: t('emergencyLoginKeys.fingerprint'),
      dataIndex: 'publicKeyFingerprint',
      width: 190,
      render: (value: string) => (
        <Typography.Text
          code
          copyable={{ text: value }}
          aria-label={t('emergencyLoginKeys.fingerprint')}
        >
          {getShortEmergencyLoginKeyFingerprint(value)}
        </Typography.Text>
      ),
    },
    {
      title: t('emergencyLoginKeys.status'),
      dataIndex: 'status',
      width: 110,
      render: (status: string) => (
        <Tag color={STATUS_COLORS[status] ?? 'default'}>
          {t(`emergencyLoginKeys.statuses.${status}`, status)}
        </Tag>
      ),
    },
    {
      title: t('emergencyLoginKeys.createdAt'),
      dataIndex: 'createdAtUtc',
      width: 155,
      render: formatDateTime,
    },
    {
      title: t('emergencyLoginKeys.activatedAt'),
      dataIndex: 'activatedAtUtc',
      width: 155,
      render: formatDateTime,
    },
    {
      title: t('emergencyLoginKeys.retiredAt'),
      dataIndex: 'retiredAtUtc',
      width: 155,
      render: formatDateTime,
    },
    {
      title: t('emergencyLoginKeys.operator'),
      key: 'operator',
      width: 130,
      ellipsis: true,
      render: (_, key) => getLatestEmergencyLoginKeyOperator(key),
    },
    {
      title: t('emergencyLoginKeys.synced'),
      key: 'synced',
      width: 125,
      align: 'right',
      render: (_, key) => key.keyId === keyset?.coverageKeyId
        ? `${keyset.coverage.acknowledgedDevices} / ${keyset.coverage.totalDevices}`
        : '-',
    },
    {
      title: t('emergencyLoginKeys.missing'),
      key: 'missing',
      width: 130,
      align: 'right',
      render: (_, key) => {
        if (key.keyId !== keyset?.coverageKeyId) {
          return '-'
        }
        const missingCount = keyset.missingDevices.length
        return missingCount > 0 ? (
          <Button
            type="link"
            danger
            size="small"
            aria-label={t('emergencyLoginKeys.viewMissing', { count: missingCount })}
            onClick={() => setMissingDevicesOpen(true)}
          >
            {t('emergencyLoginKeys.viewMissing', { count: missingCount })}
          </Button>
        ) : '0'
      },
    },
    {
      title: t('emergencyLoginKeys.actions'),
      key: 'actions',
      width: 210,
      fixed: 'right',
      render: (_, key) => {
        const coverageComplete = key.keyId === keyset?.coverageKeyId
          && keyset.missingDevices.length === 0
        const actions = getEmergencyLoginKeyActionState(key.status, coverageComplete)

        return (
          <Space size={4} wrap>
            {actions.canActivate ? (
              <Button size="small" type="primary" onClick={() => openMutation({ type: 'activate', key })}>
                {t('emergencyLoginKeys.activate')}
              </Button>
            ) : null}
            {actions.canForceActivate ? (
              <Button size="small" danger onClick={() => openMutation({ type: 'forceActivate', key })}>
                {t('emergencyLoginKeys.forceActivate')}
              </Button>
            ) : null}
            {actions.canDiscard ? (
              <Button size="small" type="link" danger onClick={() => openMutation({ type: 'discard', key })}>
                {t('emergencyLoginKeys.discard')}
              </Button>
            ) : null}
            {actions.canRetire ? (
              <Button size="small" danger onClick={() => openMutation({ type: 'retire', key })}>
                {t('emergencyLoginKeys.retire')}
              </Button>
            ) : null}
            {key.status === 'Active' ? <Typography.Text type="success">{t('emergencyLoginKeys.activeKey')}</Typography.Text> : null}
            {key.status === 'Retired' ? <Typography.Text type="secondary">{t('emergencyLoginKeys.noActions')}</Typography.Text> : null}
          </Space>
        )
      },
    },
  ]

  const hasStagedKey = keyset?.keys.some((key) => key.status === 'Staged') ?? false
  const forceTarget = mutationOperation?.type === 'forceActivate' ? mutationOperation.key : null
  const coveragePercentage = keyset && Number.isFinite(keyset.coverage.percentage)
    ? `${keyset.coverage.percentage.toFixed(2)}%`
    : '-'

  return (
    <PageContainer
      title={t('emergencyLoginKeys.title')}
      subtitle={t('emergencyLoginKeys.subtitle')}
      extra={(
        <Space size={8}>
          <Button
            icon={<ReloadOutlined />}
            loading={loading}
            aria-label={t('emergencyLoginKeys.refresh')}
            onClick={() => void loadKeyset()}
          >
            {t('emergencyLoginKeys.refresh')}
          </Button>
          <Tooltip title={hasStagedKey ? t('emergencyLoginKeys.stagedExists') : undefined}>
            <span>
              <Button
                type="primary"
                icon={<KeyOutlined />}
                disabled={!keyset || hasStagedKey}
                aria-label={t('emergencyLoginKeys.generate')}
                onClick={() => openMutation({ type: 'generate' })}
              >
                {t('emergencyLoginKeys.generate')}
              </Button>
            </span>
          </Tooltip>
        </Space>
      )}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        {loadError ? (
          <Alert
            showIcon
            type="error"
            message={loadError}
            action={<Button size="small" onClick={() => void loadKeyset()}>{t('emergencyLoginKeys.refresh')}</Button>}
          />
        ) : null}

        {loading && !keyset ? (
          <Skeleton active title paragraph={{ rows: 6 }} />
        ) : keyset ? (
          <>
            <Descriptions bordered size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
              <Descriptions.Item label={t('emergencyLoginKeys.activeKid')}>
                {keyset.activeKeyId
                  ? <Typography.Text code copyable={{ text: keyset.activeKeyId }}>{keyset.activeKeyId}</Typography.Text>
                  : '-'}
              </Descriptions.Item>
              <Descriptions.Item label={t('emergencyLoginKeys.version')}>
                <Typography.Text code>{keyset.version}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('emergencyLoginKeys.dataProtection')}>
                <Space size={4} wrap>
                  <Tag color={keyset.dataProtectionHealthy ? 'green' : 'red'}>
                    {t(keyset.dataProtectionHealthy ? 'emergencyLoginKeys.healthy' : 'emergencyLoginKeys.unhealthy')}
                  </Tag>
                  <Typography.Text type="secondary">
                    {t(`emergencyLoginKeys.dataProtectionStatuses.${getEmergencyLoginKeyDataProtectionStatusKey(keyset.dataProtectionStatus)}`)}
                  </Typography.Text>
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('emergencyLoginKeys.coverage')}>
                <Space size={4} wrap>
                  <Typography.Text code>{coveragePercentage}</Typography.Text>
                  <Typography.Text type="secondary">
                    {t('emergencyLoginKeys.coverageDetail', {
                      acknowledged: keyset.coverage.acknowledgedDevices,
                      total: keyset.coverage.totalDevices,
                    })}
                  </Typography.Text>
                </Space>
              </Descriptions.Item>
            </Descriptions>

            <Typography.Title level={5} style={{ margin: '4px 0 0' }}>
              {t('emergencyLoginKeys.listTitle')}
            </Typography.Title>
            <Table<EmergencyLoginKey>
              rowKey="keyId"
              size="small"
              loading={loading}
              dataSource={keyset.keys}
              columns={columns}
              pagination={false}
              scroll={{ x: 1600 }}
              locale={{ emptyText: <Empty description={t('emergencyLoginKeys.empty')} /> }}
            />
          </>
        ) : null}
      </Space>

      <Drawer
        title={t('emergencyLoginKeys.missingDevicesTitle')}
        open={missingDevicesOpen}
        width={820}
        onClose={() => setMissingDevicesOpen(false)}
        destroyOnHidden
      >
        <Table<EmergencyLoginKeyMissingDevice>
          rowKey="deviceRegistrationId"
          size="small"
          dataSource={keyset?.missingDevices ?? []}
          columns={missingDeviceColumns}
          pagination={false}
          scroll={{ x: 780 }}
          locale={{ emptyText: <Empty description={t('emergencyLoginKeys.empty')} /> }}
        />
      </Drawer>

      <Modal
        title={operationTitle}
        open={Boolean(mutationOperation)}
        confirmLoading={submitting}
        okText={operationConfirmText}
        okButtonProps={{ danger: mutationOperation?.type === 'forceActivate' || mutationOperation?.type === 'retire' || mutationOperation?.type === 'discard' }}
        cancelText={t('common.cancel')}
        onOk={() => mutationForm.submit()}
        onCancel={closeMutation}
        destroyOnHidden
      >
        <Form<MutationFormValues>
          form={mutationForm}
          layout="vertical"
          onFinish={(values) => void submitMutation(values)}
        >
          {forceTarget ? (
            <Alert
              showIcon
              type="error"
              message={t('emergencyLoginKeys.forceWarning', { count: keyset?.missingDevices.length ?? 0 })}
              style={{ marginBottom: 16 }}
            />
          ) : null}
          <Form.Item
            name="reason"
            label={t('emergencyLoginKeys.reason')}
            rules={[
              {
                validator: (_, value) => String(value ?? '').trim()
                  ? Promise.resolve()
                  : Promise.reject(new Error(t('emergencyLoginKeys.reasonRequired'))),
              },
              { max: 200 },
            ]}
          >
            <Input.TextArea
              rows={3}
              maxLength={200}
              showCount
              placeholder={t('emergencyLoginKeys.reasonPlaceholder')}
              aria-label={t('emergencyLoginKeys.reason')}
            />
          </Form.Item>
          {forceTarget ? (
            <Form.Item
              name="confirmKeyId"
              label={t('emergencyLoginKeys.confirmKid')}
              rules={[
                { required: true, message: t('emergencyLoginKeys.confirmKidRequired') },
                {
                  validator: (_, value) => value === forceTarget.keyId
                    ? Promise.resolve()
                    : Promise.reject(new Error(t('emergencyLoginKeys.confirmKidMismatch'))),
                },
              ]}
            >
              <Input
                autoComplete="off"
                placeholder={forceTarget.keyId}
                aria-label={t('emergencyLoginKeys.confirmKid')}
              />
            </Form.Item>
          ) : null}
        </Form>
      </Modal>
    </PageContainer>
  )
}
