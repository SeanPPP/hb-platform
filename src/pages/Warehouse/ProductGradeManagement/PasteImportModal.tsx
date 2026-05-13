import { Button, Form, Input, Modal, Select, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useState } from 'react'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { pasteImportGrades } from '../../../services/productGradeService'
import { PRODUCT_GRADE_CONFIG, type PasteImportPreviewItem, type PasteImportResult } from '../../../types/productGrade'

interface Props {
  open: boolean
  onClose: () => void
  onSuccess: () => void
}

const gradeOptions = Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => ({
  label: cfg.label,
  value: key,
}))

const GRADE_TAG_COLOR: Record<string, string> = {
  A: 'purple',
  B: 'blue',
  C: 'orange',
  D: 'red',
}

export default function PasteImportModal({ open, onClose, onSuccess }: Props) {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [previewData, setPreviewData] = useState<PasteImportResult | null>(null)
  const [suppliers, setSuppliers] = useState<Array<{ label: string; value: string }>>([])
  const [supplierLoading, setSupplierLoading] = useState(false)

  const loadSuppliers = async () => {
    setSupplierLoading(true)
    try {
      const result = await getActiveChinaSuppliers()
      setSuppliers(
        result.map((item) => ({
          label: `${item.supplierCode} - ${item.supplierName}`,
          value: item.supplierCode,
        })),
      )
    } catch {
      message.error('加载供应商列表失败')
    } finally {
      setSupplierLoading(false)
    }
  }

  const handleOpen = () => {
    void loadSuppliers()
    form.resetFields()
    setPreviewData(null)
  }

  const handleMatch = async () => {
    try {
      const values = await form.validateFields()
      setLoading(true)
      const result = await pasteImportGrades({
        supplierCode: values.supplierCode,
        productNumbers: values.productNumbers,
        grade: values.grade,
      })
      setPreviewData(result)
      if (result.matchedCount === 0) {
        message.warning('未匹配到任何商品，请检查供应商和货号')
      }
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      message.error(error instanceof Error ? error.message : '匹配失败')
    } finally {
      setLoading(false)
    }
  }

  const handleImport = () => {
    if (!previewData || previewData.matchedCount === 0) return
    message.success(
      `导入完成：新建 ${previewData.createdCount} 条，更新 ${previewData.updatedCount} 条`,
    )
    onSuccess()
    onClose()
  }

  const previewColumns: ColumnsType<PasteImportPreviewItem> = [
    { title: '货号', dataIndex: 'productNumber', width: 140 },
    {
      title: '状态',
      dataIndex: 'matched',
      width: 80,
      render: (matched: boolean) => (
        <Tag color={matched ? 'success' : 'error'}>{matched ? '已匹配' : '未匹配'}</Tag>
      ),
    },
    { title: '商品名称', dataIndex: 'productName', width: 200, ellipsis: true },
    {
      title: '当前等级',
      dataIndex: 'existingGrade',
      width: 100,
      render: (grade?: string) =>
        grade ? <Tag color={GRADE_TAG_COLOR[grade] || 'default'}>{grade}</Tag> : '--',
    },
  ]

  return (
    <Modal
      title="粘贴导入商品等级"
      open={open}
      onCancel={onClose}
      afterOpenChange={(visible) => {
        if (visible) handleOpen()
      }}
      width={800}
      footer={[
        <Button key="cancel" onClick={onClose}>
          关闭
        </Button>,
        <Button key="match" loading={loading} onClick={() => void handleMatch()}>
          匹配
        </Button>,
        <Button
          key="import"
          type="primary"
          disabled={!previewData || previewData.matchedCount === 0}
          onClick={handleImport}
        >
          确认导入
        </Button>,
      ]}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="supplierCode"
          label="供应商"
          rules={[{ required: true, message: '请选择供应商' }]}
        >
          <Select
            showSearch
            placeholder="选择供应商"
            options={suppliers}
            loading={supplierLoading}
            optionFilterProp="label"
          />
        </Form.Item>
        <Form.Item
          name="grade"
          label="目标等级"
          rules={[{ required: true, message: '请选择等级' }]}
        >
          <Select placeholder="选择等级" options={gradeOptions} />
        </Form.Item>
        <Form.Item
          name="productNumbers"
          label="货号列表"
          rules={[{ required: true, message: '请粘贴货号' }]}
        >
          <Input.TextArea
            rows={6}
            placeholder="粘贴货号，每行一个或用逗号/Tab分隔"
          />
        </Form.Item>
      </Form>

      {previewData && (
        <>
          <div style={{ marginBottom: 8, color: '#666', display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
            <span>共 <b>{previewData.totalCount}</b> 个货号</span>
            <Tag color="success">匹配 {previewData.matchedCount}</Tag>
            <Tag color="processing">新建 {previewData.createdCount}</Tag>
            <Tag color="warning">更新 {previewData.updatedCount}</Tag>
            {previewData.totalCount - previewData.matchedCount > 0 && (
              <Tag color="error">未匹配 {previewData.totalCount - previewData.matchedCount}</Tag>
            )}
          </div>
          <Table<PasteImportPreviewItem>
            rowKey="productNumber"
            size="small"
            columns={previewColumns}
            dataSource={previewData.previewItems}
            pagination={{ pageSize: 50, showSizeChanger: false }}
            scroll={{ y: 300 }}
          />
        </>
      )}
    </Modal>
  )
}
