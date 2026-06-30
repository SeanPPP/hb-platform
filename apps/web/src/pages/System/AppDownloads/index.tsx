import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Input,
  Modal,
  QRCode,
  Segmented,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  CopyOutlined,
  LinkOutlined,
  QrcodeOutlined,
  ReloadOutlined,
  RollbackOutlined,
} from '@ant-design/icons'
import {
  createMobileAppOtaRollbackCommand,
  getLatestMobileAppBuild,
  getMobileAppBuilds,
  getMobileAppOtaUpdates,
} from '../../../services/mobileAppBuildService'
import { useAuthStore } from '../../../store/auth'
import type {
  MobileAppBuild,
  MobileAppOtaRollbackCommand,
  MobileAppOtaUpdate,
} from '../../../types/mobileAppBuild'
import {
  APP_DOWNLOAD_PROFILES,
  DEFAULT_APP_DOWNLOAD_PROFILE,
  buildAppDownloadQuery,
  buildAppDownloadOtaQuery,
  normalizeAppDownloadProfile,
  normalizeRuntimeVersionFilter,
  resolveAppDownloadMirrorStatus,
  resolveAppDownloadSource,
  resolveAppDownloadContentState,
  type AppDownloadMirrorStatus,
  type AppDownloadProfile,
} from './logic'
import { formatAppDownloadLocalDateTime } from './time'
import ServiceApiTokensPanel from './ServiceApiTokensPanel'

function formatVersion(build?: MobileAppBuild | null) {
  if (!build) {
    return '--'
  }
  const version = build.appVersion || '--'
  const buildVersion = build.appBuildVersion ? ` (${build.appBuildVersion})` : ''
  return `${version}${buildVersion}`
}

function formatShortCommit(value?: string | null) {
  return value ? value.slice(0, 8) : '--'
}

function getStatusColor(status?: string | null) {
  switch ((status ?? '').toLowerCase()) {
    case 'finished':
    case 'success':
    case 'completed':
      return 'green'
    case 'errored':
    case 'failed':
    case 'canceled':
    case 'cancelled':
      return 'red'
    case 'in-progress':
    case 'running':
    case 'pending':
      return 'processing'
    default:
      return 'default'
  }
}

function getOtaTypeColor(record: MobileAppOtaUpdate) {
  return record.isRollback ? 'orange' : 'blue'
}

function getMirrorStatusColor(status: AppDownloadMirrorStatus) {
  switch (status) {
    case 'succeeded':
      return 'green'
    case 'running':
      return 'processing'
    case 'failed':
      return 'orange'
    case 'unsafe':
      return 'red'
    case 'pending':
      return 'default'
    default:
      return 'default'
  }
}

export default function AppDownloadsPage() {
  const { t } = useTranslation()
  const canManageAppDownloads = useAuthStore((state) => state.access.canManageAppDownloads)
  const [latest, setLatest] = useState<MobileAppBuild | null>(null)
  const [items, setItems] = useState<MobileAppBuild[]>([])
  const [buildLoading, setBuildLoading] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [profile, setProfile] = useState<AppDownloadProfile>(DEFAULT_APP_DOWNLOAD_PROFILE)
  const [loadFailed, setLoadFailed] = useState(false)
  const [qrBuild, setQrBuild] = useState<MobileAppBuild | null>(null)
  const [otaItems, setOtaItems] = useState<MobileAppOtaUpdate[]>([])
  const [otaLoading, setOtaLoading] = useState(false)
  const [otaPage, setOtaPage] = useState(1)
  const [otaPageSize, setOtaPageSize] = useState(10)
  const [otaTotal, setOtaTotal] = useState(0)
  const [otaLoadFailed, setOtaLoadFailed] = useState(false)
  const [otaRuntimeDraft, setOtaRuntimeDraft] = useState('')
  const [otaRuntimeVersion, setOtaRuntimeVersion] = useState('')
  const [rollbackLoadingGroupId, setRollbackLoadingGroupId] = useState<string | null>(null)
  const [rollbackCommand, setRollbackCommand] = useState<MobileAppOtaRollbackCommand | null>(null)
  const buildLoadRequestIdRef = useRef(0)
  const otaLoadRequestIdRef = useRef(0)

  async function copyText(
    value: string | null | undefined,
    successMessage: string,
    failedMessage: string,
  ) {
    if (!value) {
      return
    }

    try {
      await navigator.clipboard.writeText(value)
      message.success(successMessage)
    } catch (error) {
      console.error(failedMessage, error)
      message.error(failedMessage)
    }
  }

  async function copyLink(url?: string | null) {
    await copyText(
      url,
      t('system.appDownloads.copySuccess'),
      t('system.appDownloads.copyFailed'),
    )
  }

  async function copyGroupId(updateGroupId?: string | null) {
    await copyText(
      updateGroupId,
      t('system.appDownloads.ota.groupIdCopySuccess'),
      t('system.appDownloads.ota.copyTextFailed'),
    )
  }

  function openLink(url?: string | null) {
    if (!url) {
      return
    }
    window.open(url, '_blank', 'noopener,noreferrer')
  }

  async function loadBuildData(
    nextPage = page,
    nextPageSize = pageSize,
    nextProfile: AppDownloadProfile = profile,
  ) {
    const query = buildAppDownloadQuery(nextProfile, nextPage, nextPageSize)
    const requestId = buildLoadRequestIdRef.current + 1
    buildLoadRequestIdRef.current = requestId
    setBuildLoading(true)
    setLoadFailed(false)
    try {
      const [latestBuild, history] = await Promise.all([
        getLatestMobileAppBuild(query.profile),
        getMobileAppBuilds(query),
      ])
      if (requestId !== buildLoadRequestIdRef.current) {
        return
      }
      setLatest(latestBuild)
      setItems(history.items)
      setTotal(history.total)
      setPage(history.page)
      setPageSize(history.pageSize)
      setProfile(query.profile)
    } catch (error) {
      if (requestId !== buildLoadRequestIdRef.current) {
        return
      }
      console.error(t('system.appDownloads.loadFailed'), error)
      setLatest(null)
      setItems([])
      setTotal(0)
      setPage(1)
      setLoadFailed(true)
      message.error(t('system.appDownloads.loadFailed'))
    } finally {
      // 只允许最后一次请求收尾，避免旧请求晚返回时关闭新请求的 loading 或覆盖状态。
      if (requestId === buildLoadRequestIdRef.current) {
        setBuildLoading(false)
      }
    }
  }

  async function loadOtaData(
    nextPage = otaPage,
    nextPageSize = otaPageSize,
    nextProfile: AppDownloadProfile = profile,
    nextRuntimeVersion = otaRuntimeVersion,
  ) {
    const query = buildAppDownloadOtaQuery(
      nextProfile,
      nextPage,
      nextPageSize,
      nextRuntimeVersion,
    )
    const requestId = otaLoadRequestIdRef.current + 1
    otaLoadRequestIdRef.current = requestId
    setOtaLoading(true)
    setOtaLoadFailed(false)
    try {
      const history = await getMobileAppOtaUpdates(query)
      if (requestId !== otaLoadRequestIdRef.current) {
        return
      }
      setOtaItems(history.items)
      setOtaTotal(history.total)
      setOtaPage(history.page)
      setOtaPageSize(history.pageSize)
    } catch (error) {
      if (requestId !== otaLoadRequestIdRef.current) {
        return
      }
      console.error(t('system.appDownloads.ota.loadFailed'), error)
      setOtaItems([])
      setOtaTotal(0)
      setOtaPage(1)
      setOtaLoadFailed(true)
      message.error(t('system.appDownloads.ota.loadFailed'))
    } finally {
      // OTA 列表独立收尾，失败时不能影响 APK 最新卡片和构建历史。
      if (requestId === otaLoadRequestIdRef.current) {
        setOtaLoading(false)
      }
    }
  }

  async function generateRollbackCommand(record: MobileAppOtaUpdate) {
    if (!record.updateGroupId) {
      return
    }

    setRollbackLoadingGroupId(record.updateGroupId)
    try {
      const command = await createMobileAppOtaRollbackCommand(record.updateGroupId)
      setRollbackCommand(command)
      message.success(t('system.appDownloads.ota.rollbackCommandGenerated'))
    } catch (error) {
      console.error(t('system.appDownloads.ota.rollbackCommandFailed'), error)
      message.error(t('system.appDownloads.ota.rollbackCommandFailed'))
    } finally {
      setRollbackLoadingGroupId(null)
    }
  }

  function handleProfileChange(value: string | number) {
    const nextProfile = normalizeAppDownloadProfile(value)
    setProfile(nextProfile)
    void loadBuildData(1, pageSize, nextProfile)
    void loadOtaData(1, otaPageSize, nextProfile, otaRuntimeVersion)
  }

  useEffect(() => {
    void loadBuildData(1, pageSize, DEFAULT_APP_DOWNLOAD_PROFILE)
    void loadOtaData(1, otaPageSize, DEFAULT_APP_DOWNLOAD_PROFILE, '')
  }, [])

  function handleOtaRuntimeSearch(value: string) {
    const nextRuntimeVersion = normalizeRuntimeVersionFilter(value)
    setOtaRuntimeDraft(nextRuntimeVersion)
    setOtaRuntimeVersion(nextRuntimeVersion)
    void loadOtaData(1, otaPageSize, profile, nextRuntimeVersion)
  }

  function handleOtaRuntimeDraftChange(value: string) {
    setOtaRuntimeDraft(value)
    if (!value && otaRuntimeVersion) {
      setOtaRuntimeVersion('')
      void loadOtaData(1, otaPageSize, profile, '')
    }
  }

  const profileOptions = useMemo(
    () =>
      APP_DOWNLOAD_PROFILES.map((value) => ({
        label: t(`system.appDownloads.profiles.${value}`),
        value,
      })),
    [t],
  )

  const columns = useMemo<ColumnsType<MobileAppBuild>>(
    () => [
      {
        title: t('system.appDownloads.profile'),
        dataIndex: 'buildProfile',
        width: 130,
        render: (value: string | null | undefined) => value || '--',
      },
      {
        title: t('system.appDownloads.versionBuild'),
        key: 'version',
        width: 160,
        render: (_value, record) => formatVersion(record),
      },
      {
        title: t('system.appDownloads.status'),
        dataIndex: 'status',
        width: 130,
        render: (value: string | null | undefined) => (
          <Tag color={getStatusColor(value)}>{value || '--'}</Tag>
        ),
      },
      {
        title: t('system.appDownloads.completedAt'),
        dataIndex: 'completedAt',
        width: 190,
        render: (value: string | null | undefined) => formatAppDownloadLocalDateTime(value),
      },
      {
        title: t('system.appDownloads.expirationDate'),
        dataIndex: 'expirationDate',
        width: 190,
        render: (value: string | null | undefined) => formatAppDownloadLocalDateTime(value),
      },
      {
        title: t('system.appDownloads.downloadSource'),
        key: 'downloadSource',
        width: 130,
        render: (_value, record) => (
          <Tag>{t(`system.appDownloads.downloadSources.${resolveAppDownloadSource(record)}`)}</Tag>
        ),
      },
      {
        title: t('system.appDownloads.mirrorStatus'),
        key: 'mirrorStatus',
        width: 140,
        render: (_value, record) => {
          const mirrorStatus = resolveAppDownloadMirrorStatus(record)
          return (
            <Tag color={getMirrorStatusColor(mirrorStatus)}>
              {t(`system.appDownloads.mirrorStatuses.${mirrorStatus}`)}
            </Tag>
          )
        },
      },
      {
        title: t('system.appDownloads.mirrorError'),
        dataIndex: 'cosMirrorError',
        width: 220,
        render: (value: string | null | undefined) => (
          <Typography.Text
            type={value ? 'danger' : undefined}
            ellipsis={{ tooltip: value || undefined }}
            style={{ maxWidth: 200 }}
          >
            {value || '--'}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.commit'),
        dataIndex: 'gitCommitHash',
        width: 160,
        render: (value: string | null | undefined, record) => (
          <Typography.Text title={record.gitCommitMessage || undefined}>
            {formatShortCommit(value)}
          </Typography.Text>
        ),
      },
      {
        title: t('column.action'),
        key: 'actions',
        width: 280,
        fixed: 'right',
        render: (_value, record) => (
          <Space wrap>
            <Button
              size="small"
              icon={<LinkOutlined />}
              disabled={!record.artifactUrl}
              onClick={() => openLink(record.artifactUrl)}
            >
              {t('system.appDownloads.openDownload')}
            </Button>
            <Button
              size="small"
              icon={<CopyOutlined />}
              disabled={!record.artifactUrl}
              onClick={() => void copyLink(record.artifactUrl)}
            >
              {t('system.appDownloads.copyLink')}
            </Button>
            <Button
              size="small"
              icon={<QrcodeOutlined />}
              disabled={!record.artifactUrl}
              onClick={() => setQrBuild(record)}
            >
              {t('system.appDownloads.viewQrCode')}
            </Button>
          </Space>
        ),
      },
    ],
    [t],
  )

  const otaColumns = useMemo<ColumnsType<MobileAppOtaUpdate>>(
    () => [
      {
        title: t('system.appDownloads.channel'),
        dataIndex: 'channel',
        width: 120,
        render: (value: string | null | undefined) => value || '--',
      },
      {
        title: t('system.appDownloads.runtime'),
        dataIndex: 'runtimeVersion',
        width: 130,
        render: (value: string | null | undefined) => value || '--',
      },
      {
        title: t('system.appDownloads.ota.platform'),
        dataIndex: 'platform',
        width: 110,
        render: (value: string | null | undefined) => value || '--',
      },
      {
        title: t('system.appDownloads.ota.message'),
        dataIndex: 'message',
        width: 260,
        render: (value: string | null | undefined) => (
          <Typography.Text
            ellipsis={{ tooltip: value || undefined }}
            style={{ maxWidth: 240 }}
          >
            {value || '--'}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.ota.updateGroupId'),
        dataIndex: 'updateGroupId',
        width: 240,
        render: (value: string | null | undefined) => (
          <Typography.Text
            ellipsis={{ tooltip: value || undefined }}
            style={{ maxWidth: 220 }}
          >
            {value || '--'}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.ota.androidUpdateId'),
        dataIndex: 'androidUpdateId',
        width: 220,
        render: (value: string | null | undefined) => (
          <Typography.Text
            ellipsis={{ tooltip: value || undefined }}
            style={{ maxWidth: 200 }}
          >
            {value || '--'}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.commit'),
        dataIndex: 'gitCommitHash',
        width: 130,
        render: (value: string | null | undefined) => (
          <Typography.Text title={value || undefined}>
            {formatShortCommit(value)}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.ota.publishedAt'),
        dataIndex: 'publishedAt',
        width: 190,
        render: (value: string | null | undefined) => formatAppDownloadLocalDateTime(value),
      },
      {
        title: t('system.appDownloads.ota.type'),
        key: 'type',
        width: 120,
        render: (_value, record) => (
          <Tag color={getOtaTypeColor(record)}>
            {record.isRollback
              ? t('system.appDownloads.ota.types.rollback')
              : t('system.appDownloads.ota.types.normal')}
          </Tag>
        ),
      },
      {
        title: t('column.action'),
        key: 'actions',
        width: 380,
        fixed: 'right',
        render: (_value, record, index) => {
          const canCreateRollback =
            otaPage === 1 && index === 0 && !record.isRollback && Boolean(record.updateGroupId)
          const disabledTitle = canCreateRollback
            ? undefined
            : t('system.appDownloads.ota.rollbackLatestOnly')

          return (
            <Space wrap>
              <Button
                size="small"
                icon={<CopyOutlined />}
                disabled={!record.updateGroupId}
                onClick={() => void copyGroupId(record.updateGroupId)}
              >
                {t('system.appDownloads.ota.copyGroupId')}
              </Button>
              <Button
                size="small"
                icon={<LinkOutlined />}
                disabled={!record.dashboardUrl}
                onClick={() => openLink(record.dashboardUrl)}
              >
                {t('system.appDownloads.ota.openDashboard')}
              </Button>
              {canManageAppDownloads ? (
                <Tooltip title={disabledTitle}>
                  <span>
                    <Button
                      size="small"
                      icon={<RollbackOutlined />}
                      disabled={!canCreateRollback}
                      loading={rollbackLoadingGroupId === record.updateGroupId}
                      title={disabledTitle}
                      onClick={() => void generateRollbackCommand(record)}
                    >
                      {t('system.appDownloads.ota.createRollbackCommand')}
                    </Button>
                  </span>
                </Tooltip>
              ) : null}
            </Space>
          )
        },
      },
    ],
    [canManageAppDownloads, otaPage, rollbackLoadingGroupId, t],
  )

  const latestActions = (
    <Space wrap>
      <Segmented
        value={profile}
        options={profileOptions}
        onChange={handleProfileChange}
      />
      <Button
        icon={<CopyOutlined />}
        disabled={!latest?.artifactUrl}
        onClick={() => void copyLink(latest?.artifactUrl)}
      >
        {t('system.appDownloads.copyLink')}
      </Button>
      <Button
        type="primary"
        icon={<LinkOutlined />}
        disabled={!latest?.artifactUrl}
        onClick={() => openLink(latest?.artifactUrl)}
      >
        {t('system.appDownloads.openDownload')}
      </Button>
      <Button
        disabled={!latest?.buildDetailsPageUrl}
        onClick={() => openLink(latest?.buildDetailsPageUrl)}
      >
        {t('system.appDownloads.buildDetails')}
      </Button>
      <Button icon={<ReloadOutlined />} loading={buildLoading} onClick={() => void loadBuildData(1, pageSize)}>
        {t('common.refresh')}
      </Button>
    </Space>
  )

  const contentState = resolveAppDownloadContentState(
    loadFailed,
    Boolean(latest?.artifactUrl),
    items.length,
  )

  const otaActions = (
    <Space wrap>
      <Input.Search
        allowClear
        value={otaRuntimeDraft}
        placeholder={t('system.appDownloads.ota.runtimePlaceholder')}
        style={{ width: 220 }}
        onChange={(event) => handleOtaRuntimeDraftChange(event.target.value)}
        onSearch={handleOtaRuntimeSearch}
      />
      <Button
        icon={<ReloadOutlined />}
        loading={otaLoading}
        onClick={() => void loadOtaData(1, otaPageSize, profile, otaRuntimeVersion)}
      >
        {t('common.refresh')}
      </Button>
    </Space>
  )

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card title={t('system.appDownloads.latestTitle')} extra={latestActions} loading={buildLoading}>
        {contentState === 'error' ? (
          <Alert
            type="error"
            showIcon
            message={t('system.appDownloads.loadFailed')}
            description={t('system.appDownloads.loadFailedDescription')}
          />
        ) : latest?.artifactUrl ? (
          <Space align="start" size={24} wrap>
            <QRCode value={latest.artifactUrl} size={180} />
            <Descriptions column={2} bordered size="small" style={{ minWidth: 520 }}>
              <Descriptions.Item label={t('system.appDownloads.version')}>
                {formatVersion(latest)}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.profile')}>
                {latest.buildProfile || profile}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.channel')}>
                {latest.channel || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.runtime')}>
                {latest.runtimeVersion || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.completedAt')}>
                {formatAppDownloadLocalDateTime(latest.completedAt)}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.expirationDate')}>
                {formatAppDownloadLocalDateTime(latest.expirationDate)}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.downloadSource')}>
                <Tag>{t(`system.appDownloads.downloadSources.${resolveAppDownloadSource(latest)}`)}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.mirrorStatus')}>
                {(() => {
                  const mirrorStatus = resolveAppDownloadMirrorStatus(latest)
                  return (
                    <Tag color={getMirrorStatusColor(mirrorStatus)}>
                      {t(`system.appDownloads.mirrorStatuses.${mirrorStatus}`)}
                    </Tag>
                  )
                })()}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.mirrorError')} span={2}>
                <Typography.Text
                  type={latest.cosMirrorError ? 'danger' : undefined}
                  ellipsis={{ tooltip: latest.cosMirrorError || undefined }}
                  style={{ maxWidth: 480 }}
                >
                  {latest.cosMirrorError || '--'}
                </Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.commit')}>
                {formatShortCommit(latest.gitCommitHash)}
              </Descriptions.Item>
            </Descriptions>
          </Space>
        ) : (
          <Empty description={t('system.appDownloads.empty')} />
        )}
      </Card>

      <Card title={t('system.appDownloads.historyTitle')}>
        <Table<MobileAppBuild>
          rowKey="id"
          loading={buildLoading}
          columns={columns}
          dataSource={items}
          scroll={{ x: 1730 }}
          locale={{
            emptyText: (
              <Empty
                description={
                  loadFailed
                    ? t('system.appDownloads.loadFailed')
                    : t('system.appDownloads.empty')
                }
              />
            ),
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => void loadBuildData(nextPage, nextPageSize, profile),
          }}
        />
      </Card>

      <Card title={t('system.appDownloads.ota.title')} extra={otaActions}>
        {otaLoadFailed ? (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message={t('system.appDownloads.ota.loadFailed')}
            description={t('system.appDownloads.ota.loadFailedDescription')}
          />
        ) : null}
        <Table<MobileAppOtaUpdate>
          rowKey="id"
          loading={otaLoading}
          columns={otaColumns}
          dataSource={otaItems}
          scroll={{ x: 1800 }}
          locale={{
            emptyText: (
              <Empty
                description={
                  otaLoadFailed
                    ? t('system.appDownloads.ota.loadFailed')
                    : t('system.appDownloads.ota.empty')
                }
              />
            ),
          }}
          pagination={{
            current: otaPage,
            pageSize: otaPageSize,
            total: otaTotal,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) =>
              void loadOtaData(nextPage, nextPageSize, profile, otaRuntimeVersion),
          }}
        />
      </Card>

      {canManageAppDownloads ? <ServiceApiTokensPanel /> : null}

      <Modal
        open={!!qrBuild}
        title={t('system.appDownloads.qrCodeTitle')}
        footer={null}
        onCancel={() => setQrBuild(null)}
        destroyOnHidden
      >
        {qrBuild?.artifactUrl ? (
          <Space direction="vertical" size={16} style={{ width: '100%', alignItems: 'center' }}>
            <QRCode value={qrBuild.artifactUrl} size={220} />
            <Typography.Text copyable>{qrBuild.artifactUrl}</Typography.Text>
          </Space>
        ) : null}
      </Modal>

      <Modal
        open={!!rollbackCommand}
        title={t('system.appDownloads.ota.rollbackCommandTitle')}
        onCancel={() => setRollbackCommand(null)}
        footer={[
          <Button key="copy" onClick={() => void copyText(
            rollbackCommand?.command,
            t('system.appDownloads.ota.rollbackCommandCopySuccess'),
            t('system.appDownloads.ota.copyTextFailed'),
          )}>
            {t('common.copy')}
          </Button>,
          <Button key="close" type="primary" onClick={() => setRollbackCommand(null)}>
            {t('common.close')}
          </Button>,
        ]}
        destroyOnHidden
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {rollbackCommand?.warning ? (
            <Alert
              type="warning"
              showIcon
              message={t('system.appDownloads.ota.rollbackCommandWarning')}
              description={rollbackCommand.warning}
            />
          ) : null}
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label={t('system.appDownloads.ota.updateGroupId')}>
              {rollbackCommand?.updateGroupId || '--'}
            </Descriptions.Item>
            <Descriptions.Item label={t('system.appDownloads.ota.rollbackCommand')}>
              <Typography.Paragraph
                copyable={rollbackCommand?.command ? { text: rollbackCommand.command } : false}
                style={{ marginBottom: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}
              >
                {rollbackCommand?.command || '--'}
              </Typography.Paragraph>
            </Descriptions.Item>
          </Descriptions>
        </Space>
      </Modal>
    </Space>
  )
}
