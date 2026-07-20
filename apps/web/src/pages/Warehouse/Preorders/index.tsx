import {
  CheckCircleOutlined,
  EditOutlined,
  HistoryOutlined,
  PlusOutlined,
  RocketOutlined,
} from '@ant-design/icons'
import {
  Alert,
  App,
  Button,
  Card,
  ConfigProvider,
  DatePicker,
  Form,
  Image,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import enUS from 'antd/locale/en_US'
import zhCN from 'antd/locale/zh_CN'
import dayjs, { type Dayjs } from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { getPreorderDateDisplay } from '../../ShopPreorder/preorderDate'
import {
  activatePreorderTemplate,
  createPreorderTemplate,
  getPreorderTemplate,
  getPreorderTemplates,
  getTemplateActivations,
  resolvePreorderItems,
  updatePreorderTemplate,
} from '../../../services/preorderService'
import { getStores } from '../../../services/storeService'
import type {
  PreorderActivationSummary,
  PreorderResolvedItem,
  PreorderTemplateDetail,
  PreorderTemplateSummary,
} from '../../../types/preorder'
import type { StoreDto } from '../../../types/store'
import {
  applyPreorderPasteTextChange,
  canSavePreorderTemplate,
  parsePreorderPaste,
  removePreorderPasteItem,
} from './preorderPaste'
import {
  beginModalRequest,
  createModalRequestGuard,
  invalidateModalRequest,
  isCurrentModalRequest,
} from './modalRequestGuard'
import './styles.css'

const { Text, Title } = Typography
const { RangePicker } = DatePicker

interface TemplateFormValues {
  name: string
  isEnabled: boolean
  notes?: string
  storeGuids: string[]
}

interface ActivationFormValues {
  range: [Dayjs, Dayjs]
  estimatedArrivalDate?: Dayjs | null
  storeGuids: string[]
}

function activationStatusTag(status: PreorderActivationSummary['status'], label: string) {
  const color = status === 'Active' ? 'processing' : status === 'Scheduled' ? 'warning' : status === 'Closed' ? 'success' : 'default'
  return <Tag color={color}>{label}</Tag>
}

export default function PreordersPage() {
  const { message } = App.useApp()
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [form] = Form.useForm<TemplateFormValues>()
  const [activationForm] = Form.useForm<ActivationFormValues>()
  const [templates, setTemplates] = useState<PreorderTemplateSummary[]>([])
  const [stores, setStores] = useState<StoreDto[]>([])
  const [loading, setLoading] = useState(false)
  const [editorOpen, setEditorOpen] = useState(false)
  const [editorLoading, setEditorLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<PreorderTemplateDetail | null>(null)
  const [pasteText, setPasteText] = useState('')
  const [pasteErrors, setPasteErrors] = useState<string[]>([])
  const [items, setItems] = useState<PreorderResolvedItem[]>([])
  const [resolving, setResolving] = useState(false)
  const [activationOpen, setActivationOpen] = useState(false)
  const [activationLoading, setActivationLoading] = useState(false)
  const [activating, setActivating] = useState(false)
  const [activationTemplate, setActivationTemplate] = useState<PreorderTemplateDetail | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [historyLoading, setHistoryLoading] = useState(false)
  const [historyTemplate, setHistoryTemplate] = useState<PreorderTemplateSummary | null>(null)
  const [activations, setActivations] = useState<PreorderActivationSummary[]>([])
  const editorRequestGuardRef = useRef(createModalRequestGuard())
  const pasteRequestGuardRef = useRef(createModalRequestGuard())
  const activationRequestGuardRef = useRef(createModalRequestGuard())
  const historyRequestGuardRef = useRef(createModalRequestGuard())
  const dateTimeFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.resolvedLanguage || i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language, i18n.resolvedLanguage],
  )
  const antdLocale = i18n.resolvedLanguage === 'en' ? enUS : zhCN

  const loadTemplates = useCallback(async () => {
    setLoading(true)
    try {
      setTemplates(await getPreorderTemplates())
    } catch {
      message.error(t('warehouse.preorders.templateLoadFailed'))
    } finally {
      setLoading(false)
    }
  }, [message, t])

  useEffect(() => {
    void loadTemplates()
    const loadStores = async () => {
      const allStores: StoreDto[] = []
      let page = 1
      let total = 0
      do {
        const result = await getStores({ page, pageSize: 100, isActive: true, sortField: 'storeName', sortOrder: 'asc' })
        allStores.push(...result.items)
        total = result.total
        if (!result.items.length) break
        page += 1
      } while (allStores.length < total)
      return allStores
    }
    void loadStores()
      .then(setStores)
      .catch(() => message.warning(t('warehouse.preorders.storeLoadFailed')))
  }, [loadTemplates, message, t])

  useEffect(() => () => {
    invalidateModalRequest(editorRequestGuardRef.current)
    invalidateModalRequest(pasteRequestGuardRef.current)
    invalidateModalRequest(activationRequestGuardRef.current)
    invalidateModalRequest(historyRequestGuardRef.current)
  }, [])

  const storeOptions = useMemo(() => stores.map((store) => ({
    value: store.storeGUID,
    label: `${store.storeName || store.storeCode} (${store.storeCode})`,
  })), [stores])

  const openCreate = () => {
    // 新开创建弹窗前先使正在加载的旧模板失效，避免 A 模板慢响应覆盖创建表单。
    invalidateModalRequest(editorRequestGuardRef.current)
    invalidateModalRequest(pasteRequestGuardRef.current)
    setEditing(null)
    setItems([])
    setPasteText('')
    setPasteErrors([])
    setEditorLoading(false)
    setResolving(false)
    setSaving(false)
    form.resetFields()
    form.setFieldsValue({ name: '', isEnabled: true, notes: '', storeGuids: [] })
    setEditorOpen(true)
  }

  const closeEditor = () => {
    invalidateModalRequest(editorRequestGuardRef.current)
    invalidateModalRequest(pasteRequestGuardRef.current)
    setEditorOpen(false)
    setEditorLoading(false)
    setResolving(false)
    setSaving(false)
    setEditing(null)
    setItems([])
    setPasteText('')
    setPasteErrors([])
    form.resetFields()
  }

  const openEdit = async (row: PreorderTemplateSummary) => {
    const requestToken = beginModalRequest(editorRequestGuardRef.current)
    invalidateModalRequest(pasteRequestGuardRef.current)
    setEditing(null)
    setItems([])
    setPasteText('')
    setPasteErrors([])
    setSaving(false)
    setResolving(false)
    form.resetFields()
    setEditorLoading(true)
    setEditorOpen(true)
    try {
      const detail = await getPreorderTemplate(row.templateGuid, requestToken.signal)
      if (!isCurrentModalRequest(editorRequestGuardRef.current, requestToken)) return
      setEditing(detail)
      setItems(detail.items.map((item, index) => ({ ...item, lineNumber: index + 1, valid: true })))
      setPasteText('')
      setPasteErrors([])
      form.setFieldsValue({
        name: detail.name,
        isEnabled: detail.isEnabled,
        notes: detail.notes,
        storeGuids: detail.stores.map((store) => store.storeGuid),
      })
    } catch {
      if (isCurrentModalRequest(editorRequestGuardRef.current, requestToken)) {
        closeEditor()
        message.error(t('warehouse.preorders.templateDetailLoadFailed'))
      }
    } finally {
      if (isCurrentModalRequest(editorRequestGuardRef.current, requestToken)) {
        setEditorLoading(false)
      }
    }
  }

  const resolvePaste = async () => {
    const parsed = parsePreorderPaste(
      pasteText,
      (key, values) => t(`warehouse.preorders.paste.${key}`, values),
    )
    invalidateModalRequest(pasteRequestGuardRef.current)
    setPasteErrors(parsed.errors)
    if (parsed.errors.length) {
      setResolving(false)
      return
    }
    const requestToken = beginModalRequest(pasteRequestGuardRef.current)
    setResolving(true)
    try {
      const resolved = await resolvePreorderItems(parsed.rows, requestToken.signal)
      if (!isCurrentModalRequest(pasteRequestGuardRef.current, requestToken)) return
      setItems(resolved)
      const errors = resolved.filter((item) => !item.valid).map((item) => {
        const messageKey = item.errorCode === 'PREORDER_INVALID_REQUEST'
          ? 'invalidRequest'
          : item.errorCode === 'PREORDER_MOQ_CONFLICT'
            ? 'resolvedMoqConflict'
            : item.errorCode === 'PREORDER_ITEM_AMBIGUOUS'
              ? 'itemAmbiguous'
              : 'itemNotFound'
        return t('warehouse.preorders.paste.resolvedError', {
          lineNumber: item.lineNumber,
          message: t(`warehouse.preorders.paste.${messageKey}`),
        })
      })
      setPasteErrors(errors)
      if (!errors.length) message.success(t('warehouse.preorders.paste.parsedCount', { count: resolved.length }))
    } catch {
      if (isCurrentModalRequest(pasteRequestGuardRef.current, requestToken)) {
        message.error(t('warehouse.preorders.paste.resolveFailed'))
      }
    } finally {
      if (isCurrentModalRequest(pasteRequestGuardRef.current, requestToken)) {
        setResolving(false)
      }
    }
  }

  const saveTemplate = async () => {
    const requestVersion = editorRequestGuardRef.current.version
    const values = await form.validateFields()
    if (editorRequestGuardRef.current.version !== requestVersion) return
    if (!canSavePreorderTemplate(items, pasteErrors)) {
      if (!pasteErrors.length) {
        setPasteErrors([t('warehouse.preorders.paste.validItemRequired')])
      }
      return
    }
    setSaving(true)
    try {
      const payload = {
        ...values,
        notes: values.notes?.trim() || undefined,
        expectedRevision: editing?.revision,
        items: items.map((item, index) => ({
          productCode: item.productCode!,
          minimumOrderQuantity: item.minimumOrderQuantity,
          sortOrder: index,
        })),
      }
      if (editing) await updatePreorderTemplate(editing.templateGuid, payload)
      else await createPreorderTemplate(payload)
      if (editorRequestGuardRef.current.version !== requestVersion) return
      message.success(editing ? t('warehouse.preorders.templateUpdated') : t('warehouse.preorders.templateCreated'))
      closeEditor()
      await loadTemplates()
    } catch {
      if (editorRequestGuardRef.current.version === requestVersion) {
        message.error(t('warehouse.preorders.templateSaveFailed'))
      }
    } finally {
      if (editorRequestGuardRef.current.version === requestVersion) {
        setSaving(false)
      }
    }
  }

  const openActivation = async (row: PreorderTemplateSummary) => {
    const requestToken = beginModalRequest(activationRequestGuardRef.current)
    setActivationTemplate(null)
    setActivationLoading(true)
    setActivating(false)
    activationForm.resetFields()
    setActivationOpen(true)
    try {
      const detail = await getPreorderTemplate(row.templateGuid, requestToken.signal)
      if (!isCurrentModalRequest(activationRequestGuardRef.current, requestToken)) return
      setActivationTemplate(detail)
      activationForm.setFieldsValue({
        range: [dayjs().add(5, 'minute'), dayjs().add(7, 'day')],
        estimatedArrivalDate: null,
        storeGuids: detail.stores.map((store) => store.storeGuid),
      })
    } catch {
      if (isCurrentModalRequest(activationRequestGuardRef.current, requestToken)) {
        setActivationOpen(false)
        setActivationTemplate(null)
        message.error(t('warehouse.preorders.templateDetailLoadFailed'))
      }
    } finally {
      if (isCurrentModalRequest(activationRequestGuardRef.current, requestToken)) {
        setActivationLoading(false)
      }
    }
  }

  const closeActivationModal = () => {
    invalidateModalRequest(activationRequestGuardRef.current)
    setActivationOpen(false)
    setActivationLoading(false)
    setActivating(false)
    setActivationTemplate(null)
    activationForm.resetFields()
  }

  const createActivation = async () => {
    if (!activationTemplate) return
    const requestVersion = activationRequestGuardRef.current.version
    const values = await activationForm.validateFields()
    if (activationRequestGuardRef.current.version !== requestVersion) return
    if (!values.range[1].isAfter(values.range[0])) {
      message.warning(t('warehouse.preorders.endAfterStart'))
      return
    }
    setActivating(true)
    try {
      await activatePreorderTemplate(activationTemplate.templateGuid, {
        expectedRevision: activationTemplate.revision,
        startAtUtc: values.range[0].toISOString(),
        endAtUtc: values.range[1].toISOString(),
        // DateOnly 必须直接发送日历日期，禁止经 UTC 转换造成前后偏移。
        estimatedArrivalDate: values.estimatedArrivalDate?.format('YYYY-MM-DD') ?? null,
        storeGuids: values.storeGuids,
      })
      if (activationRequestGuardRef.current.version !== requestVersion) return
      message.success(t('warehouse.preorders.activationCreated'))
      closeActivationModal()
      await loadTemplates()
    } catch {
      if (activationRequestGuardRef.current.version === requestVersion) {
        message.error(t('warehouse.preorders.activationFailedOverlap'))
      }
    } finally {
      if (activationRequestGuardRef.current.version === requestVersion) {
        setActivating(false)
      }
    }
  }

  const openHistory = async (row: PreorderTemplateSummary) => {
    const requestToken = beginModalRequest(historyRequestGuardRef.current)
    setHistoryTemplate(row)
    setActivations([])
    setHistoryLoading(true)
    setHistoryOpen(true)
    try {
      const next = await getTemplateActivations(row.templateGuid, requestToken.signal)
      if (!isCurrentModalRequest(historyRequestGuardRef.current, requestToken)) return
      setActivations(next)
    } catch {
      if (isCurrentModalRequest(historyRequestGuardRef.current, requestToken)) {
        message.error(t('warehouse.preorders.historyLoadFailed'))
      }
    } finally {
      if (isCurrentModalRequest(historyRequestGuardRef.current, requestToken)) {
        setHistoryLoading(false)
      }
    }
  }

  const closeHistory = () => {
    invalidateModalRequest(historyRequestGuardRef.current)
    setHistoryOpen(false)
    setHistoryLoading(false)
    setHistoryTemplate(null)
    setActivations([])
  }

  const columns: ColumnsType<PreorderTemplateSummary> = [
    { title: t('warehouse.preorders.templateName'), dataIndex: 'name', width: 260, render: (value, row) => <Space><Text strong>{value}</Text>{row.isEnabled ? <Tag color="green">{t('warehouse.preorders.enabled')}</Tag> : <Tag>{t('warehouse.preorders.disabled')}</Tag>}</Space> },
    { title: t('warehouse.preorders.revision'), dataIndex: 'revision', width: 80, align: 'center' },
    { title: t('warehouse.preorders.products'), dataIndex: 'itemCount', width: 90, align: 'right' },
    { title: t('warehouse.preorders.defaultStores'), dataIndex: 'storeCount', width: 100, align: 'right' },
    { title: t('warehouse.preorders.activationCount'), dataIndex: 'activationCount', width: 100, align: 'right' },
    {
      title: t('warehouse.preorders.actions'), key: 'actions', width: 300, fixed: 'right', render: (_, row) => (
        <Space wrap>
          <Button size="small" icon={<EditOutlined />} onClick={() => void openEdit(row)}>{t('warehouse.preorders.edit')}</Button>
          <Button size="small" icon={<HistoryOutlined />} onClick={() => void openHistory(row)}>{t('warehouse.preorders.activations')}</Button>
          <Button size="small" type="primary" icon={<RocketOutlined />} disabled={!row.isEnabled} onClick={() => void openActivation(row)}>{t('warehouse.preorders.activateNew')}</Button>
        </Space>
      ),
    },
  ]

  return (
    <ConfigProvider locale={antdLocale}>
      <PageContainer title={t('warehouse.preorders.title')} extra={<Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('warehouse.preorders.createTemplate')}</Button>}>
      <Card className="preorder-admin-card">
        <Table rowKey="templateGuid" columns={columns} dataSource={templates} loading={loading} scroll={{ x: 1000 }} pagination={false} />
      </Card>

      <Modal title={editing ? t('warehouse.preorders.editTemplateTitle', { revision: editing.revision }) : t('warehouse.preorders.createTemplateTitle')} open={editorOpen} width={1120} confirmLoading={saving} okButtonProps={{ disabled: editorLoading || resolving || !canSavePreorderTemplate(items, pasteErrors) }} onOk={() => void saveTemplate()} onCancel={closeEditor} okText={t('warehouse.preorders.saveTemplate')} destroyOnClose>
        <Card loading={editorLoading} bordered={false}>
          <Form form={form} layout="vertical" initialValues={{ isEnabled: true }}>
            <div className="preorder-template-grid">
              <Form.Item name="name" label={t('warehouse.preorders.templateName')} rules={[{ required: true, message: t('warehouse.preorders.templateNameRequired') }]}><Input maxLength={100} /></Form.Item>
              <Form.Item name="isEnabled" label={t('warehouse.preorders.allowActivation')} valuePropName="checked"><Switch checkedChildren={t('warehouse.preorders.enabled')} unCheckedChildren={t('warehouse.preorders.disabled')} /></Form.Item>
            </div>
            <Form.Item name="storeGuids" label={t('warehouse.preorders.defaultVisibleStores')} rules={[{ required: true, message: t('warehouse.preorders.selectOneStore') }]}><Select mode="multiple" showSearch optionFilterProp="label" options={storeOptions} maxTagCount="responsive" /></Form.Item>
            <Form.Item name="notes" label={t('warehouse.preorders.notes')}><Input.TextArea rows={2} maxLength={500} showCount /></Form.Item>
          </Form>
          <div className="preorder-paste-grid">
            <div>
              <Title level={5}>{t('warehouse.preorders.paste.title')}</Title>
              <Text type="secondary">{t('warehouse.preorders.paste.help')}</Text>
              <Input.TextArea
                value={pasteText}
                onChange={(event) => {
                  const nextState = applyPreorderPasteTextChange(
                    { text: pasteText, items, errors: pasteErrors },
                    event.target.value,
                  )
                  if (nextState.text === pasteText) return

                  // 粘贴内容变更即废弃旧解析，避免慢响应覆盖新内容或新模板。
                  invalidateModalRequest(pasteRequestGuardRef.current)
                  setResolving(false)
                  setPasteText(nextState.text)
                  setItems(nextState.items)
                  setPasteErrors(nextState.errors)
                }}
                rows={9}
                placeholder={t('warehouse.preorders.paste.placeholder')}
              />
              <Button type="primary" ghost loading={resolving} onClick={() => void resolvePaste()} style={{ marginTop: 8 }}>{t('warehouse.preorders.paste.parseAndPreview')}</Button>
              {pasteErrors.length ? <Alert style={{ marginTop: 12 }} type="error" showIcon message={t('warehouse.preorders.paste.resolveIssues')} description={pasteErrors.map((error) => <div key={error}>{error}</div>)} /> : null}
            </div>
            <Table<PreorderResolvedItem>
              size="small"
              rowKey={(row) => `${row.lineNumber}-${row.itemNumber}`}
              dataSource={items}
              pagination={false}
              scroll={{ y: 330 }}
              columns={[
                { title: t('warehouse.preorders.image'), dataIndex: 'productImage', width: 60, render: (src) => <Image src={src} width={36} height={36} style={{ objectFit: 'contain' }} fallback="/placeholder-product.svg" /> },
                { title: t('warehouse.preorders.itemNumber'), dataIndex: 'itemNumber', width: 130 },
                { title: t('warehouse.preorders.name'), dataIndex: 'productName', ellipsis: true },
                { title: t('warehouse.preorders.importPrice'), dataIndex: 'importPrice', width: 85, align: 'right', render: (value) => value === undefined ? '--' : `$${value.toFixed(2)}` },
                { title: t('warehouse.preorders.retailPrice'), dataIndex: 'retailPrice', width: 85, align: 'right', render: (value) => value === undefined ? '--' : `$${value.toFixed(2)}` },
                { title: 'MOQ', dataIndex: 'minimumOrderQuantity', width: 90, render: (value, _, index) => <InputNumber min={1} precision={0} value={value} onChange={(next) => setItems((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, minimumOrderQuantity: Number(next || 1) } : item))} /> },
                {
                  title: '',
                  width: 60,
                  render: (_, row) => <Button type="link" danger onClick={() => {
                    const nextState = removePreorderPasteItem(
                      { items, errors: pasteErrors },
                      row.lineNumber,
                      t('warehouse.preorders.paste.linePrefix', { lineNumber: row.lineNumber }),
                    )
                    setItems(nextState.items)
                    setPasteErrors(nextState.errors)
                  }}>{t('warehouse.preorders.remove')}</Button>,
                },
              ]}
            />
          </div>
        </Card>
      </Modal>

      <Modal title={t('warehouse.preorders.activateTitle', { name: activationTemplate?.name || '' })} open={activationOpen} onCancel={closeActivationModal} onOk={() => void createActivation()} confirmLoading={activating} okButtonProps={{ disabled: activationLoading || !activationTemplate }} okText={t('warehouse.preorders.confirmActivate')}>
        <Spin spinning={activationLoading}>
          <Alert type="info" showIcon message={t('warehouse.preorders.snapshotNotice')} style={{ marginBottom: 16 }} />
          <Form form={activationForm} layout="vertical">
            <Form.Item name="range" label={t('warehouse.preorders.effectiveTime')} rules={[{ required: true }]}><RangePicker showTime style={{ width: '100%' }} disabledDate={(date) => date.endOf('day').isBefore(dayjs())} /></Form.Item>
            <Form.Item name="estimatedArrivalDate" label={t('warehouse.preorders.estimatedArrivalDate')}><DatePicker allowClear format="YYYY-MM-DD" style={{ width: '100%' }} /></Form.Item>
            <Form.Item name="storeGuids" label={t('warehouse.preorders.targetStores')} rules={[{ required: true, message: t('warehouse.preorders.selectOneStore') }]}><Select mode="multiple" showSearch optionFilterProp="label" options={storeOptions} maxTagCount="responsive" /></Form.Item>
          </Form>
        </Spin>
      </Modal>

      <Modal title={t('warehouse.preorders.historyTitle', { name: historyTemplate?.name || '' })} open={historyOpen} footer={null} width={920} onCancel={closeHistory}>
        <Table loading={historyLoading} rowKey="activationGuid" dataSource={activations} pagination={false} scroll={{ x: 900 }} columns={[
          { title: t('warehouse.preorders.periodNumber'), dataIndex: 'sequenceNumber', width: 80, render: (value) => t('warehouse.preorders.period', { sequence: value }) },
          { title: t('warehouse.preorders.activationNumber'), dataIndex: 'activationNumber', width: 150 },
          { title: t('warehouse.preorders.status'), dataIndex: 'status', width: 100, render: (status) => activationStatusTag(status, t(`warehouse.preorders.activationStatus.${status}`)) },
          { title: t('warehouse.preorders.estimatedArrivalDate'), dataIndex: 'estimatedArrivalDate', width: 140, render: (value) => getPreorderDateDisplay(value) ?? '--' },
          { title: t('warehouse.preorders.effectiveTime'), width: 260, render: (_, row) => `${dateTimeFormatter.format(new Date(row.startAtUtc))} — ${dateTimeFormatter.format(new Date(row.endAtUtc))}` },
          { title: t('warehouse.preorders.progress'), width: 150, render: (_, row) => <Text><CheckCircleOutlined /> {row.targetStoreCount - row.pendingCount}/{row.targetStoreCount}</Text> },
          { title: '', width: 80, render: (_, row) => <Button type="link" onClick={() => navigate(`/warehouse/preorders/activations/${row.activationGuid}`)}>{t('warehouse.preorders.view')}</Button> },
        ]} />
      </Modal>
      </PageContainer>
    </ConfigProvider>
  )
}
