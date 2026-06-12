import { PlusOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import {
  createStrategy,
  deleteStrategy,
  evaluatePricing,
  getStrategyById,
  getStrategyGrid,
  updateStrategy,
} from '../../../services/pricingStrategyService'
import { getActiveStores } from '../../../services/storeService'
import type {
  CreatePricingStrategyDto,
  PricingStrategyDetailDto,
  PricingStrategyListDto,
  PricingStrategyRuleDto,
  PricingStrategyTargetDto,
  UpdatePricingStrategyDto,
} from '../../../types/pricingStrategy'

type DataType = PricingStrategyListDto & { key: string }

export default function PricingStrategiesPage() {
  const { t } = useTranslation()
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<DataType[]>([])
  const [total, setTotal] = useState(0)
  const [pageSize, setPageSize] = useState(50)
  const [page, setPage] = useState(1)
  const [sortField, setSortField] = useState<string | undefined>()
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend' | undefined>()
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editorForm] = Form.useForm()
  const inFlightRef = useRef(false)

  const storeNameMap = useMemo(() => new Map(storeOptions.map((o) => [o.value, o.label])), [storeOptions])
  const supplierNameMap = useMemo(() => new Map(supplierOptions.map((o) => [o.value, o.label])), [supplierOptions])

  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([])
  const [detailCache, setDetailCache] = useState<Record<string, PricingStrategyDetailDto>>({})
  const [detailLoading, setDetailLoading] = useState<Record<string, boolean>>({})

  const loadDetail = useCallback(async (id: string) => {
    if (detailCache[id]) return
    setDetailLoading((prev) => ({ ...prev, [id]: true }))
    try {
      const dto = await getStrategyById(id)
      setDetailCache((prev) => ({ ...prev, [id]: dto }))
    } catch {
      message.error(t('posAdmin.pricing.loadDetailFailed', '加载策略详情失败'))
    } finally {
      setDetailLoading((prev) => ({ ...prev, [id]: false }))
    }
  }, [detailCache])

  const handleExpand = useCallback((expanded: boolean, record: DataType) => {
    const id = String(record.id)
    if (expanded) {
      setExpandedRowKeys((prev) => [...prev, record.key])
      loadDetail(id)
    } else {
      setExpandedRowKeys((prev) => prev.filter((k) => k !== record.key))
    }
  }, [loadDetail])

  const algorithmLabel = (v: string) => (v === 'Exponential' ? t('posAdmin.pricing.exponential', '指数') : v === 'Step' ? t('posAdmin.pricing.step', '阶梯') : t('posAdmin.pricing.linear', '线性'))
  const algorithmColor = (v: string) => (v === 'Exponential' ? 'orange' : v === 'Step' ? 'purple' : 'blue')

  const expandedRowRender = (record: DataType) => {
    const id = String(record.id)
    const dto = detailCache[id]
    if (detailLoading[id] || !dto) {
      return (
        <div style={{ textAlign: 'center', padding: 24 }}>
          <Spin />
        </div>
      )
    }

    const ruleColumns: ColumnsType<PricingStrategyRuleDto> = [
      {
        title: t('posAdmin.pricing.purchasePriceRange', '进货价区间'), key: 'priceRange', width: 240,
        render: (_, r) => (
          <Space>
            <Tag color="blue">{r.minPrice?.toFixed(2)}</Tag>
            <span style={{ color: '#999' }}>~</span>
            <Tag color="red">{r.maxPrice?.toFixed(2)}</Tag>
          </Space>
        ),
      },
      {
        title: t('posAdmin.pricing.rateRange', '上浮率区间'), key: 'rateRange', width: 240,
        render: (_, r) => (
          <Space>
            <Tag color="green">{r.startRate?.toFixed(4)}</Tag>
            <span style={{ color: '#999' }}>~</span>
            <Tag color="volcano">{r.endRate?.toFixed(4)}</Tag>
          </Space>
        ),
      },
      {
        title: t('posAdmin.pricing.algorithm', '算法'), dataIndex: 'algorithm', key: 'algorithm', width: 100,
        render: (v: string) => <Tag color={algorithmColor(v)}>{algorithmLabel(v)}</Tag>,
      },
    ]

    const targetColumns: ColumnsType<PricingStrategyTargetDto> = [
      {
        title: t('posAdmin.pricing.targetType', '目标类型'), dataIndex: 'targetType', key: 'targetType', width: 120,
        render: (v: string) => {
          const label = v === 'Store' ? t('posAdmin.pricing.storeLevel', '分店') : v === 'Supplier' ? t('posAdmin.pricing.supplierLevel', '供应商') : t('posAdmin.pricing.globalLevel', '全局')
          const color = v === 'Store' ? 'green' : v === 'Supplier' ? 'blue' : 'purple'
          return <Tag color={color}>{label}</Tag>
        },
      },
      {
        title: t('posAdmin.pricing.targetCode', '目标代码'), dataIndex: 'targetCode', key: 'targetCode', width: 200,
        render: (v: string) => {
          if (!v) return <Tag>{t('posAdmin.pricing.all', '全部')}</Tag>
          const name = storeNameMap.get(v) ?? supplierNameMap.get(v)
          return <Tag color="cyan">{name ? `${v}（${name}）` : v}</Tag>
        },
      },
    ]

    return (
      <div style={{ padding: '8px 0' }}>
        <Card title={t('posAdmin.pricing.priceRules', '定价规则（{{count}} 条）', { count: dto.details.length })} size="small" style={{ marginBottom: 12 }}>
          <Table
            rowKey={(r, i) => `${r.id ?? i}`}
            size="small"
            columns={ruleColumns}
            dataSource={dto.details}
            pagination={false}
          />
        </Card>
        <Card title={t('posAdmin.pricing.targetScope', '目标范围（{{count}} 条）', { count: dto.targets.length })} size="small">
          <Table
            rowKey={(r, i) => `${r.id ?? i}`}
            size="small"
            columns={targetColumns}
            dataSource={dto.targets}
            pagination={false}
          />
        </Card>
      </div>
    )
  }

  const [testForm] = Form.useForm()
  const [testResult, setTestResult] = useState<{ rate?: number; retailPrice?: number } | null>(null)

  const loadData = async () => {
    if (inFlightRef.current) return
    inFlightRef.current = true
    try {
      setLoading(true)
      const values = form.getFieldsValue()
      const sortModel: Record<string, string> = {}
      if (sortField && sortOrder) {
        sortModel[sortField] = sortOrder === 'ascend' ? 'asc' : 'desc'
      }
      const result = await getStrategyGrid({
        StartRow: (page - 1) * pageSize,
        EndRow: page * pageSize - 1,
        PageSize: pageSize,
        GlobalSearch: values.keyword || undefined,
        SortModel: Object.keys(sortModel).length && sortField ? [{ ColId: sortField, Sort: sortModel[sortField] }] : undefined,
      } as Record<string, unknown>)
      const items = result?.items ?? []
      const totalVal = result?.total ?? 0
      setTotal(totalVal)
      setData(items.map((it) => ({ ...it, key: String(it.id) })))
    } catch {
      message.error(t('message.loadFailed', '加载失败'))
    } finally {
      setLoading(false)
      inFlightRef.current = false
    }
  }

  const openCreate = () => {
    setEditingId(null)
    editorForm.resetFields()
    editorForm.setFieldsValue({
      name: '',
      level: 'Global',
      priority: 0,
      isEnabled: true,
      targetsStores: [],
      targetsSuppliers: [],
      details: [{ minPrice: 0, maxPrice: 0, startRate: 1, endRate: 1, algorithm: 'Linear' }],
    })
    setEditorOpen(true)
  }

  const openEdit = async (id: string) => {
    try {
      const dto = await getStrategyById(id)
      if (!dto) {
        message.error(t('posAdmin.pricing.notFound', '未找到策略'))
        return
      }
      setEditingId(id)
      const stores = dto.targets.filter((t) => t.targetType === 'Store').map((t) => t.targetCode!).filter(Boolean)
      const suppliers = dto.targets.filter((t) => t.targetType === 'Supplier').map((t) => t.targetCode!).filter(Boolean)
      editorForm.setFieldsValue({
        name: dto.name,
        level: dto.level,
        priority: dto.priority,
        isEnabled: dto.isEnabled,
        targetsStores: stores,
        targetsSuppliers: suppliers,
        details: dto.details.map((r) => ({
          id: r.id,
          minPrice: r.minPrice,
          maxPrice: r.maxPrice,
          startRate: r.startRate,
          endRate: r.endRate,
          algorithm: r.algorithm,
        })),
      })
      setEditorOpen(true)
    } catch {
      message.error(t('message.loadFailed', '加载失败'))
    }
  }

  const saveEditor = async () => {
    try {
      const v = await editorForm.validateFields()
      if (v.level === 'Store' && (!v.targetsStores || v.targetsStores.length === 0)) {
        message.warning(t('posAdmin.pricing.storeLevelRequired', '分店级别策略必须选择至少一个分店'))
        return
      }
      if (v.level === 'Supplier' && (!v.targetsSuppliers || v.targetsSuppliers.length === 0)) {
        message.warning(t('posAdmin.pricing.supplierLevelRequired', '供应商级别策略必须选择至少一个供应商'))
        return
      }
      const rules: PricingStrategyRuleDto[] = (v.details || []).map((r: any) => ({
        id: r.id,
        minPrice: Number(r.minPrice),
        maxPrice: Number(r.maxPrice),
        startRate: Number(r.startRate),
        endRate: Number(r.endRate),
        algorithm: r.algorithm,
      }))
      const targets: PricingStrategyTargetDto[] = [
        ...(v.targetsStores || []).map((code: string) => ({ targetType: 'Store' as const, targetCode: code })),
        ...(v.targetsSuppliers || []).map((code: string) => ({ targetType: 'Supplier' as const, targetCode: code })),
      ]
      const payload = { name: v.name, level: v.level, priority: Number(v.priority || 0), isEnabled: !!v.isEnabled, details: rules, targets }
      if (editingId === null) {
        await createStrategy(payload as CreatePricingStrategyDto)
      } else {
        await updateStrategy(editingId, payload as UpdatePricingStrategyDto)
      }
      message.success(t('message.saveSuccess', '保存成功'))
      setEditorOpen(false)
      await loadData()
    } catch {
      message.error(t('message.saveFailed', '保存失败'))
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await deleteStrategy(id)
      message.success(t('message.deleteSuccess', '删除成功'))
      await loadData()
    } catch {
      message.error(t('message.deleteFailed', '删除失败'))
    }
  }

  const columns: ColumnsType<DataType> = [
    { title: t('posAdmin.pricing.strategyName', '名称'), dataIndex: 'name', key: 'name', sorter: true },
    {
      title: t('posAdmin.pricing.level', '等级'), dataIndex: 'level', key: 'level', width: 100,
      render: (v: string) => {
        const label = v === 'Supplier' ? t('posAdmin.pricing.supplierLevel', '供应商') : v === 'Store' ? t('posAdmin.pricing.storeLevel', '分店') : t('posAdmin.pricing.globalLevel', '全局')
        const color = v === 'Supplier' ? 'blue' : v === 'Store' ? 'green' : 'purple'
        return <Tag color={color}>{label}</Tag>
      },
    },
    {
      title: t('posAdmin.pricing.isEnabled', '启用'), dataIndex: 'isEnabled', key: 'isEnabled', sorter: true, width: 100,
      render: (v: boolean) => <Tag color={v ? 'success' : 'error'}>{v ? t('common.enabled', '启用') : t('common.disabled', '禁用')}</Tag>,
    },
    { title: t('posAdmin.pricing.targetStores', '分店'), dataIndex: 'storeCodes', key: 'storeCodes', render: (vals: string[]) => <Space wrap>{(vals || []).map((c) => <Tag key={c} color="cyan">{storeNameMap.get(c) ?? c}</Tag>)}</Space> },
    { title: t('posAdmin.pricing.targetSuppliers', '供应商'), dataIndex: 'supplierCodes', key: 'supplierCodes', render: (vals: string[]) => <Space wrap>{(vals || []).map((c) => <Tag key={c} color="geekblue">{supplierNameMap.get(c) ?? c}</Tag>)}</Space> },
    {
      title: t('posAdmin.pricing.ruleCount', '规则数'), dataIndex: 'detailsCount', key: 'detailsCount', width: 100,
      render: (v: number) => <Tag color={v > 0 ? 'processing' : 'default'}>{v}</Tag>,
    },
    {
      title: t('posAdmin.pricing.targetCount', '目标数'), dataIndex: 'targetsCount', key: 'targetsCount', width: 100,
      render: (v: number) => <Tag color={v > 0 ? 'warning' : 'default'}>{v}</Tag>,
    },
    {
      title: t('column.actions', '操作'), key: 'actions', width: 200,
      render: (_, record) => (
        <Space>
          <Button type="link" onClick={() => openEdit(String(record.id))}>{t('common.edit', '编辑')}</Button>
          <Popconfirm title={t('posAdmin.pricing.confirmDeleteStrategy', '确认删除该策略？')} description={t('posAdmin.pricing.deleteIrreversible', '删除后无法恢复')} okText={t('common.delete', '删除')} cancelText={t('common.cancel', '取消')} okButtonProps={{ danger: true }} onConfirm={() => handleDelete(String(record.id))}>
            <Button type="link" danger>{t('common.delete', '删除')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  const onTableChange = (pagination: any, _filters: any, sorter: any) => {
    if (sorter?.field) {
      setSortField(String(sorter.field))
      setSortOrder(sorter.order)
    } else {
      setSortField(undefined)
      setSortOrder(undefined)
    }
    setPage(pagination.current)
    setPageSize(pagination.pageSize)
  }

  useEffect(() => {
    ;(async () => {
      try {
        const storeOpts = await getActiveStores()
        setStoreOptions(storeOpts)
      } catch { /* ignore */ }
      try {
        const suppliers = await getActiveLocalSuppliers()
        setSupplierOptions(suppliers.map((s) => ({ label: s.name || s.localSupplierCode, value: s.localSupplierCode })))
      } catch { /* ignore */ }
      await loadData()
    })()
  }, [])

  useEffect(() => { loadData() }, [page, pageSize, sortField, sortOrder])

  return (
    <Card title={t('posAdmin.pricing.title', '自动价格策略管理')} extra={<Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{t('posAdmin.pricing.createStrategy', '新建策略')}</Button>}>
      <Form form={form} layout="inline" onFinish={loadData} style={{ marginBottom: 16 }}>
        <Form.Item name="storeCode" label={t('posAdmin.pricing.targetStores', '分店')}><Select allowClear showSearch optionFilterProp="label" options={storeOptions} style={{ width: 240 }} /></Form.Item>
        <Form.Item name="supplierCode" label={t('posAdmin.pricing.targetSuppliers', '供应商')}><Select allowClear showSearch optionFilterProp="label" options={supplierOptions} style={{ width: 240 }} /></Form.Item>
        <Form.Item name="keyword" label={t('posAdmin.pricing.keyword', '关键词')}><Input allowClear placeholder={t('posAdmin.pricing.searchName', '搜索名称')} style={{ width: 240 }} /></Form.Item>
        <Form.Item><Space><Button type="primary" htmlType="submit">{t('common.query', '查询')}</Button><Button onClick={() => { form.resetFields(); loadData() }}>{t('common.reset', '重置')}</Button></Space></Form.Item>
      </Form>

      <Card title={t('posAdmin.pricing.strategyTest', '策略测试')} style={{ marginBottom: 16 }}>
        <Form form={testForm} layout="inline" onFinish={async (v) => {
          try {
            const result = await evaluatePricing({ purchasePrice: Number(v.purchasePrice), storeCode: v.storeCode, supplierCode: v.supplierCode })
            setTestResult({ rate: result.retailPrice ? result.retailPrice / Number(v.purchasePrice) : undefined, retailPrice: result.retailPrice })
            message.success(t('posAdmin.pricing.calcComplete', '计算完成'))
          } catch { message.error(t('posAdmin.pricing.calcFailed', '计算失败')) }
        }}>
          <Form.Item name="storeCode" label={t('posAdmin.pricing.targetStores', '分店')}><Select allowClear showSearch optionFilterProp="label" options={storeOptions} style={{ width: 240 }} /></Form.Item>
          <Form.Item name="supplierCode" label={t('posAdmin.pricing.targetSuppliers', '供应商')}><Select allowClear showSearch optionFilterProp="label" options={supplierOptions} style={{ width: 240 }} /></Form.Item>
          <Form.Item name="purchasePrice" label={t('posAdmin.pricing.purchasePrice', '进货价')} rules={[{ required: true }]}><InputNumber min={0} precision={2} style={{ width: 160 }} /></Form.Item>
          <Form.Item><Button type="primary" htmlType="submit">{t('posAdmin.pricing.calculate', '计算')}</Button></Form.Item>
        </Form>
        {testResult && (
          <Space style={{ marginTop: 12 }}>
            <Tag color="blue">{t('posAdmin.pricing.rateLabel', '上浮率：{{rate}}', { rate: typeof testResult.rate === 'number' ? testResult.rate.toFixed(4) : '-' })}</Tag>
            <Tag color="green">{t('posAdmin.pricing.retailPriceLabel', '零售价：{{price}}', { price: typeof testResult.retailPrice === 'number' ? testResult.retailPrice.toFixed(2) : '-' })}</Tag>
          </Space>
        )}
      </Card>

      <Table
        rowKey="key"
        loading={loading}
        dataSource={data}
        columns={columns}
        pagination={{ total, current: page, pageSize, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100', '200'] }}
        onChange={onTableChange}
        expandable={{
          expandedRowKeys,
          onExpandedRowsChange: (keys) => setExpandedRowKeys([...keys]),
          expandedRowRender,
          onExpand: handleExpand,
        }}
      />

      <Modal open={editorOpen} title={editingId === null ? t('posAdmin.pricing.newStrategy', '新建策略') : t('posAdmin.pricing.editStrategy', '编辑策略')} onCancel={() => setEditorOpen(false)} onOk={saveEditor} width={900} forceRender>
        <Form form={editorForm} layout="vertical">
          <Space style={{ width: '100%' }} wrap>
            <Form.Item name="name" label={t('posAdmin.pricing.strategyName', '名称')} rules={[{ required: true }]} style={{ width: 300 }}><Input /></Form.Item>
            <Form.Item name="level" label={t('posAdmin.pricing.level', '级别')} rules={[{ required: true }]} style={{ width: 160 }}>
              <Select options={[{ value: 'Supplier', label: t('posAdmin.pricing.supplierLevel', '供应商') }, { value: 'Store', label: t('posAdmin.pricing.storeLevel', '分店') }, { value: 'Global', label: t('posAdmin.pricing.globalLevel', '全局') }]} />
            </Form.Item>
            <Form.Item name="priority" label={t('posAdmin.pricing.priority', '优先级')} style={{ width: 180 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
            <Form.Item name="isEnabled" label={t('posAdmin.pricing.isEnabled', '启用')} valuePropName="checked" style={{ width: 120 }}><Switch /></Form.Item>
          </Space>
          <Space style={{ width: '100%' }} wrap>
            <Form.Item name="targetsStores" label={t('posAdmin.pricing.targetStores', '分店')} style={{ width: 400 }}><Select mode="multiple" options={storeOptions} showSearch optionFilterProp="label" allowClear /></Form.Item>
            <Form.Item name="targetsSuppliers" label={t('posAdmin.pricing.targetSuppliers', '供应商')} style={{ width: 400 }}><Select mode="multiple" options={supplierOptions} showSearch optionFilterProp="label" allowClear /></Form.Item>
          </Space>
          <Form.List name="details">
            {(fields, { add, remove }) => (
              <>
                {fields.map(({ key, name, ...restField }) => (
                  <div key={key} style={{ marginBottom: 16, padding: 12, background: '#fafafa', borderRadius: 8, border: '1px solid #f0f0f0' }}>
                    <Space style={{ width: '100%' }} wrap align="start">
                      <Form.Item {...restField} name={[name, 'minPrice']} rules={[{ required: true }]} label={t('posAdmin.pricing.minPrice', '进货价下限')} style={{ marginBottom: 8 }}><InputNumber min={0} precision={2} style={{ width: 120 }} /></Form.Item>
                      <Form.Item {...restField} name={[name, 'maxPrice']} rules={[{ required: true }]} label={t('posAdmin.pricing.maxPrice', '进货价上限')} style={{ marginBottom: 8 }}><InputNumber min={0} precision={2} style={{ width: 120 }} /></Form.Item>
                      <Form.Item {...restField} name={[name, 'startRate']} rules={[{ required: true }]} label={t('posAdmin.pricing.startRate', '起始上浮率')} style={{ marginBottom: 8 }}><InputNumber min={0} precision={4} style={{ width: 100 }} /></Form.Item>
                      <Form.Item {...restField} name={[name, 'endRate']} rules={[{ required: true }]} label={t('posAdmin.pricing.endRate', '结束上浮率')} style={{ marginBottom: 8 }}><InputNumber min={0} precision={4} style={{ width: 100 }} /></Form.Item>
                      <Form.Item {...restField} name={[name, 'algorithm']} rules={[{ required: true }]} label={t('posAdmin.pricing.algorithm', '算法')} style={{ marginBottom: 8 }}>
                        <Select style={{ width: 120 }} options={[{ value: 'Linear', label: t('posAdmin.pricing.linear', '线性') }, { value: 'Exponential', label: t('posAdmin.pricing.exponential', '指数') }, { value: 'Step', label: t('posAdmin.pricing.step', '阶梯') }]} />
                      </Form.Item>
                      <Button type="link" danger onClick={() => remove(name)} style={{ marginTop: 30 }}>{t('posAdmin.pricing.deleteRule', '删除')}</Button>
                    </Space>
                  </div>
                ))}
                <Form.Item><Button type="dashed" onClick={() => add({ minPrice: 10, maxPrice: 100, startRate: 1.5, endRate: 2.5, algorithm: 'Linear' })}>{t('posAdmin.pricing.addRule', '添加规则')}</Button></Form.Item>
              </>
            )}
          </Form.List>
        </Form>
      </Modal>
    </Card>
  )
}
