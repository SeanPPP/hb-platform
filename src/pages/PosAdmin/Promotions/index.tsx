import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import { getPosProductById, type PosProductDto } from '../../../services/posProductService'
import {
  createPromotion,
  deletePromotion,
  enablePromotion,
  getPromotionById,
  getPromotionGrid,
  updatePromotion,
} from '../../../services/promotionService'
import { getActiveStores } from '../../../services/storeService'
import type {
  CreatePromotionDto,
  PromotionDetailDto,
  PromotionListDto,
  PromotionProductItemDto,
  PromotionStoreItemDto,
  UpdatePromotionDto,
} from '../../../types/promotion'
import {
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  InputNumber,
  message,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useEffect, useRef, useState } from 'react'
import ProductPicker from './ProductPicker'

type DataType = PromotionListDto & { key: string }

export default function PromotionsPage() {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<DataType[]>([])
  const [total, setTotal] = useState(0)
  const [pageSize, setPageSize] = useState(50)
  const [page, setPage] = useState(1)
  const [sortField, setSortField] = useState<string | undefined>()
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend' | undefined>()
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editorForm] = Form.useForm()
  const inFlightRef = useRef(false)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [productInfoMap, setProductInfoMap] = useState<Record<string, PosProductDto>>({})
  const [supplierNameMap, setSupplierNameMap] = useState<Record<string, string>>({})

  const loadData = async () => {
    if (inFlightRef.current) return
    inFlightRef.current = true
    try {
      setLoading(true)
      const values = form.getFieldsValue()
      const sortModel: Record<string, string>[] = []
      if (sortField && sortOrder) {
        sortModel.push({ ColId: sortField, Sort: sortOrder === 'ascend' ? 'asc' : 'desc' })
      }
      const result = await getPromotionGrid({
        StartRow: (page - 1) * pageSize,
        EndRow: page * pageSize - 1,
        PageSize: pageSize,
        GlobalSearch: values.keyword || undefined,
        SortModel: sortModel.length ? sortModel : undefined,
      } as Record<string, unknown>)
      const items = result?.items ?? []
      setTotal(result?.total ?? 0)
      setData(items.map((it) => ({ ...it, key: String(it.id) })))
    } catch {
      message.error('加载失败')
    } finally {
      setLoading(false)
      inFlightRef.current = false
    }
  }

  const loadSupplierNames = async () => {
    try {
      const suppliers = await getActiveLocalSuppliers()
      const map: Record<string, string> = {}
      suppliers.forEach((s) => {
        if (s.localSupplierCode) map[s.localSupplierCode] = s.name || s.localSupplierCode
      })
      setSupplierNameMap(map)
    } catch { /* ignore */ }
  }

  const openCreate = () => {
    setEditingId(null)
    editorForm.resetFields()
    editorForm.setFieldsValue({
      name: '', description: '', isEnabled: true, isExclusive: true, priority: 0,
      effectiveStart: null, effectiveEnd: null, applyQuantity: 2, fixedPrice: 0,
      maxApplicationsPerOrder: undefined, stores: [], products: [],
    })
    loadSupplierNames()
    setEditorOpen(true)
  }

  const openEdit = async (id: string) => {
    try {
      const dto: PromotionDetailDto = await getPromotionById(id)
      if (!dto) { message.error('未找到促销'); return }
      setEditingId(id)
      editorForm.setFieldsValue({
        name: dto.name, description: dto.description, isEnabled: dto.isEnabled,
        isExclusive: dto.isExclusive, priority: dto.priority,
        effectiveStart: dto.effectiveStart ? dayjs(dto.effectiveStart) : null,
        effectiveEnd: dto.effectiveEnd ? dayjs(dto.effectiveEnd) : null,
        applyQuantity: dto.applyQuantity, fixedPrice: dto.fixedPrice,
        maxApplicationsPerOrder: dto.maxApplicationsPerOrder ?? undefined,
        stores: dto.stores.map((s) => s.storeCode),
        products: dto.products.map((p) => ({ id: p.id, productCode: p.productCode, unitWeight: p.unitWeight ?? 1 })),
      })
      loadSupplierNames()
      const codes = (dto.products || []).map((p) => p.productCode).filter(Boolean)
      await Promise.all(codes.map(async (code) => {
        try {
          const info = await getPosProductById(code)
          if (info?.productCode) setProductInfoMap((prev) => ({ ...prev, [info.productCode]: info }))
        } catch { /* ignore */ }
      }))
      setEditorOpen(true)
    } catch {
      message.error('加载失败')
    }
  }

  const saveEditor = async () => {
    try {
      const v = await editorForm.validateFields()
      const cleanedProducts: PromotionProductItemDto[] = (v.products || [])
        .filter((p: any) => !!p.productCode)
        .map((p: any) => ({ id: p.id, productCode: String(p.productCode), unitWeight: Number(p.unitWeight || 1) }))
      if (!cleanedProducts.length) { message.error('请至少添加一个商品'); return }
      const payload = {
        name: v.name, description: v.description || undefined, isEnabled: !!v.isEnabled,
        isExclusive: !!v.isExclusive, priority: Number(v.priority || 0),
        effectiveStart: v.effectiveStart?.toISOString(), effectiveEnd: v.effectiveEnd?.toISOString(),
        applyQuantity: Number(v.applyQuantity), fixedPrice: Number(v.fixedPrice),
        maxApplicationsPerOrder: v.maxApplicationsPerOrder != null ? Number(v.maxApplicationsPerOrder) : undefined,
        products: cleanedProducts,
        stores: (v.stores || []).map((code: string) => ({ storeCode: code })) as PromotionStoreItemDto[],
      }
      if (editingId === null) {
        await createPromotion(payload as CreatePromotionDto)
      } else {
        await updatePromotion(editingId, payload as UpdatePromotionDto)
      }
      message.success('保存成功')
      setEditorOpen(false)
      await loadData()
    } catch {
      message.error('保存失败')
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await deletePromotion(id)
      message.success('删除成功')
      await loadData()
    } catch { message.error('删除失败') }
  }

  const toggleEnable = async (record: DataType, enable: boolean) => {
    try {
      await enablePromotion(String(record.id), enable)
      message.success('已更新启用状态')
      await loadData()
    } catch { message.error('更新失败') }
  }

  const columns: ColumnsType<DataType> = [
    { title: '名称', dataIndex: 'name', key: 'name', sorter: true },
    { title: '启用', dataIndex: 'isEnabled', key: 'isEnabled', sorter: true, width: 100, render: (_, record) => <Switch checked={record.isEnabled} onChange={(val) => toggleEnable(record, val)} /> },
    { title: '排他', dataIndex: 'isExclusive', key: 'isExclusive', width: 100, render: (v: boolean) => <Tag color={v ? 'red' : 'default'}>{v ? '是' : '否'}</Tag> },
    { title: '优先级', dataIndex: 'priority', key: 'priority', sorter: true, width: 100 },
    { title: '开始', dataIndex: 'effectiveStart', key: 'effectiveStart', sorter: true, render: (v: string) => (v ? new Date(v).toLocaleString() : '-') },
    { title: '结束', dataIndex: 'effectiveEnd', key: 'effectiveEnd', sorter: true, render: (v: string) => (v ? new Date(v).toLocaleString() : '-') },
    { title: '门槛件数', dataIndex: 'applyQuantity', key: 'applyQuantity', width: 110 },
    { title: '固定总价', dataIndex: 'fixedPrice', key: 'fixedPrice', width: 110, render: (v: number) => (v ?? 0).toFixed(2) },
    { title: '商品数', dataIndex: 'productsCount', key: 'productsCount', width: 100 },
    { title: '分店数', dataIndex: 'storesCount', key: 'storesCount', width: 100 },
    {
      title: '操作', key: 'actions', width: 200,
      render: (_, record) => (
        <Space>
          <Button type="link" onClick={() => openEdit(String(record.id))}>编辑</Button>
          <Popconfirm title="确认删除该促销？" description="删除后无法恢复" okText="删除" cancelText="取消" okButtonProps={{ danger: true }} onConfirm={() => handleDelete(String(record.id))}>
            <Button type="link" danger>删除</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  const onTableChange = (pagination: any, _filters: any, sorter: any) => {
    if (sorter?.field) { setSortField(String(sorter.field)); setSortOrder(sorter.order) }
    else { setSortField(undefined); setSortOrder(undefined) }
    setPage(pagination.current)
    setPageSize(pagination.pageSize)
  }

  useEffect(() => {
    ;(async () => {
      try { setStoreOptions(await getActiveStores()) } catch { /* ignore */ }
      await loadData()
    })()
  }, [])

  useEffect(() => { loadData() }, [page, pageSize, sortField, sortOrder])

  return (
    <Card title="促销管理（满减/固定组合价）" extra={<Button type="primary" onClick={openCreate}>新建促销</Button>}>
      <Form form={form} layout="inline" onFinish={loadData} style={{ marginBottom: 16 }}>
        <Form.Item name="storeCode" label="分店"><Select allowClear showSearch optionFilterProp="label" options={storeOptions} style={{ width: 240 }} /></Form.Item>
        <Form.Item name="keyword" label="关键词"><Input allowClear placeholder="搜索名称" style={{ width: 240 }} /></Form.Item>
        <Form.Item><Space><Button type="primary" htmlType="submit">查询</Button><Button onClick={() => { form.resetFields(); loadData() }}>重置</Button></Space></Form.Item>
      </Form>

      <Table rowKey="key" loading={loading} dataSource={data} columns={columns} pagination={{ total, current: page, pageSize, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100', '200'] }} onChange={onTableChange} />

      <Modal open={editorOpen} title={editingId === null ? '新建促销' : '编辑促销'} onCancel={() => setEditorOpen(false)} onOk={saveEditor} width={900} forceRender>
        <Form form={editorForm} layout="vertical">
          <Space style={{ width: '100%' }} wrap>
            <Form.Item name="name" label="名称" rules={[{ required: true }]} style={{ width: 300 }}><Input /></Form.Item>
            <Form.Item name="priority" label="优先级" style={{ width: 180 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
            <Form.Item name="isExclusive" label="排他" valuePropName="checked" style={{ width: 120 }}><Switch /></Form.Item>
            <Form.Item name="isEnabled" label="启用" valuePropName="checked" style={{ width: 120 }}><Switch /></Form.Item>
          </Space>
          <Form.Item name="description" label="说明"><Input.TextArea rows={2} /></Form.Item>
          <Space style={{ width: '100%' }} wrap>
            <Form.Item name="effectiveStart" label="开始时间" rules={[{ required: true }]}><DatePicker showTime style={{ width: 240 }} /></Form.Item>
            <Form.Item name="effectiveEnd" label="结束时间" rules={[{ required: true }]}><DatePicker showTime style={{ width: 240 }} /></Form.Item>
          </Space>
          <Space style={{ width: '100%' }} wrap>
            <Form.Item name="applyQuantity" label="门槛件数" rules={[{ required: true, type: 'number', min: 2 }]}><InputNumber min={2} style={{ width: 160 }} /></Form.Item>
            <Form.Item name="fixedPrice" label="固定总价" rules={[{ required: true, type: 'number', min: 0 }]}><InputNumber min={0} precision={2} style={{ width: 160 }} /></Form.Item>
            <Form.Item name="maxApplicationsPerOrder" label="每单最多次数"><InputNumber min={1} style={{ width: 160 }} /></Form.Item>
          </Space>
          <Form.Item label="适用分店">
            <Space wrap>
              <Form.Item name="stores" noStyle><Select mode="multiple" options={storeOptions} showSearch optionFilterProp="label" allowClear style={{ minWidth: 360 }} /></Form.Item>
              <Button onClick={() => editorForm.setFieldsValue({ stores: storeOptions.map((x) => x.value) })}>全选</Button>
              <Button onClick={() => editorForm.setFieldsValue({ stores: [] })}>清空</Button>
            </Space>
          </Form.Item>
          <Form.List name="products">
            {(fields, { add, remove }) => {
              const values = editorForm.getFieldValue('products') || []
              const dataSource = fields.map((f) => {
                const row = values?.[f.name] || {}
                const code = row?.productCode
                const info = code ? productInfoMap[code] : undefined
                return { key: code || String(f.key), field: f, code, supplierName: info?.localSupplierCode ? supplierNameMap[info.localSupplierCode] || info.localSupplierCode : '', itemNumber: info?.itemNumber || '', productName: info?.productName || '', retailPrice: info?.retailPrice }
              })
              const productColumns = [
                { title: '计数权重', dataIndex: 'unitWeight', width: 160, render: (_: any, record: any) => <Form.Item name={[record.field.name, 'unitWeight']} rules={[{ required: true, type: 'number', min: 1 }]} style={{ marginBottom: 0 }}><InputNumber min={1} style={{ width: 120 }} /></Form.Item> },
                { title: '供应商', dataIndex: 'supplierName', width: 180, render: (t: any) => t || '—' },
                { title: '货号', dataIndex: 'itemNumber', width: 180, render: (t: any) => t || '—' },
                { title: '名称', dataIndex: 'productName', render: (t: any) => t || '—' },
                { title: '零售价', dataIndex: 'retailPrice', width: 140, render: (v: number) => v != null ? Number(v).toFixed(2) : '—' },
                { title: '操作', key: 'actions', width: 120, render: (_: any, record: any) => <Button type="link" danger onClick={() => remove(record.field.name)}>删除</Button> },
              ]
              return (
                <>
                  <Table rowKey="key" dataSource={dataSource} columns={productColumns as any} pagination={false} />
                  <Form.Item style={{ marginTop: 8 }}>
                    <Button type="dashed" onClick={() => setPickerOpen(true)}>添加商品</Button>
                  </Form.Item>
                </>
              )
            }}
          </Form.List>
        </Form>
      </Modal>

      <ProductPicker
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        alreadySelectedCodes={(editorForm.getFieldValue('products') || []).map((x: any) => x?.productCode).filter((c: string) => !!c)}
        onPick={(code) => {
          const existing = (editorForm.getFieldValue('products') || []).map((x: any) => x?.productCode).filter((c: string) => !!c)
          if (existing.includes(code)) { message.warning('该商品已在当前策略中'); return }
          const fields = editorForm.getFieldValue('products') || []
          editorForm.setFieldsValue({ products: [...fields, { productCode: code, unitWeight: 1 }] })
          ;(async () => {
            try {
              const info = await getPosProductById(code)
              if (info?.productCode) setProductInfoMap((prev) => ({ ...prev, [info.productCode]: info }))
            } catch { /* ignore */ }
          })()
        }}
        onPickMany={(codes) => {
          if (!codes?.length) return
          const existing = (editorForm.getFieldValue('products') || []).map((x: any) => x?.productCode).filter((c: string) => !!c)
          const addCodes = codes.filter((c) => !existing.includes(c))
          if (!addCodes.length) { message.warning('选中的商品已全部在当前策略中'); return }
          const fields = editorForm.getFieldValue('products') || []
          editorForm.setFieldsValue({ products: [...fields, ...addCodes.map((code) => ({ productCode: code, unitWeight: 1 }))] })
          ;(async () => {
            await Promise.all(addCodes.map(async (code) => {
              try {
                const info = await getPosProductById(code)
                if (info?.productCode) setProductInfoMap((prev) => ({ ...prev, [info.productCode]: info }))
              } catch { /* ignore */ }
            }))
          })()
        }}
      />
    </Card>
  )
}
