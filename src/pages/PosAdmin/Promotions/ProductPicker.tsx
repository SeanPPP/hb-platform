import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import { getPosProducts, type PosProductDto } from '../../../services/posProductService'
import { Button, Form, Input, message, Modal, Select, Space, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useState } from 'react'

type RowType = PosProductDto & { key: string }

interface ProductPickerProps {
  open: boolean
  onClose: () => void
  onPick: (productCode: string) => void
  alreadySelectedCodes?: string[]
  onPickMany?: (codes: string[]) => void
}

export default function ProductPicker({ open, onClose, onPick, alreadySelectedCodes = [], onPickMany }: ProductPickerProps) {
  const [loading, setLoading] = useState(false)
  const [form] = Form.useForm()
  const [data, setData] = useState<RowType[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [supplierNameMap, setSupplierNameMap] = useState<Record<string, string>>({})
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  const load = async (p?: number, ps?: number) => {
    try {
      setLoading(true)
      const fv = form.getFieldsValue()
      const result = await getPosProducts({
        search: [fv?.itemNumber, fv?.productName].filter(Boolean).join(' ') || undefined,
        localSupplierCode: fv?.supplier || undefined,
        pageNumber: p ?? page,
        pageSize: ps ?? pageSize,
      })
      const rows = (result?.items ?? []).map((x, idx) => ({ ...x, key: x.productCode ?? String(idx) } as RowType))
      setTotal(result?.total ?? 0)
      setData(rows)
      setSelectedRowKeys(rows.filter((r) => alreadySelectedCodes.includes(r.productCode)).map((r) => r.key))
    } catch {
      message.error('加载失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (open) {
      ;(async () => {
        try {
          const suppliers = await getActiveLocalSuppliers()
          const map: Record<string, string> = {}
          const opts: { label: string; value: string }[] = []
          suppliers.forEach((s) => {
            if (s.localSupplierCode) {
              map[s.localSupplierCode] = s.name || s.localSupplierCode
              opts.push({ label: s.name || s.localSupplierCode, value: s.localSupplierCode })
            }
          })
          setSupplierNameMap(map)
          setSupplierOptions(opts)
        } catch { /* ignore */ }
        load(1, pageSize)
        setPage(1)
      })()
    }
  }, [open])

  const columns: ColumnsType<RowType> = [
    { title: '供应商', dataIndex: 'localSupplierCode', width: 180, render: (code: string) => supplierNameMap[code] || code || '—' },
    { title: '货号', dataIndex: 'itemNumber', width: 160, render: (_: any, r) => r.itemNumber || r.productCode || '—' },
    { title: '商品名称', dataIndex: 'productName' },
    { title: '进货价', dataIndex: 'purchasePrice', width: 120, render: (v: number) => (v ?? 0).toFixed(2) },
    { title: '零售价', dataIndex: 'retailPrice', width: 120, render: (v: number) => (v ?? 0).toFixed(2) },
  ]

  return (
    <Modal open={open} title="选择商品" onCancel={onClose} footer={null} width={900}>
      <Form form={form} layout="inline" onFinish={() => { setPage(1); load(1, pageSize) }} style={{ marginBottom: 8 }}>
        <Form.Item name="supplier" label="供应商"><Select allowClear showSearch optionFilterProp="label" options={supplierOptions} style={{ width: 220 }} /></Form.Item>
        <Form.Item name="itemNumber" label="货号"><Input allowClear placeholder="按货号" style={{ width: 180 }} /></Form.Item>
        <Form.Item name="productName" label="商品名称"><Input allowClear placeholder="按名称" style={{ width: 180 }} /></Form.Item>
        <Form.Item><Space><Button type="primary" htmlType="submit">搜索</Button><Button onClick={() => { form.resetFields(); setPage(1); load(1, pageSize) }}>重置</Button></Space></Form.Item>
      </Form>
      <Table
        rowKey="key"
        rowSelection={{
          selectedRowKeys,
          onChange: (keys) => setSelectedRowKeys(keys),
          preserveSelectedRowKeys: true,
          getCheckboxProps: (record: any) => ({ disabled: alreadySelectedCodes.includes(record.productCode) }),
        }}
        onRow={(record) => ({
          onDoubleClick: () => {
            if (alreadySelectedCodes.includes(record.productCode)) { message.warning('该商品已在当前策略中') }
            else { onPick(record.productCode); onClose() }
          },
        })}
        loading={loading}
        dataSource={data}
        columns={columns}
        pagination={{ total, current: page, pageSize, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100', '200'] }}
        onChange={(pagination) => { setPage(pagination.current ?? 1); setPageSize(pagination.pageSize ?? 50); load(pagination.current, pagination.pageSize) }}
      />
      <Space style={{ marginTop: 8 }}>
        <Button type="primary" disabled={selectedRowKeys.length === 0} onClick={() => {
          const codes = data.filter((r) => selectedRowKeys.includes(r.key)).map((r) => r.productCode).filter((c) => !alreadySelectedCodes.includes(c))
          if (!codes.length) { message.warning('没有可新增的商品'); return }
          onPickMany?.(codes)
          onClose()
        }}>选择所选</Button>
        <Button onClick={onClose}>关闭</Button>
      </Space>
    </Modal>
  )
}
