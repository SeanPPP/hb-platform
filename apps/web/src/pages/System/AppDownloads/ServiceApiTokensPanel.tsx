import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Alert,
  Button,
  Card,
  Empty,
  Input,
  Modal,
  Popconfirm,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { CopyOutlined, PlusOutlined, ReloadOutlined, StopOutlined } from '@ant-design/icons'
import {
  createServiceApiToken,
  getServiceApiTokens,
  revokeServiceApiToken,
} from '../../../services/serviceApiTokenService'
import type { ServiceApiToken, ServiceApiTokenCreateResponse } from '../../../types/serviceApiToken'
import { formatAppDownloadLocalDateTime } from './time'
import {
  buildServiceApiTokenEnvSnippet,
  canRevokeServiceApiToken,
  resolveServiceApiTokenApiBaseUrl,
  resolveServiceApiTokenStatusColor,
} from './serviceApiTokenPanelLogic'

function resolveBrowserApiBaseUrl() {
  const envBaseUrl = (((import.meta as ImportMeta & { env?: ImportMetaEnv }).env?.VITE_API_BASE_URL) || '').trim()
  const origin = typeof window === 'undefined' ? '' : window.location.origin
  return resolveServiceApiTokenApiBaseUrl(envBaseUrl, origin)
}

export default function ServiceApiTokensPanel() {
  const { t } = useTranslation()
  const [tokens, setTokens] = useState<ServiceApiToken[]>([])
  const [loading, setLoading] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [tokenName, setTokenName] = useState('')
  const [creating, setCreating] = useState(false)
  const [createdToken, setCreatedToken] = useState<ServiceApiTokenCreateResponse | null>(null)
  const [revokingId, setRevokingId] = useState<string | null>(null)

  async function copyText(value: string, successMessage: string, failedMessage: string) {
    try {
      await navigator.clipboard.writeText(value)
      message.success(successMessage)
    } catch {
      message.error(failedMessage)
    }
  }

  async function loadTokens() {
    setLoading(true)
    setLoadFailed(false)
    try {
      setTokens(await getServiceApiTokens())
    } catch {
      setLoadFailed(true)
      message.error(t('system.appDownloads.serviceTokens.loadFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadTokens()
  }, [])

  async function handleCreate() {
    const name = tokenName.trim()
    if (!name) {
      message.warning(t('system.appDownloads.serviceTokens.nameRequired'))
      return
    }

    setCreating(true)
    try {
      const result = await createServiceApiToken(name)
      setCreatedToken(result)
      setTokenName('')
      message.success(t('system.appDownloads.serviceTokens.createSuccess'))
      await loadTokens()
    } catch {
      message.error(t('system.appDownloads.serviceTokens.createFailed'))
    } finally {
      setCreating(false)
    }
  }

  async function handleRevoke(record: ServiceApiToken) {
    setRevokingId(record.id)
    try {
      await revokeServiceApiToken(record.id)
      message.success(t('system.appDownloads.serviceTokens.revokeSuccess'))
      await loadTokens()
    } catch {
      message.error(t('system.appDownloads.serviceTokens.revokeFailed'))
    } finally {
      setRevokingId(null)
    }
  }

  function closeCreateModal() {
    setCreateOpen(false)
    setCreatedToken(null)
    setTokenName('')
  }

  const apiBaseUrl = useMemo(() => resolveBrowserApiBaseUrl(), [])
  const envSnippet = createdToken
    ? buildServiceApiTokenEnvSnippet(apiBaseUrl, createdToken.token)
    : ''

  const columns: ColumnsType<ServiceApiToken> = [
    {
      title: t('system.appDownloads.serviceTokens.name'),
      dataIndex: 'name',
      width: 180,
      render: (value: string) => value || '--',
    },
    {
      title: t('system.appDownloads.serviceTokens.tokenPrefix'),
      dataIndex: 'tokenPrefix',
      width: 170,
      render: (value: string) => <Typography.Text code>{value || '--'}</Typography.Text>,
    },
    {
      title: t('system.appDownloads.serviceTokens.scopes'),
      dataIndex: 'scopes',
      width: 240,
      render: (scopes: string[]) => (
        <Space size={[4, 4]} wrap>
          {(scopes || []).map((scope) => (
            <Tag key={scope}>{scope}</Tag>
          ))}
        </Space>
      ),
    },
    {
      title: t('system.appDownloads.serviceTokens.status'),
      dataIndex: 'status',
      width: 120,
      render: (status: string) => (
        <Tag color={resolveServiceApiTokenStatusColor(status)}>
          {t(`system.appDownloads.serviceTokens.statuses.${status}`, status)}
        </Tag>
      ),
    },
    {
      title: t('system.appDownloads.serviceTokens.createdAt'),
      dataIndex: 'createdAt',
      width: 180,
      render: (value: string | null | undefined) => formatAppDownloadLocalDateTime(value),
    },
    {
      title: t('system.appDownloads.serviceTokens.lastUsedAt'),
      dataIndex: 'lastUsedAt',
      width: 180,
      render: (value: string | null | undefined) => formatAppDownloadLocalDateTime(value),
    },
    {
      title: t('system.appDownloads.serviceTokens.lastUsedIp'),
      dataIndex: 'lastUsedIp',
      width: 150,
      render: (value: string | null | undefined) => value || '--',
    },
    {
      title: t('system.appDownloads.serviceTokens.revokedAt'),
      dataIndex: 'revokedAt',
      width: 180,
      render: (value: string | null | undefined) => formatAppDownloadLocalDateTime(value),
    },
    {
      title: t('common.actions'),
      key: 'actions',
      fixed: 'right',
      width: 100,
      render: (_, record) =>
        canRevokeServiceApiToken(record.status) ? (
          <Popconfirm
            title={t('system.appDownloads.serviceTokens.revokeConfirm')}
            onConfirm={() => void handleRevoke(record)}
          >
            <Tooltip title={t('system.appDownloads.serviceTokens.revoke')}>
              <Button
                danger
                size="small"
                icon={<StopOutlined />}
                loading={revokingId === record.id}
              />
            </Tooltip>
          </Popconfirm>
        ) : (
          '--'
        ),
    },
  ]

  return (
    <>
      <Card
        title={t('system.appDownloads.serviceTokens.title')}
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} loading={loading} onClick={() => void loadTokens()}>
              {t('common.refresh')}
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
              {t('system.appDownloads.serviceTokens.create')}
            </Button>
          </Space>
        }
      >
        {loadFailed ? (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message={t('system.appDownloads.serviceTokens.loadFailed')}
          />
        ) : null}
        <Table<ServiceApiToken>
          rowKey="id"
          loading={loading}
          columns={columns}
          dataSource={tokens}
          scroll={{ x: 1500 }}
          locale={{ emptyText: <Empty description={t('system.appDownloads.serviceTokens.empty')} /> }}
          pagination={{ pageSize: 5, showSizeChanger: false }}
        />
      </Card>

      <Modal
        open={createOpen}
        title={t('system.appDownloads.serviceTokens.createTitle')}
        onCancel={closeCreateModal}
        destroyOnHidden
        footer={
          createdToken
            ? [
                <Button
                  key="copy-env"
                  icon={<CopyOutlined />}
                  onClick={() =>
                    void copyText(
                      envSnippet,
                      t('system.appDownloads.serviceTokens.copyEnvSuccess'),
                      t('system.appDownloads.serviceTokens.copyFailed'),
                    )
                  }
                >
                  {t('system.appDownloads.serviceTokens.copyEnv')}
                </Button>,
                <Button key="close" type="primary" onClick={closeCreateModal}>
                  {t('common.close')}
                </Button>,
              ]
            : [
                <Button key="cancel" onClick={closeCreateModal}>
                  {t('common.cancel')}
                </Button>,
                <Button key="create" type="primary" loading={creating} onClick={() => void handleCreate()}>
                  {t('common.create')}
                </Button>,
              ]
        }
      >
        {createdToken ? (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Alert
              type="success"
              showIcon
              message={t('system.appDownloads.serviceTokens.oneTimeWarning')}
            />
            <Typography.Text strong>
              {t('system.appDownloads.serviceTokens.oneTimeToken')}
            </Typography.Text>
            <Typography.Paragraph copyable={{ text: createdToken.token }} code>
              {createdToken.token}
            </Typography.Paragraph>
            <Typography.Text strong>
              {t('system.appDownloads.serviceTokens.envSnippet')}
            </Typography.Text>
            <Typography.Paragraph
              copyable={{ text: envSnippet }}
              style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}
            >
              {envSnippet}
            </Typography.Paragraph>
          </Space>
        ) : (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Typography.Text>{t('system.appDownloads.serviceTokens.name')}</Typography.Text>
            <Input
              autoFocus
              maxLength={120}
              value={tokenName}
              placeholder={t('system.appDownloads.serviceTokens.namePlaceholder')}
              onChange={(event) => setTokenName(event.target.value)}
              onPressEnter={() => void handleCreate()}
            />
          </Space>
        )}
      </Modal>
    </>
  )
}
