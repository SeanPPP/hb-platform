import { DownloadOutlined, ReloadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Descriptions, Space, Typography, message } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getRemoteMaintenanceArtifactUrl,
  getRemoteMaintenanceManifest,
} from '../../../services/remoteMaintenanceService'
import type { RemoteMaintenanceArtifact, RemoteMaintenanceManifest } from '../../../types/remoteMaintenance'
import { formatRemoteMaintenanceFileSize } from '../RemoteMaintenance/remoteMaintenanceLogic'

function artifactUrl(artifact: RemoteMaintenanceArtifact, kind: 'rustdesk' | 'status-agent') {
  return artifact.downloadUrl || getRemoteMaintenanceArtifactUrl(kind)
}

export default function RustDeskDownloadEntry() {
  const { t } = useTranslation()
  const [manifest, setManifest] = useState<RemoteMaintenanceManifest | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)

  const loadManifest = async () => {
    setLoading(true)
    setFailed(false)
    try {
      setManifest(await getRemoteMaintenanceManifest())
    } catch {
      setFailed(true)
      message.error(t('system.remoteMaintenance.manifestLoadFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadManifest()
  }, [])

  const renderArtifact = (label: string, artifact: RemoteMaintenanceArtifact, kind: 'rustdesk' | 'status-agent') => (
    <Descriptions.Item label={label}>
      <Space direction="vertical" size={2}>
        <Typography.Text>{artifact.fileName || '--'} · {artifact.version || '--'} · {formatRemoteMaintenanceFileSize(artifact.sizeBytes)}</Typography.Text>
        <Button
          size="small"
          type="link"
          icon={<DownloadOutlined />}
          href={artifactUrl(artifact, kind)}
          target="_blank"
          rel="noreferrer"
        >
          {t('system.remoteMaintenance.downloadArtifact')}
        </Button>
      </Space>
    </Descriptions.Item>
  )

  return (
    <Card
      title={t('system.remoteMaintenance.downloadTitle')}
      extra={<Button icon={<ReloadOutlined />} loading={loading} onClick={() => void loadManifest()}>{t('common.refresh')}</Button>}
    >
      {failed ? (
        <Alert type="warning" showIcon message={t('system.remoteMaintenance.manifestLoadFailed')} />
      ) : manifest ? (
        <Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label={t('system.remoteMaintenance.idServer')}>{manifest.idServer || '--'}</Descriptions.Item>
          <Descriptions.Item label={t('system.remoteMaintenance.relayServer')}>{manifest.relayServer || '--'}</Descriptions.Item>
          {renderArtifact(t('system.remoteMaintenance.rustdeskArtifact'), manifest.rustdesk, 'rustdesk')}
          {renderArtifact(t('system.remoteMaintenance.statusAgentArtifact'), manifest.statusAgent, 'status-agent')}
        </Descriptions>
      ) : (
        <Typography.Text type="secondary">{t('system.remoteMaintenance.manifestUnavailable')}</Typography.Text>
      )}
    </Card>
  )
}
