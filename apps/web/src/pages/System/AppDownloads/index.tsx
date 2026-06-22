import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Modal,
  QRCode,
  Segmented,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  CopyOutlined,
  LinkOutlined,
  QrcodeOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  getLatestMobileAppBuild,
  getMobileAppBuilds,
} from '../../../services/mobileAppBuildService'
import type { MobileAppBuild } from '../../../types/mobileAppBuild'
import {
  APP_DOWNLOAD_PROFILES,
  DEFAULT_APP_DOWNLOAD_PROFILE,
  buildAppDownloadQuery,
  normalizeAppDownloadProfile,
  resolveAppDownloadContentState,
  type AppDownloadProfile,
} from './logic'

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

export default function AppDownloadsPage() {
  const { t } = useTranslation()
  const [latest, setLatest] = useState<MobileAppBuild | null>(null)
  const [items, setItems] = useState<MobileAppBuild[]>([])
  const [loading, setLoading] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [profile, setProfile] = useState<AppDownloadProfile>(DEFAULT_APP_DOWNLOAD_PROFILE)
  const [loadFailed, setLoadFailed] = useState(false)
  const [qrBuild, setQrBuild] = useState<MobileAppBuild | null>(null)
  const loadRequestIdRef = useRef(0)

  async function copyLink(url?: string | null) {
    if (!url) {
      return
    }

    try {
      await navigator.clipboard.writeText(url)
      message.success(t('system.appDownloads.copySuccess'))
    } catch (error) {
      console.error(t('system.appDownloads.copyFailed'), error)
      message.error(t('system.appDownloads.copyFailed'))
    }
  }

  function openLink(url?: string | null) {
    if (!url) {
      return
    }
    window.open(url, '_blank', 'noopener,noreferrer')
  }

  async function loadData(
    nextPage = page,
    nextPageSize = pageSize,
    nextProfile: AppDownloadProfile = profile,
  ) {
    const query = buildAppDownloadQuery(nextProfile, nextPage, nextPageSize)
    const requestId = loadRequestIdRef.current + 1
    loadRequestIdRef.current = requestId
    setLoading(true)
    setLoadFailed(false)
    try {
      const [latestBuild, history] = await Promise.all([
        getLatestMobileAppBuild(query.profile),
        getMobileAppBuilds(query),
      ])
      if (requestId !== loadRequestIdRef.current) {
        return
      }
      setLatest(latestBuild)
      setItems(history.items)
      setTotal(history.total)
      setPage(history.page)
      setPageSize(history.pageSize)
      setProfile(query.profile)
    } catch (error) {
      if (requestId !== loadRequestIdRef.current) {
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
      if (requestId === loadRequestIdRef.current) {
        setLoading(false)
      }
    }
  }

  function handleProfileChange(value: string | number) {
    const nextProfile = normalizeAppDownloadProfile(value)
    setProfile(nextProfile)
    void loadData(1, pageSize, nextProfile)
  }

  useEffect(() => {
    void loadData(1, pageSize, DEFAULT_APP_DOWNLOAD_PROFILE)
  }, [])

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
        render: (value: string | null | undefined) => formatDateTime(value),
      },
      {
        title: t('system.appDownloads.expirationDate'),
        dataIndex: 'expirationDate',
        width: 190,
        render: (value: string | null | undefined) => formatDateTime(value),
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
      <Button icon={<ReloadOutlined />} loading={loading} onClick={() => void loadData(1, pageSize)}>
        {t('common.refresh')}
      </Button>
    </Space>
  )

  const contentState = resolveAppDownloadContentState(
    loadFailed,
    Boolean(latest?.artifactUrl),
    items.length,
  )

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card title={t('system.appDownloads.latestTitle')} extra={latestActions} loading={loading}>
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
              <Descriptions.Item label={t('system.appDownloads.completedAt')}>
                {formatDateTime(latest.completedAt)}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.expirationDate')}>
                {formatDateTime(latest.expirationDate)}
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
          loading={loading}
          columns={columns}
          dataSource={items}
          scroll={{ x: 1240 }}
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
            onChange: (nextPage, nextPageSize) => void loadData(nextPage, nextPageSize, profile),
          }}
        />
      </Card>

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
    </Space>
  )
}
