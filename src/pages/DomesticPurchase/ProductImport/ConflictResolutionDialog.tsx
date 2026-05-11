import { Button, Modal, Radio, Space, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useState } from 'react'

interface ConflictItem {
  productCode: string
  existingPieces?: number
}

interface ConflictResolutionDialogProps {
  open: boolean
  conflicts: ConflictItem[]
  onClose: () => void
  onConfirm: (result: { global?: 'override' | 'increase'; perItem?: Record<string, 'override' | 'increase'> }) => void
}

export function ConflictResolutionDialog({ open, conflicts, onClose, onConfirm }: ConflictResolutionDialogProps) {
  const [globalStrategy, setGlobalStrategy] = useState<'override' | 'increase' | 'perItem'>('increase')
  const [perItemStrategy, setPerItemStrategy] = useState<Record<string, 'override' | 'increase'>>({})

  const columns: ColumnsType<ConflictItem> = [
    { title: '货号', dataIndex: 'productCode', key: 'productCode', width: 150 },
    { title: '已有件数', dataIndex: 'existingPieces', key: 'existingPieces', width: 100, render: (text) => text ?? '-' },
    {
      title: '处理策略',
      key: 'strategy',
      render: (_, record) => (
        <Radio.Group
          value={perItemStrategy[record.productCode] || 'increase'}
          onChange={(e) => setPerItemStrategy((prev) => ({ ...prev, [record.productCode]: e.target.value }))}
          disabled={globalStrategy !== 'perItem'}
        >
          <Radio value="increase">增加数量</Radio>
          <Radio value="override">覆盖</Radio>
        </Radio.Group>
      ),
    },
  ]

  const handleConfirm = () => {
    if (globalStrategy === 'perItem') {
      onConfirm({ perItem: perItemStrategy })
    } else {
      onConfirm({ global: globalStrategy })
    }
  }

  return (
    <Modal title="货柜冲突处理" open={open} onCancel={onClose} width={700} footer={<Space><Button onClick={onClose}>取消</Button><Button type="primary" onClick={handleConfirm}>确认</Button></Space>}>
      <div style={{ marginBottom: 16 }}>
        <Radio.Group value={globalStrategy} onChange={(e) => setGlobalStrategy(e.target.value)}>
          <Radio value="increase">全部增加数量</Radio>
          <Radio value="override">全部覆盖</Radio>
          <Radio value="perItem">逐项选择</Radio>
        </Radio.Group>
      </div>
      <Table columns={columns} dataSource={conflicts} rowKey="productCode" size="small" pagination={false} />
    </Modal>
  )
}
