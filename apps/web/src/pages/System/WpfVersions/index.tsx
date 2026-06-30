import {
  CloudUploadOutlined,
  DownloadOutlined,
  ReloadOutlined,
  RollbackOutlined,
  SaveOutlined,
  UploadOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
  Upload,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { UploadFile } from 'antd/es/upload/interface'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  createWpfAppRelease,
  getWpfAppReleases,
  initWpfReleaseUpload,
  saveWpfReleasePolicy,
  updateWpfAppRelease,
  uploadWpfReleaseFile,
} from '../../../services/wpfVersionService'
import { useAuthStore } from '../../../store/auth'
import type { WpfAppRelease, WpfReleasePolicyRequest } from '../../../types/wpfVersion'
import {
  WPF_RELEASE_CHANNELS,
  buildWpfPolicyPayload,
  calculateFileSha256,
  canSubmitWpfPolicy,
  getEffectiveWpfMinimumSupportedVersion,
  getDefaultWpfInstallerArguments,
  getWpfCurrentVersionText,
  getWpfPolicySummary,
  getWpfPolicyRangeError,
  getWpfVersionErrorMessage,
  inferWpfInstallerType,
  isSupportedWpfInstallerFile,
  isWpfRollbackTarget,
  normalizeWpfReleaseChannel,
} from './logic'

const { Text, Paragraph } = Typography
const { Dragger } = Upload

interface UploadFormValues {
  version: string
  channel: string
  sha256: string
  installerArguments?: string
  releaseNotes?: string
  fileSize?: number
}

interface PolicyFormValues {
  channel: string
  targetVersion: string
  minimumSupportedVersion: string
  forceUpdate: boolean
}

function formatFileSize(size?: number | null) {
  if (!size || size <= 0) {
    return '-'
  }
  if (size >= 1024 * 1024 * 1024) {
    return `${(size / 1024 / 1024 / 1024).toFixed(2)} GB`
  }
  if (size >= 1024 * 1024) {
    return `${(size / 1024 / 1024).toFixed(2)} MB`
  }
  return `${(size / 1024).toFixed(1)} KB`
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return '-'
  }

  const parsed = dayjs(value)
  return parsed.isValid() ? parsed.format('YYYY-MM-DD HH:mm') : value
}

function shortSha(value?: string | null) {
  if (!value) {
    return '-'
  }

  return value.length > 16 ? `${value.slice(0, 12)}...${value.slice(-6)}` : value
}

function getReleaseRowKey(record: WpfAppRelease) {
  return record.id || `${record.channel}-${record.version}-${record.fileName}`
}

function isPolicyReferencedRelease(record: WpfAppRelease) {
  return record.version === record.targetVersion || record.version === record.minimumSupportedVersion
}

export default function WpfVersionsPage() {
  const { t } = useTranslation()
  const canManageAppDownloads = useAuthStore((state) => state.access.canManageAppDownloads)
  const [uploadForm] = Form.useForm<UploadFormValues>()
  const [policyForm] = Form.useForm<PolicyFormValues>()
  const [channel, setChannel] = useState('production')
  const [includeDisabled, setIncludeDisabled] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [releases, setReleases] = useState<WpfAppRelease[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [policySaving, setPolicySaving] = useState(false)
  const [statusUpdatingId, setStatusUpdatingId] = useState<string | null>(null)
  const [uploadOpen, setUploadOpen] = useState(false)
  const [uploadFile, setUploadFile] = useState<File | null>(null)
  const [uploadFileList, setUploadFileList] = useState<UploadFile[]>([])
  const [calculatedSha256, setCalculatedSha256] = useState('')
  const [sha256Calculating, setSha256Calculating] = useState(false)
  const [sha256Error, setSha256Error] = useState<string | null>(null)
  const uploadHashRequestId = useRef(0)

  const loadReleases = useCallback(async (
    nextPage = page,
    nextPageSize = pageSize,
    nextChannel = channel,
    nextIncludeDisabled = includeDisabled,
  ) => {
    setLoading(true)
    try {
      const result = await getWpfAppReleases({
        page: nextPage,
        pageSize: nextPageSize,
        channel: nextChannel,
        includeDisabled: nextIncludeDisabled,
      })
      setReleases(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)

      const policySummary = getWpfPolicySummary(result.items)
      if (policySummary) {
        policyForm.setFieldsValue({
          channel: policySummary.channel,
          targetVersion: policySummary.targetVersion,
          minimumSupportedVersion: policySummary.minimumSupportedVersion,
          forceUpdate: policySummary.forceUpdate,
        })
      } else {
        policyForm.setFieldsValue({
          channel: nextChannel,
          targetVersion: undefined,
          minimumSupportedVersion: undefined,
          forceUpdate: false,
        })
      }
    } catch (error) {
      console.error('Failed to load WPF releases', error)
      message.error(getWpfVersionErrorMessage(error, t('system.wpfVersions.loadFailed', '加载 WPF 版本失败')))
    } finally {
      setLoading(false)
    }
  }, [channel, includeDisabled, page, pageSize, policyForm, t])

  useEffect(() => {
    void loadReleases(1, pageSize, channel, includeDisabled)
  }, [channel, includeDisabled, loadReleases, pageSize])

  const activeVersionOptions = useMemo(
    () => releases
      .filter((item) => item.isActive)
      .map((item) => ({ label: `${item.version} (${item.fileName})`, value: item.version })),
    [releases],
  )

  const policySummary = useMemo(() => getWpfPolicySummary(releases), [releases])
  const currentVersionText = useMemo(() => getWpfCurrentVersionText(releases), [releases])

  const validatePolicyPayload = useCallback((input: WpfReleasePolicyRequest) => {
    const payload = buildWpfPolicyPayload({ ...input })
    if (!canSubmitWpfPolicy(payload)) {
      message.warning(t('system.wpfVersions.policyMissing', '请选择目标版本和最低支持版本'))
      return null
    }
    if (getWpfPolicyRangeError(payload) === 'INVALID_VERSION_RANGE') {
      message.warning(t('system.wpfVersions.invalidVersionRange', '最低支持版本不能高于目标版本'))
      return null
    }

    return payload
  }, [t])

  const submitPolicy = async (input: WpfReleasePolicyRequest, options?: { rollbackConfirmed?: boolean }) => {
    const payload = validatePolicyPayload({
      ...input,
      rollbackConfirmed: options?.rollbackConfirmed ?? input.rollbackConfirmed,
    })
    if (!payload) {
      return
    }

    setPolicySaving(true)
    try {
      await saveWpfReleasePolicy(payload)
      message.success(
        payload.isRollback
          ? t('system.wpfVersions.rollbackSuccess', '回退策略已保存')
          : t('system.wpfVersions.policySaved', '版本策略已保存'),
      )
      await loadReleases(1, pageSize, payload.channel, includeDisabled)
      setChannel(payload.channel)
    } catch (error) {
      console.error('Failed to save WPF release policy', error)
      message.error(getWpfVersionErrorMessage(error, t('system.wpfVersions.policySaveFailed', '保存版本策略失败')))
    } finally {
      setPolicySaving(false)
    }
  }

  const handlePolicyFinish = async (values: PolicyFormValues) => {
    const input: WpfReleasePolicyRequest = {
      channel: values.channel,
      targetVersion: values.targetVersion,
      minimumSupportedVersion: values.minimumSupportedVersion,
      forceUpdate: Boolean(values.forceUpdate),
      isRollback: isWpfRollbackTarget(values.targetVersion, releases),
    }
    const payload = validatePolicyPayload(input)
    if (!payload) {
      return
    }

    if (payload.isRollback) {
      Modal.confirm({
        title: t('system.wpfVersions.rollbackConfirm', '确认回退到该版本？'),
        content: t('system.wpfVersions.rollbackConfirmDescription', '回退会让收银端安装较低版本，请确认这是管理员预期操作。'),
        okText: t('common.confirm', '确认'),
        cancelText: t('common.cancel', '取消'),
        onOk: () => submitPolicy(payload, { rollbackConfirmed: true }),
      })
      return
    }

    await submitPolicy(payload)
  }

  const handleSetCurrent = async (record: WpfAppRelease) => {
    const values = policyForm.getFieldsValue()
    const input: WpfReleasePolicyRequest = {
      channel: record.channel,
      targetVersion: record.version,
      minimumSupportedVersion: getEffectiveWpfMinimumSupportedVersion({
        targetVersion: record.version,
        minimumSupportedVersion: values.minimumSupportedVersion || record.minimumSupportedVersion || record.version,
      }),
      forceUpdate: Boolean(values.forceUpdate),
      isRollback: isWpfRollbackTarget(record.version, releases),
    }
    const payload = validatePolicyPayload(input)
    if (!payload) {
      return
    }

    if (payload.isRollback) {
      Modal.confirm({
        title: t('system.wpfVersions.rollbackConfirm', '确认回退到该版本？'),
        content: t('system.wpfVersions.rollbackConfirmDescription', '回退会让收银端安装较低版本，请确认这是管理员预期操作。'),
        okText: t('common.confirm', '确认'),
        cancelText: t('common.cancel', '取消'),
        onOk: () => submitPolicy(payload, { rollbackConfirmed: true }),
      })
      return
    }

    await submitPolicy(payload)
  }

  const handleRollback = async (record: WpfAppRelease) => {
    const values = policyForm.getFieldsValue()
    await submitPolicy({
      channel: record.channel,
      targetVersion: record.version,
      minimumSupportedVersion: getEffectiveWpfMinimumSupportedVersion({
        targetVersion: record.version,
        minimumSupportedVersion: values.minimumSupportedVersion || record.minimumSupportedVersion || record.version,
      }),
      forceUpdate: Boolean(values.forceUpdate),
      isRollback: true,
      rollbackConfirmed: true,
    }, { rollbackConfirmed: true })
  }

  const handleToggleReleaseActive = async (record: WpfAppRelease, nextIsActive: boolean) => {
    setStatusUpdatingId(record.id)
    try {
      await updateWpfAppRelease(record.id, { isActive: nextIsActive })
      message.success(
        nextIsActive
          ? t('system.wpfVersions.restoreSuccess', 'WPF 版本已恢复')
          : t('system.wpfVersions.disableSuccess', 'WPF 版本已禁用'),
      )
      await loadReleases(page, pageSize, channel, includeDisabled)
    } catch (error) {
      console.error('Failed to update WPF release status', error)
      message.error(getWpfVersionErrorMessage(error, t('system.wpfVersions.statusUpdateFailed', '更新 WPF 版本状态失败')))
    } finally {
      setStatusUpdatingId(null)
    }
  }

  const resetUploadFile = useCallback(() => {
    uploadHashRequestId.current += 1
    setUploadFile(null)
    setUploadFileList([])
    setCalculatedSha256('')
    setSha256Error(null)
    setSha256Calculating(false)
    uploadForm.resetFields(['fileSize', 'sha256'])
  }, [uploadForm])

  const closeUploadDrawer = useCallback(() => {
    setUploadOpen(false)
    resetUploadFile()
    uploadForm.resetFields()
  }, [resetUploadFile, uploadForm])

  const prepareUploadFile = useCallback(async (file: File) => {
    const requestId = uploadHashRequestId.current + 1
    const uploadFileWithUid = file as File & { uid?: string }
    uploadHashRequestId.current = requestId
    setUploadFile(file)
    setUploadFileList([{
      uid: uploadFileWithUid.uid ?? `${file.name}-${file.lastModified}`,
      name: file.name,
      status: 'done',
      size: file.size,
      type: file.type,
      originFileObj: file as UploadFile['originFileObj'],
    }])
    setCalculatedSha256('')
    setSha256Error(null)
    setSha256Calculating(true)
    uploadForm.setFieldsValue({
      fileSize: file.size,
      installerArguments: getDefaultWpfInstallerArguments(file.name),
      sha256: '',
    })

    try {
      // 中文注释：只使用浏览器现场计算出的 SHA-256，避免人工粘贴校验值和上传文件不一致。
      const sha256 = await calculateFileSha256(file)
      if (uploadHashRequestId.current !== requestId) {
        return
      }

      setCalculatedSha256(sha256)
      uploadForm.setFieldValue('sha256', sha256)
    } catch (error) {
      if (uploadHashRequestId.current !== requestId) {
        return
      }

      console.error('Failed to calculate WPF installer SHA-256', error)
      const messageText = t('system.wpfVersions.sha256CalculateFailed', '无法计算安装包 SHA-256，请重新选择文件')
      setSha256Error(messageText)
      uploadForm.setFieldValue('sha256', '')
      message.error(messageText)
    } finally {
      if (uploadHashRequestId.current === requestId) {
        setSha256Calculating(false)
      }
    }
  }, [t, uploadForm])

  const handleUploadFinish = async (values: UploadFormValues) => {
    if (!uploadFile) {
      message.warning(t('system.wpfVersions.selectInstaller', '请先选择安装包'))
      return
    }
    if (!isSupportedWpfInstallerFile(uploadFile.name)) {
      message.error(t('system.wpfVersions.unsupportedInstaller', '只支持 .exe 和 .msi 安装包'))
      return
    }
    if (sha256Calculating) {
      message.warning(t('system.wpfVersions.sha256Calculating', '正在计算安装包 SHA-256，请稍候'))
      return
    }
    if (sha256Error) {
      message.error(sha256Error)
      return
    }

    const sha256 = calculatedSha256.trim().toLowerCase()
    if (!/^[0-9a-f]{64}$/.test(sha256)) {
      message.error(t('system.wpfVersions.sha256Required', '请先选择安装包并等待 SHA-256 自动计算完成'))
      return
    }

    setUploading(true)
    try {
      // 中文注释：先拿固定版本路径的上传初始化，再直传文件，最后登记版本元数据，确保上传对象和发布记录同源。
      const uploadInit = await initWpfReleaseUpload({
        channel: normalizeWpfReleaseChannel(values.channel),
        version: values.version,
        fileName: uploadFile.name,
        fileSize: values.fileSize ?? uploadFile.size,
        sha256,
        contentType: uploadFile.type || 'application/octet-stream',
      })
      await uploadWpfReleaseFile(uploadFile, uploadInit)
      await createWpfAppRelease({
        version: values.version,
        channel: normalizeWpfReleaseChannel(values.channel),
        fileName: uploadFile.name,
        fileSize: values.fileSize ?? uploadFile.size,
        sha256,
        installerType: inferWpfInstallerType(uploadFile.name),
        installerArguments: values.installerArguments,
        objectKey: uploadInit.objectKey,
        downloadUrl: uploadInit.downloadUrl,
        releaseNotes: values.releaseNotes,
      })

      message.success(t('system.wpfVersions.uploadSuccess', 'WPF 版本已上传并登记'))
      closeUploadDrawer()
      uploadForm.resetFields()
      setChannel(normalizeWpfReleaseChannel(values.channel))
      await loadReleases(1, pageSize, normalizeWpfReleaseChannel(values.channel), includeDisabled)
    } catch (error) {
      console.error('Failed to upload WPF release', error)
      message.error(getWpfVersionErrorMessage(error, t('system.wpfVersions.uploadFailed', '上传或登记 WPF 版本失败')))
    } finally {
      setUploading(false)
    }
  }

  const columns: ColumnsType<WpfAppRelease> = [
    {
      title: t('system.wpfVersions.version', '版本'),
      dataIndex: 'version',
      width: 120,
      render: (value: string, record) => (
        <Space direction="vertical" size={2}>
          <Text strong>{value}</Text>
          {record.isRollback ? <Tag color="orange">{t('system.wpfVersions.rollbackTag', '回退')}</Tag> : null}
        </Space>
      ),
    },
    {
      title: t('system.wpfVersions.channel', '通道'),
      dataIndex: 'channel',
      width: 110,
      render: (value: string) => <Tag>{value}</Tag>,
    },
    {
      title: t('system.wpfVersions.file', '文件'),
      dataIndex: 'fileName',
      width: 220,
      render: (value: string, record) => (
        <Space direction="vertical" size={2}>
          <Text>{value || '-'}</Text>
          <Text type="secondary">{record.installerType?.toUpperCase() || '-'}</Text>
        </Space>
      ),
    },
    {
      title: t('system.wpfVersions.size', '大小'),
      dataIndex: 'fileSize',
      width: 110,
      render: formatFileSize,
    },
    {
      title: t('system.wpfVersions.sha256', '校验值'),
      dataIndex: 'sha256',
      width: 180,
      render: (value: string | null) => <Text code>{shortSha(value)}</Text>,
    },
    {
      title: t('system.wpfVersions.status', '状态'),
      width: 210,
      render: (_, record) => (
        <Space wrap>
          {record.isCurrent ? <Tag color="green">{t('system.wpfVersions.current', '当前')}</Tag> : null}
          {record.downloadUrl ? <Tag color="blue">{t('system.wpfVersions.ready', '可下载')}</Tag> : null}
          <Tag color={record.isActive ? 'default' : 'volcano'}>
            {record.isActive
              ? t('system.wpfVersions.active', '已启用')
              : t('system.wpfVersions.inactive', '已禁用')}
          </Tag>
        </Space>
      ),
    },
    {
      title: t('system.wpfVersions.target', '当前目标'),
      width: 130,
      render: (_, record) => record.targetVersion || (record.isCurrent ? record.version : '-'),
    },
    {
      title: t('system.wpfVersions.forceStatus', '强制状态'),
      dataIndex: 'forceUpdate',
      width: 120,
      render: (value: boolean, record) => (
        record.isCurrent && value
          ? <Tag color="red">{t('common.enabled', '已启用')}</Tag>
          : <Tag>{t('common.disabled', '未启用')}</Tag>
      ),
    },
    {
      title: t('system.wpfVersions.updatedAt', '更新时间'),
      dataIndex: 'updatedAt',
      width: 160,
      render: formatDateTime,
    },
    {
      title: t('common.actions', '操作'),
      fixed: 'right',
      width: 360,
      render: (_, record) => {
        const policyReferenced = isPolicyReferencedRelease(record)
        const cannotPromote = !record.isActive || record.isCurrent

        return (
          <Space wrap>
            {record.downloadUrl ? (
              <Button size="small" icon={<DownloadOutlined />} href={record.downloadUrl} target="_blank">
                {t('common.download', '下载')}
              </Button>
            ) : null}
            <Popconfirm
              title={t('system.wpfVersions.setCurrentConfirm', '设为当前发布版本？')}
              disabled={!canManageAppDownloads || cannotPromote}
              onConfirm={() => void handleSetCurrent(record)}
            >
              <Button size="small" disabled={!canManageAppDownloads || cannotPromote}>
                {t('system.wpfVersions.setCurrent', '设为当前')}
              </Button>
            </Popconfirm>
            <Popconfirm
              title={t('system.wpfVersions.rollbackConfirm', '确认回退到该版本？')}
              disabled={!canManageAppDownloads || cannotPromote}
              onConfirm={() => void handleRollback(record)}
            >
              <Button size="small" icon={<RollbackOutlined />} disabled={!canManageAppDownloads || cannotPromote}>
                {t('system.wpfVersions.rollback', '回退')}
              </Button>
            </Popconfirm>
            {record.isActive ? (
              <Popconfirm
                title={t('system.wpfVersions.disableConfirm', '确认禁用这个版本？')}
                disabled={!canManageAppDownloads || policyReferenced}
                onConfirm={() => void handleToggleReleaseActive(record, false)}
              >
                <Button
                  size="small"
                  danger
                  loading={statusUpdatingId === record.id}
                  disabled={!canManageAppDownloads || policyReferenced}
                >
                  {t('system.wpfVersions.disable', '禁用')}
                </Button>
              </Popconfirm>
            ) : (
              <Popconfirm
                title={t('system.wpfVersions.restoreConfirm', '确认恢复这个版本？')}
                disabled={!canManageAppDownloads}
                onConfirm={() => void handleToggleReleaseActive(record, true)}
              >
                <Button
                  size="small"
                  loading={statusUpdatingId === record.id}
                  disabled={!canManageAppDownloads}
                >
                  {t('system.wpfVersions.restore', '恢复')}
                </Button>
              </Popconfirm>
            )}
          </Space>
        )
      },
    },
  ]

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card
        title={t('system.wpfVersions.title', 'WPF 版本管理')}
        extra={(
          <Space wrap>
            <Select
              style={{ width: 140 }}
              value={channel}
              options={WPF_RELEASE_CHANNELS.map((item) => ({ label: item, value: item }))}
              onChange={setChannel}
            />
            <Space size={8}>
              <Text type="secondary">{t('system.wpfVersions.includeDisabled', '显示已禁用')}</Text>
              <Switch checked={includeDisabled} onChange={setIncludeDisabled} />
            </Space>
            <Button icon={<ReloadOutlined />} loading={loading} onClick={() => void loadReleases()}>
              {t('common.refresh', '刷新')}
            </Button>
            <Button
              type="primary"
              icon={<UploadOutlined />}
              disabled={!canManageAppDownloads}
              onClick={() => setUploadOpen(true)}
            >
              {t('system.wpfVersions.upload', '上传版本')}
            </Button>
          </Space>
        )}
      >
        {!canManageAppDownloads ? (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message={t('system.wpfVersions.readOnlyHint', '当前账号可查看 WPF 版本，但没有发布、回退或强制更新权限。')}
          />
        ) : null}
        <Descriptions bordered size="small" column={{ xs: 1, md: 2, xl: 4 }}>
          <Descriptions.Item label={t('system.wpfVersions.currentVersion', '当前版本')}>
            {currentVersionText ?? '-'}
          </Descriptions.Item>
          <Descriptions.Item label={t('system.wpfVersions.minimumSupportedVersion', '最低支持版本')}>
            {policySummary?.minimumSupportedVersion ?? '-'}
          </Descriptions.Item>
          <Descriptions.Item label={t('system.wpfVersions.forceUpdate', '强制更新')}>
            {policySummary?.forceUpdate ? t('common.yes', '是') : t('common.no', '否')}
          </Descriptions.Item>
          <Descriptions.Item label={t('system.wpfVersions.objectKeyPrefix', 'COS 路径')}>
            <Text code>wpf-releases/{channel}/{'{version}'}/{'{fileName}'}</Text>
          </Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title={t('system.wpfVersions.policyTitle', '发布策略')}>
        <Form<PolicyFormValues>
          form={policyForm}
          layout="inline"
          initialValues={{ channel, forceUpdate: false }}
          onFinish={(values) => void handlePolicyFinish(values)}
        >
          <Form.Item name="channel" label={t('system.wpfVersions.channel', '通道')} rules={[{ required: true }]}>
            <Select style={{ width: 140 }} options={WPF_RELEASE_CHANNELS.map((item) => ({ label: item, value: item }))} />
          </Form.Item>
          <Form.Item
            name="targetVersion"
            label={t('system.wpfVersions.targetVersion', '目标版本')}
            rules={[{ required: true }]}
          >
            <Select style={{ width: 220 }} options={activeVersionOptions} placeholder={t('system.wpfVersions.selectVersion', '选择版本')} />
          </Form.Item>
          <Form.Item
            name="minimumSupportedVersion"
            label={t('system.wpfVersions.minimumSupportedVersion', '最低支持版本')}
            rules={[{ required: true }]}
          >
            <Select style={{ width: 220 }} options={activeVersionOptions} placeholder={t('system.wpfVersions.selectVersion', '选择版本')} />
          </Form.Item>
          <Form.Item name="forceUpdate" label={t('system.wpfVersions.forceUpdate', '强制更新')} valuePropName="checked">
            <Switch disabled={!canManageAppDownloads} />
          </Form.Item>
          <Form.Item>
            <Button
              type="primary"
              htmlType="submit"
              icon={<SaveOutlined />}
              loading={policySaving}
              disabled={!canManageAppDownloads}
            >
              {t('system.wpfVersions.savePolicy', '保存策略')}
            </Button>
          </Form.Item>
        </Form>
      </Card>

      <Card title={t('system.wpfVersions.listTitle', '版本列表')}>
        <Table<WpfAppRelease>
          rowKey={getReleaseRowKey}
          loading={loading}
          columns={columns}
          dataSource={releases}
          scroll={{ x: 1700 }}
          locale={{
            emptyText: loading ? undefined : <Empty description={t('system.wpfVersions.empty', '暂无 WPF 版本')} />,
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => void loadReleases(nextPage, nextPageSize, channel, includeDisabled),
          }}
          expandable={{
            expandedRowRender: (record) => (
              <Space direction="vertical" size={4}>
                <Paragraph style={{ marginBottom: 0 }}>
                  {record.releaseNotes || t('system.wpfVersions.noReleaseNotes', '暂无发布说明')}
                </Paragraph>
                <Text type="secondary">ObjectKey: {record.objectKey || '-'}</Text>
                <Text type="secondary">Arguments: {record.installerArguments || '-'}</Text>
              </Space>
            ),
          }}
        />
      </Card>

      <Drawer
        title={t('system.wpfVersions.uploadTitle', '上传 WPF 安装包')}
        open={uploadOpen}
        width={560}
        onClose={closeUploadDrawer}
        destroyOnClose
      >
        <Form<UploadFormValues>
          form={uploadForm}
          layout="vertical"
          initialValues={{ channel, installerArguments: getDefaultWpfInstallerArguments('hbpos.exe') }}
          onFinish={(values) => void handleUploadFinish(values)}
        >
          <Form.Item label={t('system.wpfVersions.installer', '安装包')} required>
            <Dragger
              accept=".exe,.msi"
              maxCount={1}
              fileList={uploadFileList}
              beforeUpload={(file) => {
                if (!isSupportedWpfInstallerFile(file.name)) {
                  message.error(t('system.wpfVersions.unsupportedInstaller', '只支持 .exe 和 .msi 安装包'))
                  return Upload.LIST_IGNORE
                }
                void prepareUploadFile(file)
                return false
              }}
              onRemove={resetUploadFile}
            >
              <p className="ant-upload-drag-icon"><CloudUploadOutlined /></p>
              <p className="ant-upload-text">{t('system.wpfVersions.dragInstaller', '点击或拖拽 .exe/.msi 到此处')}</p>
              <p className="ant-upload-hint">{t('system.wpfVersions.uploadHint', '文件会先上传到腾讯云 COS，再登记为可发布版本。')}</p>
            </Dragger>
          </Form.Item>
          <Form.Item name="version" label={t('system.wpfVersions.version', '版本')} rules={[{ required: true }]}>
            <Input placeholder="1.2.3" />
          </Form.Item>
          <Form.Item name="channel" label={t('system.wpfVersions.channel', '通道')} rules={[{ required: true }]}>
            <Select options={WPF_RELEASE_CHANNELS.map((item) => ({ label: item, value: item }))} />
          </Form.Item>
          <Form.Item name="fileSize" label={t('system.wpfVersions.size', '大小')}>
            <InputNumber style={{ width: '100%' }} disabled addonAfter="bytes" />
          </Form.Item>
          <Form.Item
            name="sha256"
            label={t('system.wpfVersions.sha256', '校验值')}
            rules={[{ required: true, message: t('system.wpfVersions.sha256Required', '请先选择安装包并等待 SHA-256 自动计算完成') }]}
            validateStatus={sha256Error ? 'error' : sha256Calculating ? 'validating' : calculatedSha256 ? 'success' : undefined}
            help={sha256Error ?? (sha256Calculating
              ? t('system.wpfVersions.sha256Calculating', '正在计算安装包 SHA-256，请稍候')
              : calculatedSha256
                ? t('system.wpfVersions.sha256Calculated', '已自动计算 SHA-256')
                : t('system.wpfVersions.sha256AutoHint', '选择安装包后自动计算 SHA-256'))}
          >
            <Input placeholder="SHA-256" readOnly />
          </Form.Item>
          <Form.Item name="installerArguments" label={t('system.wpfVersions.installerArguments', '安装参数')}>
            <Input placeholder="/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" />
          </Form.Item>
          <Form.Item name="releaseNotes" label={t('system.wpfVersions.releaseNotes', '发布说明')}>
            <Input.TextArea rows={5} />
          </Form.Item>
          <Space>
            <Button
              type="primary"
              htmlType="submit"
              loading={uploading || sha256Calculating}
              disabled={!canManageAppDownloads || sha256Calculating || Boolean(sha256Error)}
            >
              {t('system.wpfVersions.uploadAndRegister', '上传并登记')}
            </Button>
            <Button onClick={closeUploadDrawer}>{t('common.cancel', '取消')}</Button>
          </Space>
        </Form>
      </Drawer>
    </Space>
  )
}
