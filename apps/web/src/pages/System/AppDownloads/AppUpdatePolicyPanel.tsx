import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Alert,
  Button,
  Card,
  Col,
  Descriptions,
  Empty,
  Form,
  Input,
  Modal,
  Radio,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tabs,
  Tag,
  Typography,
  message,
} from 'antd'
import type { FormInstance } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  AppstoreAddOutlined,
  LinkOutlined,
  ReloadOutlined,
  SaveOutlined,
} from '@ant-design/icons'
import { appUpdatePolicyService } from '../../../services/appUpdatePolicyService'
import type {
  AppUpdateApp,
  AppUpdateTargetStoreOption,
  IosAppStoreRelease,
  NativeUpdatePolicy,
  PosIpadOtaRelease,
  PosIpadOtaRollout,
} from '../../../types/appUpdatePolicy'
import {
  buildAppStoreReleaseRegistrationSummary,
  buildNativePolicyConfirmationSummary,
  buildNativeUpdatePolicyRequest,
  buildOtaPolicyConfirmationSummary,
  buildOtaRolloutRequest,
  resolveNativeReleaseStatus,
  resolveOtaReleaseStatus,
  type AppStoreReleaseRegistrationFormValue,
  type AppStoreReleaseRegistrationSummary,
  type AppUpdatePolicyConfirmationSummary,
  type NativeUpdatePolicyFormValue,
  type OtaRolloutFormValue,
} from './appUpdatePolicyLogic'
import { formatAppDownloadLocalDateTime } from './time'

interface AppUpdatePolicyPanelProps {
  canManage: boolean
}

interface PolicySaveConfirmation {
  kind: 'native' | 'ota'
  enabled: boolean
  summary: AppUpdatePolicyConfirmationSummary
  releaseLabel: string
  onOk: () => Promise<void>
}

const EMPTY_NATIVE_POLICY: NativeUpdatePolicy = {
  id: null,
  enabled: false,
  policyVersion: 0,
  releaseId: null,
  latestVersion: null,
  minimumSupportedVersion: null,
  appStoreUrl: null,
  releaseMessage: null,
  targetScope: 'all',
  targetStoreGuids: [],
  updatedAt: null,
  updatedBy: null,
}

const EMPTY_OTA_ROLLOUT: PosIpadOtaRollout = {
  id: null,
  enabled: false,
  policyVersion: 0,
  releaseId: null,
  forceUpdate: false,
  targetScope: 'all',
  targetStoreGuids: [],
  releaseMessage: null,
  release: null,
  updatedAt: null,
  updatedBy: null,
}

function toNativeFormValue(policy: NativeUpdatePolicy): NativeUpdatePolicyFormValue {
  return {
    enabled: policy.enabled,
    releaseId: policy.releaseId,
    minimumSupportedVersion: policy.minimumSupportedVersion,
    releaseMessage: policy.releaseMessage,
    targetScope: policy.targetScope,
    targetStoreGuids: policy.targetStoreGuids,
  }
}

function toOtaFormValue(rollout: PosIpadOtaRollout): OtaRolloutFormValue {
  return {
    enabled: rollout.enabled,
    releaseId: rollout.releaseId,
    forceUpdate: rollout.forceUpdate,
    targetScope: rollout.targetScope,
    targetStoreGuids: rollout.targetStoreGuids,
    releaseMessage: rollout.releaseMessage,
  }
}

function formatNativeReleaseLabel(release: IosAppStoreRelease) {
  return `${release.version} (${release.buildNumber || '--'})`
}

function formatOtaReleaseLabel(release: PosIpadOtaRelease) {
  return `${release.channel} · ${release.runtimeVersion} · ${release.iosUpdateId.slice(0, 8)}`
}

function safeExternalUrl(value: string | null | undefined, allowedHosts: readonly string[]) {
  if (!value) {
    return null
  }

  try {
    const url = new URL(value)
    return url.protocol === 'https:'
      && !url.username
      && !url.password
      && allowedHosts.includes(url.hostname)
      ? url.toString()
      : null
  } catch {
    return null
  }
}

export default function AppUpdatePolicyPanel({ canManage }: AppUpdatePolicyPanelProps) {
  const { t } = useTranslation()
  const [mobileForm] = Form.useForm<NativeUpdatePolicyFormValue>()
  const [ipadForm] = Form.useForm<NativeUpdatePolicyFormValue>()
  const [otaForm] = Form.useForm<OtaRolloutFormValue>()
  const [registerForm] = Form.useForm<AppStoreReleaseRegistrationFormValue>()
  const [mobileReleases, setMobileReleases] = useState<IosAppStoreRelease[]>([])
  const [ipadReleases, setIpadReleases] = useState<IosAppStoreRelease[]>([])
  const [otaReleases, setOtaReleases] = useState<PosIpadOtaRelease[]>([])
  const [mobilePolicy, setMobilePolicy] = useState<NativeUpdatePolicy | null>(null)
  const [ipadPolicy, setIpadPolicy] = useState<NativeUpdatePolicy | null>(null)
  const [otaRollout, setOtaRollout] = useState<PosIpadOtaRollout | null>(null)
  const [storeOptions, setStoreOptions] = useState<AppUpdateTargetStoreOption[]>([])
  const [loading, setLoading] = useState(false)
  const [loaded, setLoaded] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)
  const [mobileSaving, setMobileSaving] = useState(false)
  const [ipadSaving, setIpadSaving] = useState(false)
  const [otaSaving, setOtaSaving] = useState(false)
  const [registerApp, setRegisterApp] = useState<AppUpdateApp | null>(null)
  const [registerSaving, setRegisterSaving] = useState(false)
  const loadRequestIdRef = useRef(0)

  const mobileEnabled = Form.useWatch('enabled', mobileForm) ?? false
  const ipadEnabled = Form.useWatch('enabled', ipadForm) ?? false
  const ipadTargetScope = Form.useWatch('targetScope', ipadForm) ?? 'all'
  const otaEnabled = Form.useWatch('enabled', otaForm) ?? false
  const otaTargetScope = Form.useWatch('targetScope', otaForm) ?? 'all'

  const loadData = useCallback(async () => {
    const requestId = loadRequestIdRef.current + 1
    loadRequestIdRef.current = requestId
    setLoading(true)
    setLoadFailed(false)

    try {
      const [
        nextMobileReleases,
        nextMobilePolicy,
        nextIpadReleases,
        nextIpadPolicy,
        nextOtaReleases,
        nextOtaRollout,
        nextStoreOptions,
      ] = await Promise.all([
        appUpdatePolicyService.getIosAppStoreReleases('mobile-ios'),
        appUpdatePolicyService.getMobileIosNativePolicy(),
        appUpdatePolicyService.getIosAppStoreReleases('pos-ipad'),
        appUpdatePolicyService.getPosIpadNativePolicy(),
        appUpdatePolicyService.getPosIpadOtaReleases(),
        appUpdatePolicyService.getPosIpadOtaRollout(),
        canManage
          ? appUpdatePolicyService.getPosIpadStoreOptions()
          : Promise.resolve([]),
      ])

      if (requestId !== loadRequestIdRef.current) {
        return
      }

      setMobileReleases(nextMobileReleases)
      setMobilePolicy(nextMobilePolicy)
      mobileForm.setFieldsValue(toNativeFormValue(nextMobilePolicy))
      setIpadReleases(nextIpadReleases)
      setIpadPolicy(nextIpadPolicy)
      ipadForm.setFieldsValue(toNativeFormValue(nextIpadPolicy))
      setOtaReleases(nextOtaReleases)
      setOtaRollout(nextOtaRollout)
      otaForm.setFieldsValue(toOtaFormValue(nextOtaRollout))
      setStoreOptions(nextStoreOptions)
      setLoaded(true)
    } catch (error) {
      if (requestId !== loadRequestIdRef.current) {
        return
      }
      console.error('Failed to load app update policies', error)
      setLoadFailed(true)
      message.error(t('system.appDownloads.updatePolicy.loadFailed'))
    } finally {
      if (requestId === loadRequestIdRef.current) {
        setLoading(false)
      }
    }
  }, [canManage, ipadForm, mobileForm, otaForm, t])

  useEffect(() => {
    void loadData()
    return () => {
      loadRequestIdRef.current += 1
    }
  }, [loadData])

  const mergedStoreOptions = useMemo(() => {
    const options = new Map(
      storeOptions.map((store) => [
        store.storeGuid,
        {
          label: `${store.storeCode} · ${store.storeName}`,
          value: store.storeGuid,
        },
      ]),
    )

    for (const storeGuid of [
      ...(ipadPolicy?.targetStoreGuids ?? []),
      ...(otaRollout?.targetStoreGuids ?? []),
    ]) {
      if (!options.has(storeGuid)) {
        options.set(storeGuid, { label: storeGuid, value: storeGuid })
      }
    }

    return [...options.values()]
  }, [ipadPolicy?.targetStoreGuids, otaRollout?.targetStoreGuids, storeOptions])

  function openRegisterModal(app: AppUpdateApp) {
    setRegisterApp(app)
    registerForm.setFieldsValue({
      appStoreId: '',
      buildNumber: '',
      storefront: 'au',
    })
  }

  function closeRegisterModal() {
    if (registerSaving) {
      return
    }
    setRegisterApp(null)
    registerForm.resetFields()
  }

  function handleRegisterRelease(values: AppStoreReleaseRegistrationFormValue) {
    if (!registerApp || registerSaving) {
      return
    }

    const app = registerApp
    const summary = buildAppStoreReleaseRegistrationSummary(values)
    Modal.confirm({
      title: t('system.appDownloads.updatePolicy.registerFinalConfirmTitle'),
      content: (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Text>
            {t('system.appDownloads.updatePolicy.registerFinalConfirmDescription')}
          </Typography.Text>
          <Descriptions size="small" bordered column={1}>
            <Descriptions.Item
              label={t('system.appDownloads.updatePolicy.registerTargetApp')}
            >
              {app === 'pos-ipad'
                ? t('system.appDownloads.updatePolicy.tabs.ipadNative')
                : t('system.appDownloads.updatePolicy.tabs.mobile')}
            </Descriptions.Item>
            <Descriptions.Item
              label={t('system.appDownloads.updatePolicy.appStoreId')}
            >
              {summary.appStoreId}
            </Descriptions.Item>
            <Descriptions.Item
              label={t('system.appDownloads.updatePolicy.buildNumber')}
            >
              {summary.buildNumber}
            </Descriptions.Item>
            <Descriptions.Item
              label={t('system.appDownloads.updatePolicy.storefront')}
            >
              {summary.storefront.toUpperCase()}
            </Descriptions.Item>
          </Descriptions>
        </Space>
      ),
      okText: t('system.appDownloads.updatePolicy.verifyAndRegister'),
      cancelText: t('common.cancel'),
      width: 560,
      onOk: () => registerRelease(app, summary),
    })
  }

  async function registerRelease(
    app: AppUpdateApp,
    summary: AppStoreReleaseRegistrationSummary,
  ) {
    setRegisterSaving(true)
    try {
      const release = await appUpdatePolicyService.createIosAppStoreRelease({
        app,
        ...summary,
      })
      const updateReleases = (items: IosAppStoreRelease[]) => [
        release,
        ...items.filter((item) => item.id !== release.id),
      ]
      if (app === 'mobile-ios') {
        setMobileReleases(updateReleases)
      } else {
        setIpadReleases(updateReleases)
      }
      message.success(t('system.appDownloads.updatePolicy.registerSuccess'))
      setRegisterApp(null)
      registerForm.resetFields()
    } catch (error) {
      console.error('Failed to register App Store release', error)
      message.error(t('system.appDownloads.updatePolicy.registerFailed'))
    } finally {
      setRegisterSaving(false)
    }
  }

  function confirmPolicySave({
    kind,
    enabled,
    summary,
    releaseLabel,
    onOk,
  }: PolicySaveConfirmation) {
    const storeLabels = new Map(
      mergedStoreOptions.map((option) => [String(option.value), String(option.label)]),
    )
    const selectedStoreLabels = summary.targetStoreGuids.map(
      (storeGuid) => storeLabels.get(storeGuid) ?? storeGuid,
    )
    const scopeSummary = summary.targetScope === 'stores'
      ? (
          <Space direction="vertical" size={2}>
            <Typography.Text>
              {t('system.appDownloads.updatePolicy.confirmSelectedStores', {
                count: selectedStoreLabels.length,
              })}
            </Typography.Text>
            <Typography.Text
              type="secondary"
              style={{
                display: 'block',
                maxHeight: 96,
                overflowY: 'auto',
                overflowWrap: 'anywhere',
              }}
            >
              {selectedStoreLabels.join(', ')}
            </Typography.Text>
          </Space>
        )
      : t('system.appDownloads.updatePolicy.targetAll')
    const updateMode = kind === 'native'
      ? (
          summary.updateMode === 'required'
            ? t('system.appDownloads.updatePolicy.confirmNativeRequiredMode', {
                version: summary.minimumSupportedVersion,
              })
            : t('system.appDownloads.updatePolicy.confirmNativeOptionalMode')
        )
      : (
          summary.updateMode === 'required'
            ? t('system.appDownloads.updatePolicy.confirmOtaRequiredMode')
            : t('system.appDownloads.updatePolicy.confirmOtaOptionalMode')
        )

    Modal.confirm({
      title: enabled
        ? t(
            kind === 'native'
              ? 'system.appDownloads.updatePolicy.activateNativeConfirmTitle'
              : 'system.appDownloads.updatePolicy.activateOtaConfirmTitle',
          )
        : t('system.appDownloads.updatePolicy.disableConfirmTitle'),
      content: enabled
        ? (
            <Space direction="vertical" size={12} style={{ width: '100%' }}>
              <Typography.Text>
                {t(
                  kind === 'native'
                    ? 'system.appDownloads.updatePolicy.activateNativeConfirmDescription'
                    : 'system.appDownloads.updatePolicy.activateOtaConfirmDescription',
                )}
              </Typography.Text>
              <Descriptions size="small" bordered column={1}>
                <Descriptions.Item
                  label={t('system.appDownloads.updatePolicy.confirmRelease')}
                >
                  <Typography.Text style={{ overflowWrap: 'anywhere' }}>
                    {releaseLabel}
                  </Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item
                  label={t('system.appDownloads.updatePolicy.confirmScope')}
                >
                  {scopeSummary}
                </Descriptions.Item>
                <Descriptions.Item
                  label={t('system.appDownloads.updatePolicy.confirmUpdateMode')}
                >
                  {updateMode}
                </Descriptions.Item>
              </Descriptions>
            </Space>
          )
        : t('system.appDownloads.updatePolicy.disableConfirmDescription'),
      okText: t('common.confirm'),
      cancelText: t('common.cancel'),
      okButtonProps: enabled ? undefined : { danger: true },
      width: 560,
      onOk,
    })
  }

  async function saveNativePolicy(
    app: AppUpdateApp,
    values: NativeUpdatePolicyFormValue,
  ) {
    const isIpad = app === 'pos-ipad'
    const setSaving = isIpad ? setIpadSaving : setMobileSaving
    setSaving(true)
    try {
      const nextPolicy = isIpad
        ? await appUpdatePolicyService.savePosIpadNativePolicy(
            buildNativeUpdatePolicyRequest(values, true),
          )
        : await appUpdatePolicyService.saveMobileIosNativePolicy(
            buildNativeUpdatePolicyRequest(values, false),
          )

      if (isIpad) {
        setIpadPolicy(nextPolicy)
        ipadForm.setFieldsValue(toNativeFormValue(nextPolicy))
      } else {
        setMobilePolicy(nextPolicy)
        mobileForm.setFieldsValue(toNativeFormValue(nextPolicy))
      }
      message.success(t('system.appDownloads.updatePolicy.saveSuccess'))
    } catch (error) {
      console.error('Failed to save native update policy', error)
      message.error(t('system.appDownloads.updatePolicy.saveFailed'))
      throw error
    } finally {
      setSaving(false)
    }
  }

  function handleNativeFinish(
    app: AppUpdateApp,
    values: NativeUpdatePolicyFormValue,
  ) {
    const isIpad = app === 'pos-ipad'
    const summary = buildNativePolicyConfirmationSummary(values, isIpad)
    const releases = isIpad ? ipadReleases : mobileReleases
    const release = releases.find((item) => item.id === summary.releaseId)
    confirmPolicySave({
      kind: 'native',
      enabled: values.enabled,
      summary,
      releaseLabel: release
        ? formatNativeReleaseLabel(release)
        : summary.releaseId ?? '--',
      onOk: () => saveNativePolicy(app, values),
    })
  }

  async function saveOtaRollout(values: OtaRolloutFormValue) {
    setOtaSaving(true)
    try {
      const nextRollout = await appUpdatePolicyService.savePosIpadOtaRollout(
        buildOtaRolloutRequest(values),
      )
      setOtaRollout(nextRollout)
      otaForm.setFieldsValue(toOtaFormValue(nextRollout))
      message.success(t('system.appDownloads.updatePolicy.saveSuccess'))
    } catch (error) {
      console.error('Failed to save iPad OTA rollout', error)
      message.error(t('system.appDownloads.updatePolicy.saveFailed'))
      throw error
    } finally {
      setOtaSaving(false)
    }
  }

  function handleOtaFinish(values: OtaRolloutFormValue) {
    const summary = buildOtaPolicyConfirmationSummary(values)
    const release = otaReleases.find((item) => item.id === summary.releaseId)
    confirmPolicySave({
      kind: 'ota',
      enabled: values.enabled,
      summary,
      releaseLabel: release
        ? formatOtaReleaseLabel(release)
        : summary.releaseId ?? '--',
      onOk: () => saveOtaRollout(values),
    })
  }

  function nativeReleaseColumns(policy: NativeUpdatePolicy): ColumnsType<IosAppStoreRelease> {
    return [
      {
        title: t('system.appDownloads.updatePolicy.status'),
        key: 'status',
        width: 140,
        render: (_value, release) => {
          const status = resolveNativeReleaseStatus(
            release.id,
            policy.releaseId,
            policy.enabled,
          )
          return (
            <Tag color={status === 'active' ? 'green' : 'blue'}>
              {t(`system.appDownloads.updatePolicy.${status}`)}
            </Tag>
          )
        },
      },
      {
        title: t('system.appDownloads.updatePolicy.versionBuild'),
        key: 'version',
        width: 160,
        render: (_value, release) => formatNativeReleaseLabel(release),
      },
      {
        title: t('system.appDownloads.updatePolicy.bundle'),
        dataIndex: 'bundleIdentifier',
        width: 220,
        render: (value: string) => value || '--',
      },
      {
        title: t('system.appDownloads.updatePolicy.verifiedAt'),
        dataIndex: 'appleVerifiedAtUtc',
        width: 190,
        render: (value: string) => formatAppDownloadLocalDateTime(value),
      },
      {
        title: t('column.action'),
        key: 'action',
        width: 150,
        render: (_value, release) => {
          const url = safeExternalUrl(
            release.appStoreUrl,
            ['apps.apple.com', 'itunes.apple.com'],
          )
          return (
            <Button
              size="small"
              icon={<LinkOutlined />}
              disabled={!url}
              href={url ?? undefined}
              target="_blank"
              rel="noopener noreferrer"
            >
              {t('system.appDownloads.updatePolicy.openStore')}
            </Button>
          )
        },
      },
    ]
  }

  const otaReleaseColumns = useMemo<ColumnsType<PosIpadOtaRelease>>(
    () => [
      {
        title: t('system.appDownloads.updatePolicy.status'),
        key: 'status',
        width: 180,
        render: (_value, release) => {
          const status = resolveOtaReleaseStatus(
            release.id,
            otaRollout?.releaseId,
            otaRollout?.enabled ?? false,
          )
          return (
            <Space size={4} wrap>
              <Tag color={status === 'active' ? 'green' : 'blue'}>
                {t(`system.appDownloads.updatePolicy.${status}`)}
              </Tag>
              {release.isRollback ? (
                <Tag color="orange">
                  {t('system.appDownloads.updatePolicy.rollback')}
                </Tag>
              ) : null}
            </Space>
          )
        },
      },
      {
        title: t('system.appDownloads.updatePolicy.channel'),
        dataIndex: 'channel',
        width: 180,
      },
      {
        title: t('system.appDownloads.updatePolicy.environment'),
        dataIndex: 'environment',
        width: 130,
      },
      {
        title: t('system.appDownloads.updatePolicy.runtime'),
        dataIndex: 'runtimeVersion',
        width: 150,
      },
      {
        title: t('system.appDownloads.updatePolicy.iosUpdateId'),
        dataIndex: 'iosUpdateId',
        width: 260,
        render: (value: string) => (
          <Typography.Text copyable ellipsis={{ tooltip: value }} style={{ maxWidth: 235 }}>
            {value || '--'}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.updatePolicy.updateGroupId'),
        dataIndex: 'updateGroupId',
        width: 260,
        render: (value: string) => (
          <Typography.Text copyable ellipsis={{ tooltip: value }} style={{ maxWidth: 235 }}>
            {value || '--'}
          </Typography.Text>
        ),
      },
      {
        title: t('system.appDownloads.updatePolicy.publishedAt'),
        dataIndex: 'publishedAtUtc',
        width: 190,
        render: (value: string) => formatAppDownloadLocalDateTime(value),
      },
      {
        title: t('column.action'),
        key: 'action',
        width: 170,
        render: (_value, release) => {
          const url = safeExternalUrl(release.dashboardUrl, ['expo.dev'])
          return (
            <Button
              size="small"
              icon={<LinkOutlined />}
              disabled={!url}
              href={url ?? undefined}
              target="_blank"
              rel="noopener noreferrer"
            >
              {t('system.appDownloads.updatePolicy.openDashboard')}
            </Button>
          )
        },
      },
    ],
    [otaRollout?.enabled, otaRollout?.releaseId, t],
  )

  function renderPolicyMetadata(
    policy: NativeUpdatePolicy | PosIpadOtaRollout,
    forceUpdate?: boolean,
  ) {
    return (
      <Space size={4} wrap>
        <Tag color={policy.enabled ? 'green' : 'default'}>
          {t(`system.appDownloads.updatePolicy.${policy.enabled ? 'enabled' : 'disabled'}`)}
        </Tag>
        <Tag>
          {t('system.appDownloads.updatePolicy.policyVersion')}: {policy.policyVersion}
        </Tag>
        {forceUpdate ? (
          <Tag color="red">{t('system.appDownloads.updatePolicy.forceUpdate')}</Tag>
        ) : null}
      </Space>
    )
  }

  function renderTargetFields(
    enabled: boolean,
    targetScope: string,
  ) {
    return (
      <>
        <Col xs={24} md={12}>
          <Form.Item
            name="targetScope"
            label={t('system.appDownloads.updatePolicy.targetScope')}
            rules={[{ required: enabled }]}
          >
            <Radio.Group
              disabled={!canManage || !enabled}
              options={[
                {
                  label: t('system.appDownloads.updatePolicy.targetAll'),
                  value: 'all',
                },
                {
                  label: t('system.appDownloads.updatePolicy.targetStores'),
                  value: 'stores',
                },
              ]}
            />
          </Form.Item>
        </Col>
        <Col xs={24} md={12}>
          <Form.Item
            name="targetStoreGuids"
            label={t('system.appDownloads.updatePolicy.selectStores')}
            dependencies={['enabled', 'targetScope']}
            rules={[
              ({ getFieldValue }) => ({
                validator: async (_rule, value?: string[]) => {
                  if (
                    getFieldValue('enabled')
                    && getFieldValue('targetScope') === 'stores'
                    && (!value || value.length === 0)
                  ) {
                    throw new Error(t('system.appDownloads.updatePolicy.storesRequired'))
                  }
                },
              }),
            ]}
          >
            <Select
              mode="multiple"
              allowClear
              showSearch
              optionFilterProp="label"
              options={mergedStoreOptions}
              disabled={!canManage || !enabled || targetScope !== 'stores'}
              placeholder={t('system.appDownloads.updatePolicy.selectStores')}
            />
          </Form.Item>
        </Col>
      </>
    )
  }

  function renderNativePolicyEditor(
    app: AppUpdateApp,
    policy: NativeUpdatePolicy,
    releases: IosAppStoreRelease[],
    form: FormInstance<NativeUpdatePolicyFormValue>,
    enabled: boolean,
    targetScope: string,
    saving: boolean,
  ) {
    const isIpad = app === 'pos-ipad'
    return (
      <Card
        size="small"
        title={t('system.appDownloads.updatePolicy.policy')}
        extra={renderPolicyMetadata(policy)}
      >
        <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 3 }} style={{ marginBottom: 16 }}>
          <Descriptions.Item label={t('system.appDownloads.updatePolicy.latestVersion')}>
            {policy.latestVersion || '--'}
          </Descriptions.Item>
          <Descriptions.Item label={t('system.appDownloads.updatePolicy.updatedAt')}>
            {formatAppDownloadLocalDateTime(policy.updatedAt)}
          </Descriptions.Item>
          <Descriptions.Item label={t('system.appDownloads.updatePolicy.updatedBy')}>
            {policy.updatedBy || '--'}
          </Descriptions.Item>
        </Descriptions>

        <Form<NativeUpdatePolicyFormValue>
          form={form}
          layout="vertical"
          disabled={!canManage}
          onFinish={(values) => handleNativeFinish(app, values)}
        >
          <Row gutter={[16, 0]}>
            <Col xs={24} md={8}>
              <Form.Item
                name="enabled"
                label={t('system.appDownloads.updatePolicy.policyStatus')}
                valuePropName="checked"
              >
                <Switch
                  checkedChildren={t('system.appDownloads.updatePolicy.enabled')}
                  unCheckedChildren={t('system.appDownloads.updatePolicy.disabled')}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item
                name="releaseId"
                label={t('system.appDownloads.updatePolicy.release')}
                dependencies={['enabled']}
                rules={[
                  ({ getFieldValue }) => ({
                    validator: async (_rule, value?: string) => {
                      if (getFieldValue('enabled') && !value) {
                        throw new Error(t('system.appDownloads.updatePolicy.releaseRequired'))
                      }
                    },
                  }),
                ]}
              >
                <Select
                  showSearch
                  optionFilterProp="label"
                  disabled={!canManage || !enabled}
                  placeholder={t('system.appDownloads.updatePolicy.selectRelease')}
                  options={releases.map((release) => ({
                    label: formatNativeReleaseLabel(release),
                    value: release.id,
                  }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item
                name="minimumSupportedVersion"
                label={t('system.appDownloads.updatePolicy.minimumVersion')}
                extra={t('system.appDownloads.updatePolicy.minimumVersionHelp')}
              >
                <Input
                  disabled={!canManage || !enabled}
                  placeholder={t('system.appDownloads.updatePolicy.minimumVersionPlaceholder')}
                />
              </Form.Item>
            </Col>
            {isIpad ? renderTargetFields(enabled, targetScope) : null}
            <Col span={24}>
              <Form.Item
                name="releaseMessage"
                label={t('system.appDownloads.updatePolicy.releaseMessage')}
              >
                <Input.TextArea
                  rows={3}
                  maxLength={1000}
                  showCount
                  disabled={!canManage || !enabled}
                  placeholder={t('system.appDownloads.updatePolicy.releaseMessagePlaceholder')}
                />
              </Form.Item>
            </Col>
          </Row>
          {canManage ? (
            <Button
              type="primary"
              htmlType="submit"
              icon={<SaveOutlined />}
              loading={saving}
              disabled={!loaded}
            >
              {t('common.save')}
            </Button>
          ) : null}
        </Form>
      </Card>
    )
  }

  function renderNativeTab(
    app: AppUpdateApp,
    releases: IosAppStoreRelease[],
    policy: NativeUpdatePolicy,
    form: FormInstance<NativeUpdatePolicyFormValue>,
    enabled: boolean,
    targetScope: string,
    saving: boolean,
  ) {
    return (
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Card
          size="small"
          title={t('system.appDownloads.updatePolicy.releaseFacts')}
          extra={
            canManage ? (
              <Button
                icon={<AppstoreAddOutlined />}
                onClick={() => openRegisterModal(app)}
              >
                {t('system.appDownloads.updatePolicy.registerRelease')}
              </Button>
            ) : null
          }
        >
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            message={t('system.appDownloads.updatePolicy.appleVerificationHint')}
            description={t('system.appDownloads.updatePolicy.registrationNotActivation')}
          />
          <Table<IosAppStoreRelease>
            rowKey="id"
            size="small"
            columns={nativeReleaseColumns(policy)}
            dataSource={releases}
            scroll={{ x: 860 }}
            locale={{
              emptyText: <Empty description={t('system.appDownloads.updatePolicy.noReleases')} />,
            }}
            pagination={{ pageSize: 5, hideOnSinglePage: true }}
          />
        </Card>
        {renderNativePolicyEditor(
          app,
          policy,
          releases,
          form,
          enabled,
          targetScope,
          saving,
        )}
      </Space>
    )
  }

  const tabs = [
    {
      key: 'mobile-native',
      label: t('system.appDownloads.updatePolicy.tabs.mobile'),
      children: renderNativeTab(
        'mobile-ios',
        mobileReleases,
        mobilePolicy ?? EMPTY_NATIVE_POLICY,
        mobileForm,
        mobileEnabled,
        'all',
        mobileSaving,
      ),
    },
    {
      key: 'ipad-native',
      label: t('system.appDownloads.updatePolicy.tabs.ipadNative'),
      children: renderNativeTab(
        'pos-ipad',
        ipadReleases,
        ipadPolicy ?? EMPTY_NATIVE_POLICY,
        ipadForm,
        ipadEnabled,
        ipadTargetScope,
        ipadSaving,
      ),
    },
    {
      key: 'ipad-ota',
      label: t('system.appDownloads.updatePolicy.tabs.ipadOta'),
      children: (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Card size="small" title={t('system.appDownloads.updatePolicy.releaseFacts')}>
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              message={t('system.appDownloads.updatePolicy.otaScriptHint')}
            />
            <Table<PosIpadOtaRelease>
              rowKey="id"
              size="small"
              columns={otaReleaseColumns}
              dataSource={otaReleases}
              scroll={{ x: 1420 }}
              locale={{
                emptyText: <Empty description={t('system.appDownloads.updatePolicy.noReleases')} />,
              }}
              pagination={{ pageSize: 5, hideOnSinglePage: true }}
            />
          </Card>

          <Card
            size="small"
            title={t('system.appDownloads.updatePolicy.rollout')}
            extra={renderPolicyMetadata(
              otaRollout ?? EMPTY_OTA_ROLLOUT,
              otaRollout?.enabled && otaRollout.forceUpdate,
            )}
          >
            <Descriptions
              size="small"
              column={{ xs: 1, sm: 2 }}
              style={{ marginBottom: 16 }}
            >
              <Descriptions.Item label={t('system.appDownloads.updatePolicy.updatedAt')}>
                {formatAppDownloadLocalDateTime(otaRollout?.updatedAt)}
              </Descriptions.Item>
              <Descriptions.Item label={t('system.appDownloads.updatePolicy.updatedBy')}>
                {otaRollout?.updatedBy || '--'}
              </Descriptions.Item>
            </Descriptions>
            <Form<OtaRolloutFormValue>
              form={otaForm}
              layout="vertical"
              disabled={!canManage}
              onFinish={handleOtaFinish}
            >
              <Row gutter={[16, 0]}>
                <Col xs={24} md={8}>
                  <Form.Item
                    name="enabled"
                    label={t('system.appDownloads.updatePolicy.rolloutStatus')}
                    valuePropName="checked"
                  >
                    <Switch
                      checkedChildren={t('system.appDownloads.updatePolicy.enabled')}
                      unCheckedChildren={t('system.appDownloads.updatePolicy.disabled')}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item
                    name="releaseId"
                    label={t('system.appDownloads.updatePolicy.release')}
                    dependencies={['enabled']}
                    rules={[
                      ({ getFieldValue }) => ({
                        validator: async (_rule, value?: string) => {
                          if (getFieldValue('enabled') && !value) {
                            throw new Error(t('system.appDownloads.updatePolicy.releaseRequired'))
                          }
                        },
                      }),
                    ]}
                  >
                    <Select
                      showSearch
                      optionFilterProp="label"
                      disabled={!canManage || !otaEnabled}
                      placeholder={t('system.appDownloads.updatePolicy.selectRelease')}
                      options={otaReleases.map((release) => ({
                        label: formatOtaReleaseLabel(release),
                        value: release.id,
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item
                    name="forceUpdate"
                    label={t('system.appDownloads.updatePolicy.forceUpdate')}
                    valuePropName="checked"
                  >
                    <Switch disabled={!canManage || !otaEnabled} />
                  </Form.Item>
                </Col>
                {renderTargetFields(otaEnabled, otaTargetScope)}
                <Col span={24}>
                  <Form.Item
                    name="releaseMessage"
                    label={t('system.appDownloads.updatePolicy.releaseMessage')}
                  >
                    <Input.TextArea
                      rows={3}
                      maxLength={1000}
                      showCount
                      disabled={!canManage || !otaEnabled}
                      placeholder={t('system.appDownloads.updatePolicy.releaseMessagePlaceholder')}
                    />
                  </Form.Item>
                </Col>
              </Row>
              {canManage ? (
                <Button
                  type="primary"
                  htmlType="submit"
                  icon={<SaveOutlined />}
                  loading={otaSaving}
                  disabled={!loaded}
                >
                  {t('common.save')}
                </Button>
              ) : null}
            </Form>
          </Card>
        </Space>
      ),
    },
  ]

  return (
    <>
      <Card
        title={t('system.appDownloads.updatePolicy.title')}
        extra={
          <Button
            icon={<ReloadOutlined />}
            loading={loading}
            onClick={() => void loadData()}
          >
            {t('common.refresh')}
          </Button>
        }
        loading={loading && !loaded}
      >
        <Typography.Paragraph type="secondary">
          {t('system.appDownloads.updatePolicy.subtitle')}
        </Typography.Paragraph>
        {!canManage ? (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            message={t('system.appDownloads.updatePolicy.readOnly')}
          />
        ) : null}
        {loadFailed ? (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message={t('system.appDownloads.updatePolicy.loadFailed')}
            action={
              <Button size="small" onClick={() => void loadData()}>
                {t('system.appDownloads.updatePolicy.retry')}
              </Button>
            }
          />
        ) : null}
        {loaded ? <Tabs items={tabs} destroyInactiveTabPane={false} /> : null}
      </Card>

      <Modal
        open={registerApp !== null}
        title={t('system.appDownloads.updatePolicy.registerTitle', {
          app: registerApp === 'pos-ipad'
            ? t('system.appDownloads.updatePolicy.tabs.ipadNative')
            : t('system.appDownloads.updatePolicy.tabs.mobile'),
        })}
        onCancel={closeRegisterModal}
        destroyOnHidden
        footer={[
          <Button key="cancel" disabled={registerSaving} onClick={closeRegisterModal}>
            {t('common.cancel')}
          </Button>,
          <Button
            key="register"
            type="primary"
            loading={registerSaving}
            onClick={() => registerForm.submit()}
          >
            {t('system.appDownloads.updatePolicy.verifyAndRegister')}
          </Button>,
        ]}
      >
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={t('system.appDownloads.updatePolicy.registerConfirmDescription')}
        />
        <Form<AppStoreReleaseRegistrationFormValue>
          form={registerForm}
          layout="vertical"
          initialValues={{ storefront: 'au' }}
          onFinish={handleRegisterRelease}
        >
          <Form.Item
            name="appStoreId"
            label={t('system.appDownloads.updatePolicy.appStoreId')}
            rules={[
              {
                validator: async (_rule, value?: string) => {
                  if (!value?.trim()) {
                    throw new Error(t('system.appDownloads.updatePolicy.appStoreIdRequired'))
                  }
                },
              },
              {
                pattern: /^\d{6,20}$/,
                message: t('system.appDownloads.updatePolicy.appStoreIdInvalid'),
              },
            ]}
          >
            <Input autoComplete="off" placeholder="1234567890" />
          </Form.Item>
          <Form.Item
            name="buildNumber"
            label={t('system.appDownloads.updatePolicy.buildNumber')}
            rules={[
              {
                validator: async (_rule, value?: string) => {
                  if (!value?.trim()) {
                    throw new Error(t('system.appDownloads.updatePolicy.buildNumberRequired'))
                  }
                },
              },
              {
                pattern: /^[0-9A-Za-z._-]{1,64}$/,
                message: t('system.appDownloads.updatePolicy.buildNumberInvalid'),
              },
            ]}
          >
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item
            name="storefront"
            label={t('system.appDownloads.updatePolicy.storefront')}
            rules={[
              { required: true, message: t('system.appDownloads.updatePolicy.storefrontRequired') },
              {
                pattern: /^[A-Za-z]{2}$/,
                message: t('system.appDownloads.updatePolicy.storefrontInvalid'),
              },
            ]}
          >
            <Select options={[{ label: 'Australia (au)', value: 'au' }]} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
